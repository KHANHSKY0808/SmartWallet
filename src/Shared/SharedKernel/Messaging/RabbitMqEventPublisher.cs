using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using SharedKernel.Events;

namespace SharedKernel.Messaging;

public class RabbitMqEventPublisher : IEventPublisher, IBatchRawEventPublisher, IDisposable
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
            DispatchConsumersAsync = true,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(3)
        };
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

                using var channel = _connection.CreateModel();

                channel.ExchangeDeclare(
                    exchange: _exchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false
                );

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
        var json = JsonSerializer.Serialize(@event, @event.GetType());
        var cid = SharedKernel.Extensions.CorrelationContext.CurrentCorrelationId ?? @event.Id.ToString();
        return PublishRawAsync(@event.Id, @event.EventType, routingKey, json, cid, @event.OccurredAt, cancellationToken);
    }

    public Task PublishRawAsync(
        Guid eventId,
        string eventType,
        string routingKey,
        string payloadJson,
        string? correlationId = null,
        DateTime? occurredAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureConnection())
        {
            _logger.LogWarning("Không thể publish sự kiện {EventType} ({RoutingKey}): Kết nối RabbitMQ không khả dụng.", eventType, routingKey);
            throw new InvalidOperationException($"Không thể publish sự kiện {eventType}: Mất kết nối tới RabbitMQ.");
        }

        try
        {
            using var channel = _connection!.CreateModel();

            // Kích hoạt Publisher Confirms để đảm bảo message đã an toàn trên Broker
            channel.ConfirmSelect();

            PublishOnChannel(channel, eventId, eventType, routingKey, payloadJson, correlationId, occurredAtUtc);

            // Chờ broker xác nhận (Publisher Confirm) với timeout 5 giây
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

            var cid = ResolveCorrelationId(eventId, correlationId);
            _logger.LogInformation("Published sự kiện {EventType} (ID: {EventId}, CorrelationId: {CorrelationId}) thành công tới Exchange '{Exchange}' với RoutingKey '{RoutingKey}' (Publisher Confirmed).",
                eventType, eventId, cid, _exchangeName, routingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi publish sự kiện {EventType} (ID: {EventId}) tới RabbitMQ", eventType, eventId);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task PublishBatchRawAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return Task.CompletedTask;

        if (!EnsureConnection())
        {
            _logger.LogWarning("Không thể publish lô {Count} sự kiện Outbox: Kết nối RabbitMQ không khả dụng.", messages.Count);
            throw new InvalidOperationException("Không thể publish lô sự kiện Outbox: Mất kết nối tới RabbitMQ.");
        }

        try
        {
            using var channel = _connection!.CreateModel();
            channel.ConfirmSelect();

            foreach (var msg in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PublishOnChannel(channel, msg.Id, msg.Type, msg.RoutingKey, msg.Payload, msg.CorrelationId, msg.CreatedAt);
            }

            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(10));
            _logger.LogInformation("Published lô {Count} sự kiện Outbox thành công tới Exchange '{Exchange}' (Batch Publisher Confirmed).",
                messages.Count, _exchangeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi publish lô {Count} sự kiện Outbox tới RabbitMQ", messages.Count);
            throw;
        }

        return Task.CompletedTask;
    }

    private static string ResolveCorrelationId(Guid eventId, string? correlationId)
        => !string.IsNullOrWhiteSpace(correlationId)
            ? correlationId
            : (SharedKernel.Extensions.CorrelationContext.CurrentCorrelationId ?? eventId.ToString());

    private void PublishOnChannel(
        IModel channel,
        Guid eventId,
        string eventType,
        string routingKey,
        string payloadJson,
        string? correlationId,
        DateTime? occurredAtUtc)
    {
        var body = Encoding.UTF8.GetBytes(payloadJson);
        var cid = ResolveCorrelationId(eventId, correlationId);
        var ts = occurredAtUtc ?? DateTime.UtcNow;

        var properties = channel.CreateBasicProperties();
        properties.DeliveryMode = 2; // Persistent: Lưu trữ message trên đĩa
        properties.MessageId = eventId.ToString();
        properties.CorrelationId = cid;
        properties.Type = eventType;
        properties.Timestamp = new AmqpTimestamp(new DateTimeOffset(DateTime.SpecifyKind(ts, DateTimeKind.Utc)).ToUnixTimeSeconds());
        properties.Headers = new Dictionary<string, object>
        {
            { "X-Correlation-ID", cid },
            { "correlation-id", cid },
            { "event-type", eventType }
        };

        channel.BasicPublish(
            exchange: _exchangeName,
            routingKey: routingKey,
            basicProperties: properties,
            body: body
        );
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
