using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// D64: Redis connection cho kiểm tra khóa tức thì (fail fast trong 500ms khi Redis tắt)
var redisConnStr = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var opts = ConfigurationOptions.Parse(redisConnStr);
    opts.AbortOnConnectFail = false;
    opts.ConnectTimeout = 500;
    opts.SyncTimeout = 500;
    opts.AsyncTimeout = 500;
    return ConnectionMultiplexer.Connect(opts);
});

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("HealthProbe", client =>
{
    client.Timeout = TimeSpan.FromSeconds(2); // D61: timeout 2s mỗi service
});
builder.Services.AddServiceHealthChecks(builder.Configuration);

var app = builder.Build();

// Middleware: X-Correlation-ID tracing
app.Use(async (context, next) =>
{
    const string correlationHeader = "X-Correlation-ID";
    if (!context.Request.Headers.TryGetValue(correlationHeader, out var correlationId) ||
        string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
        context.Request.Headers[correlationHeader] = correlationId;
    }

    context.Response.Headers[correlationHeader] = correlationId;
    await next();
});

app.UseCors("AllowFrontend");
app.UseAuthentication();

// D64 & D65: Chặn token System từ ngoài + kiểm tra khóa/valid-after qua Redis (fail-closed admin / fail-open user)
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        // D65 / RB-05: Từ chối mọi request từ ngoài mang token role=System
        if (context.User.IsInRole("System") || context.User.HasClaim("role", "System") || context.User.HasClaim(ClaimTypes.Role, "System"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { code = "SYSTEM_TOKEN_REJECTED", message = "Service token không được phép truy cập qua Gateway." });
            return;
        }

        var path = context.Request.Path;
        var isAdminOrAuditRoute = path.StartsWithSegments("/api/admin") || path.StartsWithSegments("/api/audit");
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var redis = context.RequestServices.GetRequiredService<IConnectionMultiplexer>();
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GatewaySessionGuard");

            try
            {
                if (!redis.IsConnected)
                {
                    logger.LogWarning("Redis đang mất kết nối khi kiểm tra phiên cho {Path} (Admin fail-closed, User fail-open).", path);
                    if (isAdminOrAuditRoute)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsJsonAsync(new { code = "REDIS_UNAVAILABLE", message = "Hệ thống xác thực quản trị tạm thời gián đoạn." });
                        return;
                    }
                }
                else
                {
                    var db = redis.GetDatabase();
                    var locked = await db.KeyExistsAsync($"auth:locked:{userId}");
                    if (locked)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { code = "ACCOUNT_LOCKED", message = "Phiên đăng nhập đã bị thu hồi do tài khoản bị khóa." });
                        return;
                    }

                    var validAfterRaw = await db.StringGetAsync($"auth:valid-after:{userId}");
                    if (validAfterRaw.HasValue && long.TryParse(validAfterRaw.ToString(), out var validAfter))
                    {
                        var iatClaim = context.User.FindFirstValue(JwtRegisteredClaimNames.Iat);
                        if (!long.TryParse(iatClaim, out var iat) || iat <= validAfter)
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            await context.Response.WriteAsJsonAsync(new { code = "TOKEN_REVOKED", message = "Phiên đăng nhập đã hết hiệu lực. Vui lòng đăng nhập lại." });
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis không khả dụng khi kiểm tra phiên cho {Path} (Admin fail-closed, User fail-open).", path);
                if (isAdminOrAuditRoute)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new { code = "REDIS_UNAVAILABLE", message = "Hệ thống xác thực quản trị tạm thời gián đoạn." });
                    return;
                }
                // User route: fail-open (D64)
            }
        }
    }

    await next();
});

app.UseAuthorization();

// Gateway health (giữ "healthy" viết thường theo test_G01, không lộ /metrics qua Gateway theo D61 / HM-05)
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    gateway = "ApiGateway.Yarp",
    port = 8000,
    timestamp = DateTime.UtcNow
}));
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "ApiGateway.Yarp" }));

// D61 / FIN-136 / HM-03, HM-04: Endpoint tổng hợp trạng thái 12 microservices cho Admin
var monitoredServices = new (string Name, string ClusterKey, string DefaultUrl)[]
{
    ("IdentityService",      "identity-cluster",     "http://localhost:8001"),
    ("FileService",          "file-cluster",         "http://localhost:8012"),
    ("UserProfileService",   "profile-cluster",      "http://localhost:8002"),
    ("WalletService",        "wallet-cluster",       "http://localhost:8003"),
    ("TransactionService",   "transaction-cluster",  "http://localhost:8004"),
    ("CategoryService",      "category-cluster",     "http://localhost:8005"),
    ("BudgetService",        "budget-cluster",       "http://localhost:8006"),
    ("SavingGoalService",    "saving-cluster",       "http://localhost:8007"),
    ("RecurringDebtService", "recurring-cluster",    "http://localhost:8008"),
    ("ReportService",        "report-cluster",       "http://localhost:8009"),
    ("NotificationService",  "notification-cluster", "http://localhost:8010"),
    ("AuditService",         "audit-cluster",        "http://localhost:8011")
};

app.MapGet("/api/admin/health", async (
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    CancellationToken ct) =>
{
    const string cacheKey = "admin:health:aggregate";
    if (cache.TryGetValue(cacheKey, out object? cachedResult) && cachedResult != null)
    {
        return Results.Ok(cachedResult);
    }

    var client = httpClientFactory.CreateClient("HealthProbe");
    var tasks = monitoredServices.Select(async svc =>
    {
        var baseUrl = config[$"ReverseProxy:Clusters:{svc.ClusterKey}:Destinations:d1:Address"] ?? svc.DefaultUrl;
        var healthUrl = $"{baseUrl.TrimEnd('/')}/health";
        var sw = Stopwatch.StartNew();
        string status = "Unhealthy";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            using var resp = await client.GetAsync(healthUrl, cts.Token);
            sw.Stop();

            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("status", out var sProp))
                {
                    var parsedStatus = sProp.GetString();
                    status = string.Equals(parsedStatus, "Degraded", StringComparison.OrdinalIgnoreCase)
                        ? "Degraded"
                        : "Healthy";
                }
                else
                {
                    status = "Healthy";
                }
            }
        }
        catch
        {
            sw.Stop();
            status = "Unhealthy";
        }

        return new
        {
            service = svc.Name,
            status,
            latencyMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
            checkedAt = DateTime.UtcNow
        };
    });

    var results = await Task.WhenAll(tasks);
    cache.Set<object>(cacheKey, results, TimeSpan.FromSeconds(1)); // Cache 1s (<= 10s D61, HM-04 <= 30s)
    return Results.Ok(results);
}).RequireAuthorization(AppPermissions.HealthRead);

app.MapReverseProxy();

app.Run();
