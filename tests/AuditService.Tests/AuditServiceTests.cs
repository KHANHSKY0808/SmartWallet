using System.Text.Json;
using AuditService.Consumers;
using AuditService.Controllers;
using AuditService.Data;
using AuditService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Extensions;

namespace AuditService.Tests;

public class AuditServiceTests
{
    private static ServiceProvider CreateAuditProvider(SqliteConnection conn)
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddDbContext<AuditDbContext>(o => o.UseSqlite(conn));
        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        db.Database.EnsureCreated();
        AuditDbContext.EnsureImmutableTriggersAsync(db).GetAwaiter().GetResult();
        return sp;
    }

    [Fact]
    public async Task Test_AU04_RedeliveredEventLoggedOnce_AndUnknownRoutingKeyUsesUnknownService()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        using var sp = CreateAuditProvider(conn);

        var consumer = new AuditAllConsumer(
            sp.GetRequiredService<IConfiguration>(),
            sp,
            sp.GetRequiredService<ILogger<AuditAllConsumer>>());

        var evId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { Id = evId, OccurredAt = "2026-11-25T00:00:00Z", UserId = userId, Note = "QA probe" });

        await consumer.HandleRawMessageAsync(payload, "qa.audit_probe", evId.ToString(), evId.ToString(), null);
        await consumer.HandleRawMessageAsync(payload, "qa.audit_probe", evId.ToString(), evId.ToString(), null);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var rows = await db.AuditLogs.Where(a => a.UserId == userId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("Unknown", rows[0].ServiceName);
    }

    [Fact]
    public async Task Test_AU05_SqliteTrigger_PreventsUpdateAndDelete()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        using var sp = CreateAuditProvider(conn);
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        db.AuditLogs.Add(new Models.AuditLog { ServiceName = "WalletService", Action = "Created", Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE AuditLogs SET Action = 'X'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs"));
    }

    [Fact]
    public void Test_AU06_MasksEmailsAndStripsPasswordTokenSecretOtpCodeKeys()
    {
        var json = JsonSerializer.Serialize(new
        {
            Id = Guid.NewGuid(),
            EventType = "UserRegisteredEvent",
            UserId = Guid.NewGuid(),
            Email = "qauser@qa.local",
            Password = "Secret123!",
            ResetToken = "secret-tok",
            SecretKey = "top-secret",
            OtpCode = "123456",
            Nested = new { AccessToken = "jwt-val", Note = "Notify qauser@qa.local" }
        });

        var log = AuditEventParser.ParseAndMask(json, "user.registered", null, null, null);
        Assert.NotNull(log.NewValueJson);
        Assert.DoesNotContain("qauser@qa.local", log.NewValueJson);
        Assert.Contains("q***@qa.local", log.NewValueJson);
        Assert.DoesNotContain("Password", log.NewValueJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", log.NewValueJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", log.NewValueJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Otp", log.NewValueJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Code", log.NewValueJson, StringComparison.OrdinalIgnoreCase);

        // EmailRequested strips Data payload
        var emailReqJson = JsonSerializer.Serialize(new
        {
            Id = Guid.NewGuid(),
            EventType = "EmailRequestedEvent",
            To = "alice@example.com",
            Template = "ConfirmEmail",
            Data = new { confirmLink = "http://localhost/confirm-email?token=abcdef" }
        });
        var emailLog = AuditEventParser.ParseAndMask(emailReqJson, "identity.email_requested", null, null, null);
        Assert.NotNull(emailLog.NewValueJson);
        Assert.DoesNotContain("abcdef", emailLog.NewValueJson);
        Assert.DoesNotContain("confirmLink", emailLog.NewValueJson);
        Assert.Contains("a***@example.com", emailLog.NewValueJson);
    }

    [Fact]
    public async Task Test_AU03_AU09_ControllerFiltersPageSizeAndResilience()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        using var sp = CreateAuditProvider(conn);
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var u1 = Guid.NewGuid();
        db.AuditLogs.AddRange(
            new Models.AuditLog { UserId = u1, ServiceName = "TransactionService", Action = "TransactionCreatedEvent", Timestamp = DateTime.UtcNow.AddMinutes(-5) },
            new Models.AuditLog { UserId = u1, ServiceName = "TransactionService", Action = "TransactionDeletedEvent", Timestamp = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var controller = new AuditController(db);
        var bad = await controller.GetLogs(pageSize: 101);
        Assert.IsType<BadRequestObjectResult>(bad);

        var ok = Assert.IsType<OkObjectResult>(await controller.GetLogs(userId: u1, serviceName: "TransactionService"));
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("TransactionDeletedEvent", items[0].GetProperty("action").GetString());
    }

    [Fact]
    public void Test_HM07_ConsulRegistrationExtensions_ConfiguresHealthCheckWithIntervalUnder15s()
    {
        var consulMethod = typeof(ConsulRegistrationExtensions).GetMethod("UseConsulServiceRegistration");
        Assert.NotNull(consulMethod);

        // Verify source contract in ConsulRegistrationExtensions.cs points to /health and Interval <= 15s
        var srcPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Shared/SharedKernel/Extensions/ConsulRegistrationExtensions.cs"));
        var content = File.ReadAllText(srcPath);
        Assert.Contains("/health\"", content);
        Assert.Contains("TimeSpan.FromSeconds(10)", content);
    }
}

