namespace SharedKernel.Messaging;

public static class RabbitMqRetryHelper
{
    public const int MaxRetryAttempts = 3;
    public const string RetryCountHeader = "x-retry-count";

    public static int GetRetryCount(IDictionary<string, object>? headers)
    {
        if (headers == null || !headers.TryGetValue(RetryCountHeader, out var val))
        {
            return 0;
        }

        if (val is byte[] byteVal)
        {
            if (int.TryParse(System.Text.Encoding.UTF8.GetString(byteVal), out var countFromBytes))
            {
                return countFromBytes;
            }
        }

        try
        {
            return Convert.ToInt32(val);
        }
        catch
        {
            return 0;
        }
    }

    public static bool ShouldRetry(int currentRetryCount, out int nextRetryCount)
    {
        if (currentRetryCount < MaxRetryAttempts)
        {
            nextRetryCount = currentRetryCount + 1;
            return true;
        }

        nextRetryCount = currentRetryCount;
        return false;
    }
}

