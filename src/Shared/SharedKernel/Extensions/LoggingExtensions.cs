using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace SharedKernel.Extensions;

/// <summary>Lưu CorrelationId và UserId theo luồng async hiện tại (http -> event -> consumer) theo D71.</summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _correlationId = new();
    private static readonly AsyncLocal<string?> _userId = new();

    public static string? CurrentCorrelationId
    {
        get => _correlationId.Value;
        set => _correlationId.Value = value;
    }

    public static string? CurrentUserId
    {
        get => _userId.Value;
        set => _userId.Value = value;
    }
}

/// <summary>Bộ lọc che mật khẩu, JWT, token URL và header Authorization khỏi toàn bộ log (D71, LG-01).</summary>
public static partial class SensitiveDataSanitizer
{
    [GeneratedRegex(@"((?:confirm-email|reset-password)\?[^\s""']*?token=)(?!\[REDACTED\])[^\s&""']+", RegexOptions.IgnoreCase)]
    private static partial Regex TokenQueryRegex();

    [GeneratedRegex(@"(Bearer\s+)[A-Za-z0-9\-_.~+/=]{12,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"eyJ[A-Za-z0-9\-_]{10,}\.eyJ[A-Za-z0-9\-_]{10,}\.[A-Za-z0-9\-_]*")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(""?(?:password|newPassword|currentPassword|confirmPassword|accessToken|refreshToken|signingKey|secret)""?\s*[:=]\s*""?)[^""\s,&}]+", RegexOptions.IgnoreCase)]
    private static partial Regex SecretFieldRegex();

    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var result = TokenQueryRegex().Replace(input, "$1[REDACTED]");
        result = BearerRegex().Replace(result, "$1[REDACTED]");
        result = JwtRegex().Replace(result, "[REDACTED_JWT]");
        result = SecretFieldRegex().Replace(result, "$1[REDACTED]");
        return result;
    }
}

public sealed class SensitiveDestructuringPolicy : IDestructuringPolicy
{
    private static readonly string[] SensitiveKeys =
        ["password", "token", "secret", "authorization", "signingkey"];

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        result = null!;
        if (value is null) return false;
        var type = value.GetType();
        if (type.IsPrimitive || value is string or Guid or DateTime or DateTimeOffset or decimal)
        {
            return false;
        }

        var props = type.GetProperties();
        if (!props.Any(p => SensitiveKeys.Any(k => p.Name.Contains(k, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        var list = new List<LogEventProperty>(props.Length);
        foreach (var p in props)
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
            var isSensitive = SensitiveKeys.Any(k => p.Name.Contains(k, StringComparison.OrdinalIgnoreCase));
            object? raw = null;
            try { raw = isSensitive ? "[REDACTED]" : p.GetValue(value); } catch { raw = "[UNREADABLE]"; }
            list.Add(new LogEventProperty(p.Name, propertyValueFactory.CreatePropertyValue(raw, destructureObjects: false)));
        }
        result = new StructureValue(list);
        return true;
    }
}

public sealed class SanitizingTextFormatter : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _inner = new(
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] [cid={CorrelationId}] [uid={UserId}] {Message:lj}{NewLine}{Exception}");

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var sw = new StringWriter();
        if (logEvent.Properties.TryGetValue("SourceContext", out var srcVal) &&
            srcVal is Serilog.Events.ScalarValue { Value: string src } &&
            !string.IsNullOrEmpty(src))
        {
            var lvl = logEvent.Level switch
            {
                LogEventLevel.Warning => "warn",
                LogEventLevel.Error => "fail",
                LogEventLevel.Fatal => "crit",
                LogEventLevel.Debug => "dbug",
                LogEventLevel.Verbose => "trce",
                _ => "info"
            };
            sw.Write($"{lvl}: {src} ");
        }
        _inner.Format(logEvent, sw);
        output.Write(SensitiveDataSanitizer.Sanitize(sw.ToString()));
    }
}

public static class LoggingExtensions
{
    public static WebApplicationBuilder AddSmartWalletLogging(this WebApplicationBuilder builder, string serviceName)
    {
        // D75 / SEC-01 / G14: Không phát Server header mặc định của Kestrel
        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

        builder.Host.UseSerilog((context, services, loggerConfig) =>
        {
            loggerConfig
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Yarp", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName)
                .Destructure.With<SensitiveDestructuringPolicy>()
                .WriteTo.Console(new SanitizingTextFormatter());

            var seqEnabled = !string.Equals(context.Configuration["Seq:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
            var seqUrl = context.Configuration["Seq:ServerUrl"];
            if (string.IsNullOrWhiteSpace(seqUrl))
            {
                seqUrl = "http://localhost:5341";
            }

            if (seqEnabled && Uri.TryCreate(seqUrl, UriKind.Absolute, out _))
            {
                // Timeout kết nối nhanh (150ms) để không làm chậm shutdown khi Seq không bật (LG-03)
                var fastHandler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(150) };
                loggerConfig.WriteTo.Seq(
                    serverUrl: seqUrl,
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    messageHandler: fastHandler);
            }
        });

        return builder;
    }

    public static WebApplication UseSmartWalletRequestLogging(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            const string correlationHeader = "X-Correlation-ID";
            if (!context.Request.Headers.TryGetValue(correlationHeader, out var correlationId) ||
                string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = Guid.NewGuid().ToString("N");
                context.Request.Headers[correlationHeader] = correlationId;
            }

            var cid = correlationId.ToString();
            CorrelationContext.CurrentCorrelationId = cid;
            CorrelationContext.CurrentUserId = null;

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[correlationHeader] = cid;
                context.Response.Headers.Remove("Server");
                context.Response.Headers.Remove("X-Powered-By");
                return Task.CompletedTask;
            });

            var sw = Stopwatch.StartNew();
            using (LogContext.PushProperty("CorrelationId", cid))
            {
                try
                {
                    await next();
                }
                finally
                {
                    sw.Stop();
                    var path = context.Request.Path.Value ?? "/";
                    if (!path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) &&
                        !path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase))
                    {
                        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? context.User?.FindFirstValue("sub")
                            ?? "anonymous";
                        CorrelationContext.CurrentUserId = userId;
                        using (LogContext.PushProperty("UserId", userId))
                        {
                            var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("SmartWallet.Http");
                            logger?.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:F1}ms",
                                context.Request.Method, path, context.Response.StatusCode, sw.Elapsed.TotalMilliseconds);
                        }
                    }

                    CorrelationContext.CurrentCorrelationId = null;
                    CorrelationContext.CurrentUserId = null;
                }
            }
        });

        return app;
    }

    /// <summary>Mở tài liệu OpenAPI tại /openapi/v1.json chỉ ở môi trường Development (D74, SW-03).</summary>
    public static WebApplication UseSmartWalletOpenApi(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger(c =>
            {
                c.RouteTemplate = "openapi/{documentName}.json";
            });
        }

        return app;
    }
}
