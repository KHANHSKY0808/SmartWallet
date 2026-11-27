using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedKernel.Events;

namespace SharedKernel.Messaging;

public abstract class BaseRabbitMqConsumer<TEvent> : BackgroundService where TEvent : IntegrationEvent
{
    private readonly ILogger _logger;
    private readonly string _queueName;
    private readonly string _routingKey;
    private readonly string _exchangeName;
    private readonly string _dlxExchangeName;
    private readonly string _dlqQueueName;
    private readonly ConnectionFactory? _connectionFactory;
    private IConnection? _connection;
    private IModel? _channel;

    protected BaseRabbitMqConsumer(
        IConfiguration configuration,
        ILogger logger,
        string queueName,
        string routingKey)
    {
        _logger = logger;
        _queueName = queueName;
        _routingKey = routingKey;
        _exchangeName = configuration["RabbitMQ:Exchange"] ?? "qlct.events";
        _dlxExchangeName = $"{_exchangeName}.dlx";
        _dlqQueueName = $"{_queueName}.dlq";

        try
        {
            _connectionFactory = new ConnectionFactory
            {
                HostName = configuration["RabbitMQ:Host"] ?? "localhost",
                Port = int.TryParse(configuration["RabbitMQ:Port"], out var port) ? port : 5672,
                UserName = configuration["RabbitMQ:Username"] ?? "guest",
                Password = configuration["RabbitMQ:Password"] ?? "guest",
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                DispatchConsumersAsync = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi khởi tạo cấu hình RabbitMQ Consumer cho queue {QueueName}", _queueName);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // O2: Nhường quyền thực thi để không block tiến trình khởi động đồng bộ của host
        await Task.Yield();

        if (_connectionFactory is null)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _connection = _connectionFactory.CreateConnection();
                _channel = _connection.CreateModel();

                // Khởi tạo topology RabbitMQ chuẩn (DLX, DLQ, Retry queue TTL, Main queue) thông qua helper dùng chung
                RabbitMqTopology.DeclareConsumerQueue(_channel, _queueName, _routingKey, _exchangeName);

                // Giới hạn prefetch để xử lý công bằng giữa các consumer instances
                _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += async (sender, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);

                    try
                    {
                        var @event = JsonSerializer.Deserialize<TEvent>(json);
                        if (@event is null)
                        {
                            _logger.LogWarning("Nhận message rỗng hoặc không thể parse JSON trên queue {QueueName}. Chuyển vào DLQ.", _queueName);
                            _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                            return;
                        }

                        var eventId = @event.Id.ToString();
                        var cid = ea.BasicProperties?.CorrelationId;
                        if (string.IsNullOrWhiteSpace(cid) && ea.BasicProperties?.Headers != null)
                        {
                            if (ea.BasicProperties.Headers.TryGetValue("X-Correlation-ID", out var xcid) && xcid is byte[] xbytes)
                            {
                                cid = Encoding.UTF8.GetString(xbytes);
                            }
                            else if (ea.BasicProperties.Headers.TryGetValue("correlation-id", out var rcid) && rcid is byte[] rbytes)
                            {
                                cid = Encoding.UTF8.GetString(rbytes);
                            }
                        }
                        cid ??= eventId;

                        SharedKernel.Extensions.CorrelationContext.CurrentCorrelationId = cid;
                        using var _cidProp = Serilog.Context.LogContext.PushProperty("CorrelationId", cid);
                        _logger.LogInformation("Consuming {EventType} (ID: {EventId}, CorrelationId: {CorrelationId}) on queue {QueueName}.",
                            @event.EventType, eventId, cid, _queueName);

                        // Hook Idempotency Check: Ngăn chặn xử lý trùng lặp sự kiện
                        if (await IsEventProcessedAsync(eventId, stoppingToken))
                        {
                            _logger.LogWarning("Sự kiện {EventType} (ID: {EventId}) đã được xử lý trước đó. Bỏ qua và ACK.", @event.EventType, eventId);
                            _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                            return;
                        }

                        // Thực thi nghiệp vụ domain
                        await ProcessEventAsync(@event, stoppingToken);

                        // Hook Idempotency Mark: Lưu vết đã xử lý thành công
                        await MarkEventProcessedAsync(eventId, stoppingToken);

                        // Xác nhận xử lý thành công (ACK)
                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                        SharedKernel.Extensions.CorrelationContext.CurrentCorrelationId = null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý sự kiện từ queue {QueueName}.", _queueName);

                        // D15: Retry tối đa 3 lần qua retry queue có TTL, sau đó đẩy sang DLQ
                        var currentRetries = RabbitMqRetryHelper.GetRetryCount(ea.BasicProperties?.Headers);
                        if (RabbitMqRetryHelper.ShouldRetry(currentRetries, out var nextRetries))
                        {
                            _logger.LogWarning("Thử lại xử lý sự kiện (Lần {NextRetries}/{MaxRetries}) qua retry queue cho queue {QueueName}.",
                                nextRetries, RabbitMqRetryHelper.MaxRetryAttempts, _queueName);

                            var retryProps = _channel.CreateBasicProperties();
                            retryProps.Persistent = true;
                            if (!string.IsNullOrEmpty(ea.BasicProperties?.MessageId))
                            {
                                retryProps.MessageId = ea.BasicProperties.MessageId;
                            }
                            if (!string.IsNullOrEmpty(ea.BasicProperties?.CorrelationId))
                            {
                                retryProps.CorrelationId = ea.BasicProperties.CorrelationId;
                            }
                            var headers = ea.BasicProperties?.Headers != null
                                ? new Dictionary<string, object>(ea.BasicProperties.Headers)
                                : new Dictionary<string, object>();
                            headers[RabbitMqRetryHelper.RetryCountHeader] = nextRetries;
                            retryProps.Headers = headers;

                            _channel.BasicPublish(
                                exchange: "",
                                routingKey: $"{_queueName}.retry",
                                basicProperties: retryProps,
                                body: ea.Body
                            );

                            _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                        }
                        else
                        {
                            _logger.LogWarning("Đã thử lại {MaxRetries} lần thất bại trên queue {QueueName}. Đẩy message vào DLQ ({DlxExchange}).",
                                RabbitMqRetryHelper.MaxRetryAttempts, _queueName, _dlxExchangeName);

                            _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                        }
                    }
                };

                _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
                _logger.LogInformation("Khởi động consumer thành công cho queue '{QueueName}' (RoutingKey: '{RoutingKey}') kèm DLQ '{Dlq}'.",
                    _queueName, _routingKey, _dlqQueueName);

                // Giữ kết nối hoạt động cho tới khi cancellation token hủy hoặc mất kết nối
                var tcs = new TaskCompletionSource<bool>();
                using (stoppingToken.Register(() => tcs.TrySetResult(true)))
                {
                    _connection.ConnectionShutdown += (s, e) =>
                    {
                        tcs.TrySetResult(false);
                    };
                    await tcs.Task;
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể kết nối RabbitMQ để lắng nghe queue {QueueName}. Sẽ thử lại sau 5 giây.", _queueName);
            }

            try
            {
                _channel?.Dispose();
                _channel = null;
                _connection?.Dispose();
                _connection = null;
            }
            catch { }

            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Hook Idempotency: Kiểm tra sự kiện đã từng được xử lý thành công chưa (tra cứu CSDL bảng ProcessedEvents).
    /// Các service con có thể override để kết nối DbContext tương ứng.
    /// </summary>
    protected virtual Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// Hook Idempotency: Đánh dấu sự kiện đã được xử lý thành công vào CSDL.
    /// </summary>
    protected virtual Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected abstract Task ProcessEventAsync(TEvent @event, CancellationToken cancellationToken);

    public override void Dispose()
    {
        try
        {
            _channel?.Close();
            _channel?.Dispose();
            _connection?.Close();
            _connection?.Dispose();
        }
        catch
        {
            // Ignore on dispose
        }
        base.Dispose();
    }
}
