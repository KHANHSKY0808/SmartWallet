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
    private readonly ConnectionFactory _connectionFactory;
    private readonly string _exchangeName;
    private readonly string _dlxExchangeName;
    private readonly object _connectionLock = new();
    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqEventPublisher(IConfiguration configuration, ILogger<RabbitMqEventPublisher> logger)
    {
        _logger = logger;
        _exchangeName = configuration["RabbitMQ:Exchange"] ?? "qlct.events";
        _dlxExchangeName = $"{_exchangeName}.dlx";

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

        EnsureConnection();
    }

    private bool EnsureConnection()
    {
        if (_connection is { IsOpen: true })
        {
            return true;
        }

        lock (_connectionLock)
        {
            if (_connection is { IsOpen: true })
            {
                return true;
            }

            try
            {
                _connection?.Dispose();
                _connection = _connectionFactory.CreateConnection();

                // Tạo channel tạm thời để khởi tạo Exchanges
                using var channel = _connection.CreateModel();

                // 1. Exchange chính
                channel.ExchangeDeclare(
                    exchange: _exchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false
                );

                // 2. Dead Letter Exchange (DLX)
                channel.ExchangeDeclare(
                    exchange: _dlxExchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false
                );

                _logger.LogInformation("Kết nối thành công tới RabbitMQ broker và khởi tạo Exchange '{Exchange}' + DLX '{Dlx}'.", _exchangeName, _dlxExchangeName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể kết nối RabbitMQ ({Host}:{Port}). Sẽ thử kết nối lại khi có yêu cầu publish.", _connectionFactory.HostName, _connectionFactory.Port);
                return false;
            }
        }
    }

    public Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        if (!EnsureConnection())
        {
            _logger.LogError("Không thể publish sự kiện {EventType} ({RoutingKey}): Kết nối RabbitMQ không khả dụng.", @event.EventType, routingKey);
            throw new InvalidOperationException($"Không thể publish sự kiện {@event.EventType}: Mất kết nối tới RabbitMQ.");
        }

        try
        {
            // Thread-Safe: Tạo channel riêng biệt cho từng thao tác publish và đóng lại khi xong
            using var channel = _connection!.CreateModel();

            // Kích hoạt Publisher Confirms để đảm bảo message đã an toàn trên Broker
            channel.ConfirmSelect();

            var json = JsonSerializer.Serialize(@event, @event.GetType());
            var body = Encoding.UTF8.GetBytes(json);

            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2; // Persistent: Lưu trữ message trên đĩa
            properties.MessageId = @event.Id.ToString();
            properties.Type = @event.EventType;
            properties.Timestamp = new AmqpTimestamp(new DateTimeOffset(@event.OccurredAt).ToUnixTimeSeconds());
            properties.Headers = new Dictionary<string, object>
            {
                { "correlation-id", @event.Id.ToString() },
                { "event-type", @event.EventType }
            };

            channel.BasicPublish(
                exchange: _exchangeName,
                routingKey: routingKey,
                basicProperties: properties,
                body: body
            );

            // Chờ broker xác nhận (Publisher Confirm) với timeout 5 giây
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

            _logger.LogInformation("Published sự kiện {EventType} (ID: {EventId}) thành công tới Exchange '{Exchange}' với RoutingKey '{RoutingKey}' (Publisher Confirmed).",
                @event.EventType, @event.Id, _exchangeName, routingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi publish sự kiện {EventType} (ID: {EventId}) tới RabbitMQ", @event.EventType, @event.Id);
            throw;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _connection?.Close();
            _connection?.Dispose();
        }
        catch
        {
            // Ignore on dispose
        }
    }
}
