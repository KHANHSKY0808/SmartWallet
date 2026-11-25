using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NotificationService.Consumers;
using NotificationService.Data;
using NotificationService.Models;
using NotificationService.Services;
using SharedKernel.Events.EventContracts;

namespace NotificationService.Tests;

public class Sprint6bEmailAndNotificationTests
{
    private static ServiceProvider CreateInMemorySqliteProvider(SqliteConnection conn, out TestTimeProvider timeProvider, ISmtpEmailClient? smtp = null, int[]? delays = null)
    {
        timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero));
        var delayConfig = new Dictionary<string, string?>();
        var effectiveDelays = delays ?? [1, 2];
        for (int i = 0; i < effectiveDelays.Length; i++)
        {
            delayConfig[$"Email:RetryDelaysSeconds:{i}"] = effectiveDelays[i].ToString();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(delayConfig)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<IEmailDispatchSignal, EmailDispatchSignal>();
        services.AddSingleton(smtp ?? new FakeSmtpClient());
        services.AddLogging();
        services.AddDbContext<NotificationDbContext>(o => o.UseSqlite(conn));
        services.AddScoped<EmailDispatcherService>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.EnsureCreated();
        return sp;
    }

    [Fact]
    public async Task Test_NT05_DuplicateEventDeliveredTwice_CreatesOnlyOneNotification()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        using var sp = CreateInMemorySqliteProvider(conn, out _);

        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILogger<MoneyTransferredConsumer>>();
        var consumer = new MoneyTransferredConsumer(config, logger, sp);

        var evId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ev = new MoneyTransferredEvent
        {
            Id = evId,
            UserId = userId,
            FromWalletId = Guid.NewGuid(),
            ToWalletId = Guid.NewGuid(),
            Amount = 50000m
        };

        await consumer.HandleEventAsync(ev);
        await consumer.HandleEventAsync(ev);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var count = await db.Notifications.CountAsync(n => n.EventId == evId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Test_EM04_SmtpFailsThreeTimesConsecutively_MarksFailedWithThreeAttempts()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        var failingSmtp = new FakeSmtpClient { ShouldFail = true };
        // Delays [2, 4] -> Attempt 1 (+2s), Attempt 2 (+4s), Attempt 3 -> Failed (Attempts = 3)
        using var sp = CreateInMemorySqliteProvider(conn, out var clock, failingSmtp, delays: [2, 4]);

        var consumer = new EmailRequestedConsumer(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<EmailRequestedConsumer>>(),
            sp,
            sp.GetRequiredService<IEmailDispatchSignal>(),
            clock);

        var ev = new EmailRequestedEvent
        {
            Id = Guid.NewGuid(),
            To = "em04@test.local",
            Template = "ConfirmEmail",
            Data = new() { ["confirmLink"] = "http://localhost:4200/confirm-email?token=abc" }
        };
        await consumer.HandleEventAsync(ev);

        using (var s1 = sp.CreateScope())
        {
            await s1.ServiceProvider.GetRequiredService<EmailDispatcherService>().ProcessDueEmailsAsync();
        }
        clock.Advance(TimeSpan.FromSeconds(3));
        using (var s2 = sp.CreateScope())
        {
            await s2.ServiceProvider.GetRequiredService<EmailDispatcherService>().ProcessDueEmailsAsync();
        }
        clock.Advance(TimeSpan.FromSeconds(5));
        using (var s3 = sp.CreateScope())
        {
            await s3.ServiceProvider.GetRequiredService<EmailDispatcherService>().ProcessDueEmailsAsync();
        }
        clock.Advance(TimeSpan.FromSeconds(60));
        using (var s4 = sp.CreateScope())
        {
            // Không thử nữa sau khi Failed
            await s4.ServiceProvider.GetRequiredService<EmailDispatcherService>().ProcessDueEmailsAsync();
            var item = await s4.ServiceProvider.GetRequiredService<NotificationDbContext>().EmailQueue.SingleAsync();
            Assert.Equal(3, item.Attempts);
            Assert.Equal(EmailQueueStatus.Failed, item.Status);
            Assert.Equal(3, failingSmtp.CallCount);
        }
    }

    [Fact]
    public async Task Test_EM06_SentEmailOlderThan24Hours_ClearsBodyAndLogsContainNoToken()
    {
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        var smtp = new FakeSmtpClient();
        using var sp = CreateInMemorySqliteProvider(conn, out var clock, smtp);

        var secretToken = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
        var captureLogger = new ListLogger<EmailDispatcherService>();

        using (var s1 = sp.CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<NotificationDbContext>();
            db.EmailQueue.Add(new EmailQueueItem
            {
                ToEmail = "em06@test.local",
                Subject = "Chào mừng đến SmartWallet",
                BodyHtml = $"<a href='http://localhost:4200/confirm-email?token={secretToken}'>Link</a>",
                TemplateCode = "ConfirmEmail",
                Status = EmailQueueStatus.Pending
            });
            await db.SaveChangesAsync();

            var dispatcher = new EmailDispatcherService(db, smtp, sp.GetRequiredService<IConfiguration>(), clock, captureLogger);
            await dispatcher.ProcessDueEmailsAsync();
        }

        clock.Advance(TimeSpan.FromHours(25));

        using (var s2 = sp.CreateScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<NotificationDbContext>();
            var dispatcher = new EmailDispatcherService(db, smtp, sp.GetRequiredService<IConfiguration>(), clock, captureLogger);
            var cleaned = await dispatcher.CleanupExpiredBodiesAsync();
            Assert.Equal(1, cleaned);

            var item = await db.EmailQueue.SingleAsync();
            Assert.Equal(EmailQueueStatus.Sent, item.Status);
            Assert.Null(item.BodyHtml);
            Assert.DoesNotContain(captureLogger.Messages, m => m.Contains(secretToken, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Test_EM07_PendingEmailPersistsAcrossRestart_SentWhenWorkerResumes()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"noti_em07_{Guid.NewGuid():N}.db");
        try
        {
            using (var conn1 = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn1.Open();
                var smtpDown = new FakeSmtpClient { ShouldFail = true };
                using var sp1 = CreateInMemorySqliteProvider(conn1, out var c1, smtpDown, delays: [2, 4, 8]);
                var db1 = sp1.GetRequiredService<NotificationDbContext>();
                db1.EmailQueue.Add(new EmailQueueItem
                {
                    ToEmail = "em07@test.local",
                    Subject = "Chào mừng",
                    BodyHtml = "Body",
                    TemplateCode = "ConfirmEmail",
                    Status = EmailQueueStatus.Pending
                });
                await db1.SaveChangesAsync();
                await sp1.GetRequiredService<EmailDispatcherService>().ProcessDueEmailsAsync();
                Assert.Equal(EmailQueueStatus.Pending, (await db1.EmailQueue.SingleAsync()).Status);
            }

            // Simulate process restart on same DB file after backoff elapsed
            using (var conn2 = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn2.Open();
                var smtpUp = new FakeSmtpClient { ShouldFail = false };
                using var sp2 = CreateInMemorySqliteProvider(conn2, out var c2, smtpUp, delays: [2, 4, 8]);
                c2.Advance(TimeSpan.FromSeconds(10));
                await sp2.GetRequiredService<EmailDispatcherService>().ProcessDueEmailsAsync();
                var db2 = sp2.GetRequiredService<NotificationDbContext>();
                var item = await db2.EmailQueue.SingleAsync();
                Assert.Equal(EmailQueueStatus.Sent, item.Status);
                Assert.Equal(1, smtpUp.CallCount);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    private sealed class FakeSmtpClient : ISmtpEmailClient
    {
        public bool ShouldFail { get; set; }
        public int CallCount { get; private set; }
        public Task SendAsync(string toEmail, string subject, string bodyHtml, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ShouldFail) throw new InvalidOperationException("SMTP unavailable");
            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public TestTimeProvider(DateTimeOffset initial) => _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan ts) => _now = _now.Add(ts);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}

