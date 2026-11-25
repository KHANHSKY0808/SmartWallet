using System.Text;
using AuditService.Data;
using AuditService.Services;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedKernel.Messaging;

namespace AuditService.Consumers;

/// <summary>D63: Consumer bind wildcard "#" trên exchange qlct.events để lưu mọi sự kiện vào AuditLogs.</summary>
public sealed class AuditAllConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditAllConsumer> _logger;
    private readonly ConnectionFactory? _connectionFactory;
    private readonly string _exchangeName;
    private readonly string _queueName = "audit.all";
    private readonly string _routingKey = "#";
    private IConnection? _connection;
    private IModel? _channel;

    public AuditAllConsumer(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<AuditAllConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _exchangeName = configuration["RabbitMQ:Exchange"] ?? "qlct.events";

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
            _logger.LogWarning(ex, "Không thể khởi tạo RabbitMQ ConnectionFactory cho AuditAllConsumer");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        if (_connectionFactory is null) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _connection = _connectionFactory.CreateConnection();
                _channel = _connection.CreateModel();

                RabbitMqTopology.DeclareConsumerQueue(_channel, _queueName, _routingKey, _exchangeName);
                _channel.BasicQos(prefetchSize: 0, prefetchCount: 20, global: false); // D63: prefetch <= 20

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += async (_, ea) =>
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    try
                    {
                        await HandleRawMessageAsync(
                            json,
                            ea.RoutingKey,
                            ea.BasicProperties?.MessageId,
                            ea.BasicProperties?.CorrelationId,
                            ea.BasicProperties?.Type,
                            stoppingToken);
                        _channel.BasicAck(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "AuditService lỗi khi xử lý message từ routing key {RoutingKey}", ea.RoutingKey);
                        var retries = RabbitMqRetryHelper.GetRetryCount(ea.BasicProperties?.Headers);
                        if (RabbitMqRetryHelper.ShouldRetry(retries, out var nextRetries))
                        {
                            var props = _channel.CreateBasicProperties();
                            props.Persistent = true;
                            props.MessageId = ea.BasicProperties?.MessageId;
                            props.CorrelationId = ea.BasicProperties?.CorrelationId;
                            props.Type = ea.BasicProperties?.Type;
                            var headers = ea.BasicProperties?.Headers != null
                                ? new Dictionary<string, object>(ea.BasicProperties.Headers)
                                : new Dictionary<string, object>();
                            headers[RabbitMqRetryHelper.RetryCountHeader] = nextRetries;
                            props.Headers = headers;

                            _channel.BasicPublish("", $"{_queueName}.retry", props, ea.Body);
                            _channel.BasicAck(ea.DeliveryTag, multiple: false);
                        }
                        else
                        {
                            _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                        }
                    }
                };

                _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
                _logger.LogInformation("AuditAllConsumer đã lắng nghe tất cả sự kiện (#) trên queue {Queue}", _queueName);

                while (!stoppingToken.IsCancellationRequested && _connection.IsOpen && _channel.IsOpen)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AuditAllConsumer mất kết nối RabbitMQ, thử lại sau 5 giây...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    public async Task HandleRawMessageAsync(
        string rawJson,
        string? routingKey,
        string? messageId,
        string? correlationId,
        string? headerEventType,
        CancellationToken cancellationToken = default)
    {
        var log = AuditEventParser.ParseAndMask(rawJson, routingKey, messageId, correlationId, headerEventType);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        if (log.EventId.HasValue && await context.AuditLogs.AnyAsync(a => a.EventId == log.EventId, cancellationToken))
        {
            return; // Idempotent theo EventId
        }

        context.AuditLogs.Add(log);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Bắt trường hợp trùng EventId đồng thời
        }
    }

    public override void Dispose()
    {
        try { _channel?.Dispose(); } catch { }
        try { _connection?.Dispose(); } catch { }
        base.Dispose();
    }
}

