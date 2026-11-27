using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using ApiGateway.Yarp.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddSmartWalletLogging("ApiGateway.Yarp");

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
builder.Services.AddHttpClient("BackupProbe", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30); // D70: timeout 30s cho VACUUM INTO
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BackupCoordinatorService>();
builder.Services.AddHostedService<BackupSchedulerBackgroundService>();
builder.Services.AddServiceHealthChecks(builder.Configuration);

// D73 / H-03: Rate limiting tại Gateway (Auth 10/phút/IP không tin X-Forwarded-For giả; User 300/phút/sub)
var rateLimitEnabled = !string.Equals(builder.Configuration["RateLimit:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
var authPermitLimit = int.TryParse(builder.Configuration["RateLimit:Auth:PermitLimit"], out var apl) ? apl : 10;
var authWindowSeconds = int.TryParse(builder.Configuration["RateLimit:Auth:WindowSeconds"], out var aws) ? aws : 60;
var userPermitLimit = int.TryParse(builder.Configuration["RateLimit:User:PermitLimit"], out var upl) ? upl : 300;
var userWindowSeconds = int.TryParse(builder.Configuration["RateLimit:User:WindowSeconds"], out var uws) ? uws : 60;
var trustedProxies = (builder.Configuration.GetSection("RateLimit:TrustedProxies").Get<string[]>() ?? [])
    .Where(p => !string.IsNullOrWhiteSpace(p))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

if (rateLimitEnabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path.Value ?? "/";
            if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            {
                return RateLimitPartition.GetNoLimiter("bypass");
            }

            if (path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/auth/register", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/auth/forgot-password", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/auth/resend-confirmation", StringComparison.OrdinalIgnoreCase))
            {
                var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var clientIp = remoteIp;
                if (trustedProxies.Contains(remoteIp) &&
                    context.Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
                {
                    var forwarded = xff.ToString().Split(',').FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(forwarded))
                    {
                        clientIp = forwarded;
                    }
                }

                return RateLimitPartition.GetFixedWindowLimiter($"auth:{clientIp}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authPermitLimit,
                    Window = TimeSpan.FromSeconds(authWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.User.FindFirstValue("sub")
                    ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"user:{sub}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = userPermitLimit,
                    Window = TimeSpan.FromSeconds(userWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            }

            return RateLimitPartition.GetNoLimiter("anon");
        });

        options.OnRejected = async (context, token) =>
        {
            var retryAfter = authWindowSeconds;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retrySpan))
            {
                retryAfter = Math.Max(1, (int)Math.Ceiling(retrySpan.TotalSeconds));
            }
            context.HttpContext.Response.Headers["Retry-After"] = retryAfter.ToString();
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                code = "RATE_LIMITED",
                message = "Bạn thao tác quá nhanh, vui lòng thử lại sau."
            }, token);
        };
    });
}

var app = builder.Build();

// D75 / SEC-01: Security Headers cho mọi HTTP response qua Gateway
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            headers["Content-Security-Policy"] = "frame-ancestors 'none'";
        }
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        return Task.CompletedTask;
    });
    await next();
});

// D71 / LG-01, LG-02: CorrelationId + LogContext + sanitized HTTP request log
app.UseSmartWalletRequestLogging();

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
if (rateLimitEnabled)
{
    app.UseRateLimiter();
}

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

// D70 / FIN-159: Điều phối sao lưu & xem lịch sử sao lưu toàn hệ thống
app.MapPost("/api/admin/backups", async (BackupCoordinatorService coordinator) =>
{
    var backupId = await coordinator.TriggerBackupAsync();
    return Results.Accepted($"/api/admin/backups", new { backupId });
}).RequireAuthorization(AppPermissions.BackupsWrite);

app.MapGet("/api/admin/backups", async (BackupCoordinatorService coordinator, CancellationToken ct) =>
{
    var history = await coordinator.GetHistoryAsync(ct);
    return Results.Ok(history);
}).RequireAuthorization(AppPermissions.BackupsWrite);

// D74 / SW-01..SW-03: Swagger UI tập trung tại Gateway (chỉ mở trong Development; Production trả 404)
if (app.Environment.IsDevelopment())
{
    var swaggerServices = new Dictionary<string, (string ClusterKey, string DefaultUrl, string DisplayName)>(StringComparer.OrdinalIgnoreCase)
    {
        ["identity"]     = ("identity-cluster",     "http://localhost:8001", "IdentityService"),
        ["profile"]      = ("profile-cluster",      "http://localhost:8002", "UserProfileService"),
        ["wallet"]       = ("wallet-cluster",       "http://localhost:8003", "WalletService"),
        ["transaction"]  = ("transaction-cluster",  "http://localhost:8004", "TransactionService"),
        ["category"]     = ("category-cluster",     "http://localhost:8005", "CategoryService"),
        ["budget"]       = ("budget-cluster",       "http://localhost:8006", "BudgetService"),
        ["savinggoal"]   = ("saving-cluster",       "http://localhost:8007", "SavingGoalService"),
        ["recurring"]    = ("recurring-cluster",    "http://localhost:8008", "RecurringDebtService"),
        ["report"]       = ("report-cluster",       "http://localhost:8009", "ReportService"),
        ["notification"] = ("notification-cluster", "http://localhost:8010", "NotificationService"),
        ["audit"]        = ("audit-cluster",        "http://localhost:8011", "AuditService"),
        ["file"]         = ("file-cluster",         "http://localhost:8012", "FileService")
    };

    app.MapGet("/swagger/docs/{service}.json", async (
        string service,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct) =>
    {
        if (!swaggerServices.TryGetValue(service, out var entry))
        {
            return Results.NotFound(new { message = "Unknown service documentation." });
        }

        var baseUrl = config[$"ReverseProxy:Clusters:{entry.ClusterKey}:Destinations:d1:Address"] ?? entry.DefaultUrl;
        var docUrl = $"{baseUrl.TrimEnd('/')}/openapi/v1.json";
        try
        {
            var client = httpClientFactory.CreateClient("HealthProbe");
            using var resp = await client.GetAsync(docUrl, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return Results.NotFound(new { message = $"OpenAPI doc unavailable for {service}." });
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            return Results.Content(json, "application/json");
        }
        catch
        {
            return Results.NotFound(new { message = $"Service {service} is unreachable." });
        }
    });

    app.UseSwaggerUI(c =>
    {
        c.RoutePrefix = "swagger";
        foreach (var (key, entry) in swaggerServices)
        {
            c.SwaggerEndpoint($"/swagger/docs/{key}.json", entry.DisplayName);
        }
    });
}

app.MapReverseProxy();

app.Run();
