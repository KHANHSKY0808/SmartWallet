using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel.Extensions;

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
}).RequireAuthorization("AdminOnly");

app.MapReverseProxy();

app.Run();
