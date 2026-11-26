using System.Collections.Concurrent;

namespace IdentityService.Services;

public interface IResendConfirmationRateLimiter
{
    bool IsThrottled(string normalizedEmail, DateTimeOffset now);
    void RecordSent(string normalizedEmail, DateTimeOffset now);
}

public sealed class InMemoryResendConfirmationRateLimiter : IResendConfirmationRateLimiter
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new(StringComparer.OrdinalIgnoreCase);

    public bool IsThrottled(string normalizedEmail, DateTimeOffset now)
    {
        return _lastSent.TryGetValue(normalizedEmail, out var lastSent) && (now - lastSent) < Cooldown;
    }

    public void RecordSent(string normalizedEmail, DateTimeOffset now)
    {
        _lastSent[normalizedEmail] = now;
    }
}

