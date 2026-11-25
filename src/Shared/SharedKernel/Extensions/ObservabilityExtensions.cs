using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using RabbitMQ.Client;

namespace SharedKernel.Extensions;

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

        return app;
    }
}
