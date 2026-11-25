using StackExchange.Redis;

namespace IdentityService.Services;

public interface ISessionRevocationStore
{
    Task SetLockedAsync(Guid userId, CancellationToken cancellationToken = default);
    Task ClearLockedAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetValidAfterNowAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class RedisSessionRevocationStore : ISessionRevocationStore
{
    private static readonly TimeSpan AccessTokenTtl = TimeSpan.FromMinutes(30);
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisSessionRevocationStore> _logger;

    public RedisSessionRevocationStore(
        ILogger<RedisSessionRevocationStore> logger,
        IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task SetLockedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_redis is null || !_redis.IsConnected)
        {
            _logger.LogWarning("Redis không khả dụng khi khóa phiên cho user {UserId}", userId);
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            var epochSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            await db.StringSetAsync($"auth:locked:{userId}", "1", AccessTokenTtl);
            await db.StringSetAsync($"auth:valid-after:{userId}", epochSec, AccessTokenTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ghi Redis khóa phiên cho user {UserId}", userId);
        }
    }

    public async Task ClearLockedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_redis is null || !_redis.IsConnected)
        {
            _logger.LogWarning("Redis không khả dụng khi xóa auth:locked cho user {UserId}", userId);
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            // D64: Xóa auth:locked, GIỮ lại auth:valid-after để token cũ trước khi khóa vẫn bị 401
            await db.KeyDeleteAsync($"auth:locked:{userId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi xóa auth:locked trên Redis cho user {UserId}", userId);
        }
    }

    public async Task SetValidAfterNowAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_redis is null || !_redis.IsConnected)
        {
            _logger.LogWarning("Redis không khả dụng khi cập nhật valid-after cho user {UserId}", userId);
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            var epochSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            await db.StringSetAsync($"auth:valid-after:{userId}", epochSec, AccessTokenTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi ghi auth:valid-after trên Redis cho user {UserId}", userId);
        }
    }
}

