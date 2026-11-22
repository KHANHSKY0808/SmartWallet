using RabbitMQ.Client;

namespace SharedKernel.Messaging;

public static class RabbitMqTopology
{
    public const string DefaultExchange = "qlct.events";
    public const int DefaultRetryTtlMs = 5000;

    public static Dictionary<string, object> GetMainQueueArguments(string exchangeName = DefaultExchange, string? routingKey = null)
    {
        var dlx = $"{exchangeName}.dlx";
        var dict = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", dlx }
        };
        if (!string.IsNullOrEmpty(routingKey))
        {
            dict["x-dead-letter-routing-key"] = routingKey;
        }
        return dict;
    }

    public static Dictionary<string, object> GetRetryQueueArguments(string exchangeName = DefaultExchange, string routingKey = "", int ttlMs = DefaultRetryTtlMs)
    {
        return new Dictionary<string, object>
        {
            { "x-message-ttl", ttlMs },
            { "x-dead-letter-exchange", exchangeName },
            { "x-dead-letter-routing-key", routingKey }
        };
    }

    public static Dictionary<string, object> DeclareConsumerQueue(
        IModel? channel,
        string queueName,
        string routingKey,
        string exchangeName = DefaultExchange)
    {
        var dlxExchange = $"{exchangeName}.dlx";
        var dlqQueue = $"{queueName}.dlq";
        var retryQueue = $"{queueName}.retry";

        var retryArgs = GetRetryQueueArguments(exchangeName, routingKey);
        var mainArgs = GetMainQueueArguments(exchangeName, routingKey);

        if (channel != null)
        {
            // 1. Khai báo Dead Letter Exchange (DLX) và Dead Letter Queue (DLQ)
            channel.ExchangeDeclare(exchange: dlxExchange, type: ExchangeType.Topic, durable: true);
            channel.QueueDeclare(queue: dlqQueue, durable: true, exclusive: false, autoDelete: false);
            channel.QueueBind(queue: dlqQueue, exchange: dlxExchange, routingKey: routingKey);

            // 2. Khai báo Exchange chính và Retry Queue kèm TTL
            channel.ExchangeDeclare(exchange: exchangeName, type: ExchangeType.Topic, durable: true);
            channel.QueueDeclare(queue: retryQueue, durable: true, exclusive: false, autoDelete: false, arguments: retryArgs);

            // 3. Khai báo Queue chính gắn Dead Letter Exchange
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: mainArgs);
            channel.QueueBind(queue: queueName, exchange: exchangeName, routingKey: routingKey);
        }

        return mainArgs;
    }
}

