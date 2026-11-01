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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_connectionFactory is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            _connection = _connectionFactory.CreateConnection();
            _channel = _connection.CreateModel();

            // 1. Khởi tạo Dead Letter Exchange (DLX) và Dead Letter Queue (DLQ)
            _channel.ExchangeDeclare(exchange: _dlxExchangeName, type: ExchangeType.Topic, durable: true);
            _channel.QueueDeclare(queue: _dlqQueueName, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(queue: _dlqQueueName, exchange: _dlxExchangeName, routingKey: _routingKey);

            // 2. Khởi tạo Exchange chính và Queue chính với cấu hình gắn DLX
            _channel.ExchangeDeclare(exchange: _exchangeName, type: ExchangeType.Topic, durable: true);

            var queueArgs = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", _dlxExchangeName },
                { "x-dead-letter-routing-key", _routingKey }
            };

            _channel.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs
            );

            _channel.QueueBind(queue: _queueName, exchange: _exchangeName, routingKey: _routingKey);

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
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi xử lý sự kiện từ queue {QueueName}. Tự động đẩy vào DLQ ({DlxExchange}).", _queueName, _dlxExchangeName);
                    // NACK với requeue = false để RabbitMQ tự động đẩy message sang DLX/DLQ
                    _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
            _logger.LogInformation("Khởi động consumer thành công cho queue '{QueueName}' (RoutingKey: '{RoutingKey}') kèm DLQ '{Dlq}'.",
                _queueName, _routingKey, _dlqQueueName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể kết nối RabbitMQ để lắng nghe queue {QueueName}. Consumer sẽ tự động thử lại khi có broker.", _queueName);
        }

        return Task.CompletedTask;
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
