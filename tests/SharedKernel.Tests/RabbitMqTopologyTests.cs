using SharedKernel.Messaging;

namespace SharedKernel.Tests;

public class RabbitMqTopologyTests
{
    [Fact]
    public void T21_DeclareConsumerQueue_WalletAndConsumer_GenerateSameArguments()
    {
        // Act: Gọi helper 2 lần cho queue transaction.money-transferred
        var walletSideArgs = RabbitMqTopology.DeclareConsumerQueue(
            channel: null,
            queueName: "transaction.money-transferred",
            routingKey: "wallet.transferred"
        );

        var consumerSideArgs = RabbitMqTopology.DeclareConsumerQueue(
            channel: null,
            queueName: "transaction.money-transferred",
            routingKey: "wallet.transferred"
        );

        // Assert: So sánh hai bộ arguments phải đồng nhất 100% từng key & value
        Assert.NotNull(walletSideArgs);
        Assert.NotNull(consumerSideArgs);
        Assert.Equal(walletSideArgs.Count, consumerSideArgs.Count);

        foreach (var kvp in walletSideArgs)
        {
            Assert.True(consumerSideArgs.ContainsKey(kvp.Key), $"Missing key: {kvp.Key}");
            Assert.Equal(kvp.Value, consumerSideArgs[kvp.Key]);
        }

        Assert.True(walletSideArgs.ContainsKey("x-dead-letter-exchange"));
        Assert.Equal("qlct.events.dlx", walletSideArgs["x-dead-letter-exchange"]);
    }

    [Fact]
    public void X09_GetRetryQueueArguments_DeadLettersOnlyToOwnQueue()
    {
        var retryArgs = RabbitMqTopology.GetRetryQueueArguments("profile.user-registered");

        Assert.Equal(5000, retryArgs["x-message-ttl"]);
        Assert.Equal("", retryArgs["x-dead-letter-exchange"]);
        Assert.Equal("profile.user-registered", retryArgs["x-dead-letter-routing-key"]);
    }

    [Fact]
    public void H10_Consumer_RetryExceededMaxAttempts_GoesToDlq()
    {
        // Lần 1: retry = 0 -> next = 1 (tiếp tục retry)
        Assert.True(RabbitMqRetryHelper.ShouldRetry(0, out var next1));
        Assert.Equal(1, next1);

        // Lần 2: retry = 1 -> next = 2
        Assert.True(RabbitMqRetryHelper.ShouldRetry(1, out var next2));
        Assert.Equal(2, next2);

        // Lần 3: retry = 2 -> next = 3
        Assert.True(RabbitMqRetryHelper.ShouldRetry(2, out var next3));
        Assert.Equal(3, next3);

        // Lần 4: retry = 3 -> ĐÃ ĐẠT GIỚI HẠN (không lặp vô hạn, chuyển DLQ)
        Assert.False(RabbitMqRetryHelper.ShouldRetry(3, out var next4));
        Assert.Equal(3, next4);

        // Lần 5: retry > 3 -> vẫn false
        Assert.False(RabbitMqRetryHelper.ShouldRetry(4, out var next5));
        Assert.Equal(4, next5);
    }

    [Fact]
    public void RabbitMqRetryHelper_GetRetryCount_HandlesVariousHeaderTypes()
    {
        Assert.Equal(0, RabbitMqRetryHelper.GetRetryCount(null));
        Assert.Equal(0, RabbitMqRetryHelper.GetRetryCount(new Dictionary<string, object>()));

        var headersInt = new Dictionary<string, object> { { RabbitMqRetryHelper.RetryCountHeader, 2 } };
        Assert.Equal(2, RabbitMqRetryHelper.GetRetryCount(headersInt));

        var headersBytes = new Dictionary<string, object> { { RabbitMqRetryHelper.RetryCountHeader, System.Text.Encoding.UTF8.GetBytes("3") } };
        Assert.Equal(3, RabbitMqRetryHelper.GetRetryCount(headersBytes));
    }
}

