using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using RabbitMQ.Client;

namespace SharedKernel.Extensions;

public record ServiceBackupResult(string Service, string FileName, long SizeBytes, string Sha256);

public interface IBackupRunner
{
    Task<ServiceBackupResult> ExecuteBackupAsync(string serviceName, string backupId, IConfiguration configuration, CancellationToken cancellationToken = default);
}

public class SqliteBackupRunner<TDbContext> : IBackupRunner where TDbContext : DbContext
{
    private static readonly Dictionary<string, string> CanonicalFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IdentityService"] = "identity.db",
        ["UserProfileService"] = "profile.db",
        ["WalletService"] = "wallet.db",
        ["TransactionService"] = "transaction.db",
        ["CategoryService"] = "category.db",
        ["BudgetService"] = "budget.db",
        ["SavingGoalService"] = "saving.db",
        ["RecurringDebtService"] = "recdebt.db",
        ["ReportService"] = "report.db",
        ["NotificationService"] = "notification.db",
        ["AuditService"] = "audit.db",
        ["FileService"] = "file.db"
    };

    private readonly TDbContext _dbContext;

    public SqliteBackupRunner(TDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ServiceBackupResult> ExecuteBackupAsync(
        string serviceName,
        string backupId,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupId) || !Regex.IsMatch(backupId, "^[A-Za-z0-9_-]{4,80}$"))
        {
            throw new ArgumentException("BackupId không hợp lệ.", nameof(backupId));
        }

        var backupRoot = configuration["Backup:Directory"];
        if (string.IsNullOrWhiteSpace(backupRoot))
        {
            backupRoot = Path.Combine(Directory.GetCurrentDirectory(), "backups");
        }

        var backupDir = Path.Combine(backupRoot, backupId);
        Directory.CreateDirectory(backupDir);

        var fileName = CanonicalFileNames.TryGetValue(serviceName, out var mapped)
            ? mapped
            : $"{serviceName.ToLowerInvariant().Replace("service", string.Empty)}.db";
        var targetPath = Path.Combine(backupDir, fileName);

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        var connStr = _dbContext.Database.GetConnectionString()
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Không tìm thấy SQLite ConnectionString.");

        var isMemory = connStr.Contains(":memory:", StringComparison.OrdinalIgnoreCase);
        if (isMemory && _dbContext.Database.GetDbConnection() is SqliteConnection activeConn)
        {
            if (activeConn.State != System.Data.ConnectionState.Open)
            {
                await activeConn.OpenAsync(cancellationToken);
            }
            await using var cmd = activeConn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{targetPath.Replace("'", "''")}';";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{targetPath.Replace("'", "''")}';";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var fileInfo = new FileInfo(targetPath);
        await using var fs = File.OpenRead(targetPath);
        var hashBytes = await SHA256.HashDataAsync(fs, cancellationToken);
        var sha256 = Convert.ToHexStringLower(hashBytes);

        return new ServiceBackupResult(serviceName, fileName, fileInfo.Length, sha256);
    }
}

public class SelfHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckResult.Healthy("Service is running"));
}

public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly string _host;
    private readonly int _port;

    public RabbitMqHealthCheck(IConfiguration configuration)
    {
        _host = configuration["RabbitMQ:Host"] ?? "localhost";
        _port = int.TryParse(configuration["RabbitMQ:Port"], out var p) ? p : 5672;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Kiểm tra TCP nhanh (300ms) trước để tránh HTTP client timeout 1s khi broker tắt (test_S3_20 / X04)
            using (var tcp = new TcpClient())
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMilliseconds(300));
                await tcp.ConnectAsync(_host, _port, cts.Token);
            }

            var factory = new ConnectionFactory
            {
                HostName = _host,
                Port = _port,
                RequestedConnectionTimeout = TimeSpan.FromMilliseconds(400),
                SocketReadTimeout = TimeSpan.FromMilliseconds(400),
                SocketWriteTimeout = TimeSpan.FromMilliseconds(400)
            };
            using var conn = factory.CreateConnection();
            if (conn.IsOpen)
            {
                return HealthCheckResult.Healthy("RabbitMQ reachable");
            }
            return HealthCheckResult.Degraded("RabbitMQ not open");
        }
        catch
        {
            return HealthCheckResult.Degraded("RabbitMQ unavailable");
        }
    }
}

public class RedisHealthCheck : IHealthCheck
{
    private readonly string _host;
    private readonly int _port;

    public RedisHealthCheck(IConfiguration configuration)
    {
        var connStr = configuration["Redis:Configuration"] ?? configuration.GetConnectionString("Redis") ?? "localhost:6379";
        var firstEndpoint = connStr.Split(',')[0].Trim();
        var parts = firstEndpoint.Split(':');
        _host = parts[0];
        _port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 6379;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(300));
            await tcp.ConnectAsync(_host, _port, cts.Token);
            return HealthCheckResult.Healthy("Redis reachable");
        }
        catch
        {
            return HealthCheckResult.Degraded("Redis unavailable");
        }
    }
}

public static class ObservabilityExtensions
{
    public static IHealthChecksBuilder AddServiceHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        bool checkRabbitMq = false,
        bool checkRedis = false)
    {
        var builder = services.AddHealthChecks()
            .AddCheck<SelfHealthCheck>("self", failureStatus: HealthStatus.Unhealthy, tags: new[] { "live" });

        if (checkRabbitMq)
        {
            services.AddSingleton<RabbitMqHealthCheck>();
            builder.AddCheck<RabbitMqHealthCheck>("rabbitmq", failureStatus: HealthStatus.Degraded);
        }

        if (checkRedis)
        {
            services.AddSingleton<RedisHealthCheck>();
            builder.AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded);
        }

        return builder;
    }

    public static IHealthChecksBuilder AddServiceHealthChecks<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        bool checkRabbitMq = false,
        bool checkRedis = false)
        where TDbContext : DbContext
    {
        services.AddScoped<IBackupRunner, SqliteBackupRunner<TDbContext>>();

        var builder = services.AddHealthChecks()
            .AddDbContextCheck<TDbContext>(
                name: "database",
                failureStatus: HealthStatus.Unhealthy,
                customTestQuery: async (db, ct) => await db.Database.CanConnectAsync(ct));

        if (checkRabbitMq)
        {
            services.AddSingleton<RabbitMqHealthCheck>();
            builder.AddCheck<RabbitMqHealthCheck>("rabbitmq", failureStatus: HealthStatus.Degraded);
        }

        if (checkRedis)
        {
            services.AddSingleton<RedisHealthCheck>();
            builder.AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded);
        }

        return builder;
    }

    public static WebApplication UseServiceHealthAndMetrics(this WebApplication app, string serviceName, bool exposeMetrics = true)
    {
        if (exposeMetrics)
        {
            // Prometheus HTTP metrics & /metrics endpoint trên cổng riêng của service (D61)
            app.UseHttpMetrics();
            app.MapMetrics("/metrics");
        }

        // Liveness probe: luôn 200 nếu process còn sống
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = async (context, _) =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = "Healthy",
                    service = serviceName
                }));
            }
        });

        // Readiness probe: DB lỗi -> 503 Unhealthy; RabbitMQ/Redis lỗi -> 200 Degraded; không lộ thông tin nội bộ (D61)
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var payload = new
                {
                    status = report.Status.ToString(),
                    service = serviceName,
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString()
                    }).ToList()
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            }
        });

        // D70: Endpoint nội bộ sao lưu an toàn WAL qua VACUUM INTO (chỉ cấp cho service có DB + token SystemOnly)
        var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
        using (var probeScope = scopeFactory.CreateScope())
        {
            if (probeScope.ServiceProvider.GetService<IBackupRunner>() is not null)
            {
                app.MapPost("/internal/backup", async (
                    HttpContext context,
                    IBackupRunner backupRunner,
                    IConfiguration configuration,
                    CancellationToken ct) =>
                {
                    string? backupId = context.Request.Query["backupId"].FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(backupId))
                    {
                        try
                        {
                            using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
                            if (doc.RootElement.TryGetProperty("backupId", out var prop) ||
                                doc.RootElement.TryGetProperty("BackupId", out prop))
                            {
                                backupId = prop.GetString();
                            }
                        }
                        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                        {
                            // Body rỗng hoặc không phải JSON: dùng backupId mặc định bên dưới
                        }
                    }

                    backupId = string.IsNullOrWhiteSpace(backupId)
                        ? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMdd-HHmmss")
                        : backupId.Trim();

                    try
                    {
                        var result = await backupRunner.ExecuteBackupAsync(serviceName, backupId, configuration, ct);
                        return Results.Ok(new
                        {
                            service = result.Service,
                            fileName = result.FileName,
                            sizeBytes = result.SizeBytes,
                            sha256 = result.Sha256
                        });
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(new { message = ex.Message });
                    }
                }).RequireAuthorization("SystemOnly");
            }
        }

        return app;
    }
}
