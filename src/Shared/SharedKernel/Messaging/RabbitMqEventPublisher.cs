using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using SharedKernel.Events;

namespace SharedKernel.Messaging;

public class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly IConnection? _connection;
    private readonly IModel? _channel;
    private readonly string _exchangeName;
    private bool _disposed;

    public RabbitMqEventPublisher(IConfiguration configuration, ILogger<RabbitMqEventPublisher> logger)
    {
        _logger = logger;
        _exchangeName = configuration["RabbitMQ:Exchange"] ?? "qlct.events";

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = configuration["RabbitMQ:Host"] ?? "localhost",
                Port = int.TryParse(configuration["RabbitMQ:Port"], out var port) ? port : 5672,
                UserName = configuration["RabbitMQ:Username"] ?? "guest",
                Password = configuration["RabbitMQ:Password"] ?? "guest",
                DispatchConsumersAsync = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare(
                exchange: _exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể kết nối RabbitMQ tại thời điểm khởi tạo Publisher. Chế độ fallback được kích hoạt.");
        }
    }

    public Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        if (_channel is null || !_channel.IsOpen)
        {
            _logger.LogWarning("RabbitMQ channel không khả dụng. Bỏ qua publish sự kiện {EventType} ({RoutingKey}).", @event.EventType, routingKey);
            return Task.CompletedTask;
        }

        try
        {
            var json = JsonSerializer.Serialize(@event, @event.GetType());
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel.CreateBasicProperties();
            properties.DeliveryMode = 2; // Persistent
            properties.MessageId = @event.Id.ToString();
            properties.Type = @event.EventType;
            properties.Timestamp = new AmqpTimestamp(new DateTimeOffset(@event.OccurredAt).ToUnixTimeSeconds());

            _channel.BasicPublish(
                exchange: _exchangeName,
                routingKey: routingKey,
                basicProperties: properties,
                body: body
            );

            _logger.LogInformation("Đã publish sự kiện {EventType} (ID: {EventId}) với routing key: {RoutingKey}", @event.EventType, @event.Id, routingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi publish sự kiện {EventType} lên RabbitMQ", @event.EventType);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

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
    }
}

