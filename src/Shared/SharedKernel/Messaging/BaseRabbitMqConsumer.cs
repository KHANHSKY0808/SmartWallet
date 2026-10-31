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

        try
        {
            _connectionFactory = new ConnectionFactory
            {
                HostName = configuration["RabbitMQ:Host"] ?? "localhost",
                Port = int.TryParse(configuration["RabbitMQ:Port"], out var port) ? port : 5672,
                UserName = configuration["RabbitMQ:Username"] ?? "guest",
                Password = configuration["RabbitMQ:Password"] ?? "guest",
                DispatchConsumersAsync = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi cấu hình RabbitMQ Consumer cho queue {QueueName}", _queueName);
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

            _channel.ExchangeDeclare(exchange: _exchangeName, type: ExchangeType.Topic, durable: true);
            _channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(queue: _queueName, exchange: _exchangeName, routingKey: _routingKey);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (sender, ea) =>
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);

                try
                {
                    var @event = JsonSerializer.Deserialize<TEvent>(json);
                    if (@event != null)
                    {
                        await ProcessEventAsync(@event, stoppingToken);
                    }
                    _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi xử lý message từ queue {QueueName}", _queueName);
                    // Requeue hoặc nack tùy theo policy (để đơn giản nack false không requeue nếu lỗi lặp lại)
                    _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
            _logger.LogInformation("Đã khởi động consumer cho queue {QueueName} với routing key {RoutingKey}", _queueName, _routingKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể kết nối RabbitMQ để lắng nghe queue {QueueName}", _queueName);
        }

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

