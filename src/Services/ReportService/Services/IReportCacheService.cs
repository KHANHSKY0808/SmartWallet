using System.Text.Json;
using StackExchange.Redis;

namespace ReportService.Services;

public interface IReportCacheService
{
    Task<T?> GetAsync<T>(Guid userId, string cacheSubKey, CancellationToken cancellationToken = default);
    Task SetAsync<T>(Guid userId, string cacheSubKey, T value, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task InvalidateUserCacheAsync(Guid userId, CancellationToken cancellationToken = default);
    Task InvalidateAllUsersCacheAsync(CancellationToken cancellationToken = default);
}

public class RedisReportCacheService : IReportCacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisReportCacheService> _logger;

    public RedisReportCacheService(ILogger<RedisReportCacheService> logger, IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task<T?> GetAsync<T>(Guid userId, string cacheSubKey, CancellationToken cancellationToken = default)
    {
        if (_redis == null || !_redis.IsConnected) return default;
        try
        {
            var db = _redis.GetDatabase();
            var fullKey = await BuildKeyAsync(db, userId, cacheSubKey);
            var json = await db.StringGetAsync(fullKey);
            if (!json.HasValue) return default;
            return JsonSerializer.Deserialize<T>(json.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET lỗi cho key {SubKey}, tính toán trực tiếp (D59)", cacheSubKey);
            return default;
        }
    }

    public async Task SetAsync<T>(Guid userId, string cacheSubKey, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (_redis == null || !_redis.IsConnected) return;
        try
        {
            var db = _redis.GetDatabase();
            var fullKey = await BuildKeyAsync(db, userId, cacheSubKey);
            var json = JsonSerializer.Serialize(value);
            await db.StringSetAsync(fullKey, json, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET lỗi cho key {SubKey}, bỏ qua cache (D59)", cacheSubKey);
        }
    }

    public async Task InvalidateUserCacheAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_redis == null || !_redis.IsConnected) return;
        try
        {
            var db = _redis.GetDatabase();
            await db.StringIncrementAsync($"report:ver:{userId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis Invalidate lỗi cho user {UserId}", userId);
        }
    }

    public async Task InvalidateAllUsersCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_redis == null || !_redis.IsConnected) return;
        try
        {
            var db = _redis.GetDatabase();
            await db.StringIncrementAsync("report:global_cat_ver");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis Global Invalidate lỗi");
        }
    }

    private static async Task<string> BuildKeyAsync(IDatabase db, Guid userId, string subKey)
    {
        var userVerVal = await db.StringGetAsync($"report:ver:{userId}");
        var globalVerVal = await db.StringGetAsync("report:global_cat_ver");
        var uVer = userVerVal.HasValue ? (long)userVerVal : 0L;
        var gVer = globalVerVal.HasValue ? (long)globalVerVal : 0L;
        return $"report:g{gVer}:u{uVer}:{userId}:{subKey}";
    }
}

