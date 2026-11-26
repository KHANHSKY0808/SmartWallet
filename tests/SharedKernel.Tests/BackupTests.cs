using System.Net;
using System.Text;
using ApiGateway.Yarp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Extensions;

namespace SharedKernel.Tests;

public class BackupTests : IDisposable
{
    private readonly string _tempDir;

    public BackupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"qlct-bk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Test_BK01_BK03_SqliteBackupRunner_VacuumsValidDatabaseAndSha256()
    {
        var dbPath = Path.Combine(_tempDir, "sample.db");
        var connStr = $"Data Source={dbPath};Cache=Shared";
        var opts = new DbContextOptionsBuilder<TestBackupDbContext>().UseSqlite(connStr).Options;
        await using (var db = new TestBackupDbContext(opts))
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
            db.Items.Add(new TestItem { Id = 1, Name = "Backup Item" });
            await db.SaveChangesAsync();

            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Backup:Directory"] = Path.Combine(_tempDir, "backups") })
                .Build();

            var runner = new SqliteBackupRunner<TestBackupDbContext>(db);
            var res = await runner.ExecuteBackupAsync("IdentityService", "bk-unit-01", cfg);

            Assert.Equal("identity.db", res.FileName);
            Assert.True(res.SizeBytes > 0);
            Assert.Matches("^[0-9a-f]{64}$", res.Sha256);

            var outDb = Path.Combine(_tempDir, "backups", "bk-unit-01", "identity.db");
            await using var verifyConn = new SqliteConnection($"Data Source={outDb};Mode=ReadOnly");
            await verifyConn.OpenAsync();
            await using var cmd = verifyConn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", (await cmd.ExecuteScalarAsync())?.ToString());

            cmd.CommandText = "SELECT Name FROM Items WHERE Id = 1;";
            Assert.Equal("Backup Item", (await cmd.ExecuteScalarAsync())?.ToString());
        }
    }

    [Fact]
    public async Task Test_BK06_Retention_KeepsLatest14Backups()
    {
        var backupRoot = Path.Combine(_tempDir, "backups-bk06");
        var cfg = CreateGatewayConfig(backupRoot);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero));
        var factory = new MockBackupHttpClientFactory(failService: null);
        var coordinator = new BackupCoordinatorService(cfg, factory, NullLogger<BackupCoordinatorService>.Instance, clock);

        for (int i = 1; i <= 15; i++)
        {
            clock.Advance(TimeSpan.FromHours(1));
            var entry = await coordinator.ExecuteBackupAsync($"bk-{i:D2}");
            Assert.Equal("Completed", entry.Status);
        }

        var history = await coordinator.GetHistoryAsync();
        Assert.Equal(14, history.Count(e => e.Status == "Completed"));
        Assert.DoesNotContain(history, e => e.BackupId == "bk-01");
        Assert.Contains(history, e => e.BackupId == "bk-02");
        Assert.Contains(history, e => e.BackupId == "bk-15");
        Assert.False(File.Exists(Path.Combine(backupRoot, "bk-01.zip")));
        Assert.True(File.Exists(Path.Combine(backupRoot, "bk-15.zip")));
    }

    [Fact]
    public async Task Test_BK07_OneServiceFails_MarksPartiallyFailed_AndKeepsPreviousGoodBackup()
    {
        var backupRoot = Path.Combine(_tempDir, "backups-bk07");
        var cfg = CreateGatewayConfig(backupRoot);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 26, 1, 0, 0, TimeSpan.FromHours(7)));

        var okCoord = new BackupCoordinatorService(cfg, new MockBackupHttpClientFactory(null), NullLogger<BackupCoordinatorService>.Instance, clock);
        var goodEntry = await okCoord.ExecuteBackupAsync("bk-good");
        Assert.Equal("Completed", goodEntry.Status);

        clock.Advance(TimeSpan.FromMinutes(10));
        var partialCoord = new BackupCoordinatorService(cfg, new MockBackupHttpClientFactory("WalletService"), NullLogger<BackupCoordinatorService>.Instance, clock);
        var failedEntry = await partialCoord.ExecuteBackupAsync("bk-partial");

        Assert.Equal("PartiallyFailed", failedEntry.Status);
        Assert.Contains(failedEntry.FailedServices, f => f.Service == "WalletService");
        Assert.Equal(11, failedEntry.Files.Count);
        Assert.True(File.Exists(Path.Combine(backupRoot, "bk-good.zip")));
    }

    [Fact]
    public async Task Test_BK08_Scheduler_RunsOnceDailyAt0100VietnamTime()
    {
        var backupRoot = Path.Combine(_tempDir, "backups-bk08");
        var cfg = CreateGatewayConfig(backupRoot);
        // 00:59 VN = 17:59 UTC prev day -> should NOT trigger
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 26, 0, 59, 0, TimeSpan.FromHours(7)));
        var coordinator = new BackupCoordinatorService(cfg, new MockBackupHttpClientFactory(null), NullLogger<BackupCoordinatorService>.Instance, clock);
        var scheduler = new BackupSchedulerBackgroundService(coordinator, clock, NullLogger<BackupSchedulerBackgroundService>.Instance);

        Assert.False(await scheduler.CheckAndRunDueBackupAsync());

        // 01:05 VN -> triggers once
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.True(await scheduler.CheckAndRunDueBackupAsync());

        // 01:25 VN same day -> does NOT trigger again
        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.False(await scheduler.CheckAndRunDueBackupAsync());

        // Next day 01:02 VN -> triggers once more
        clock.Advance(TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(37)));
        Assert.True(await scheduler.CheckAndRunDueBackupAsync());

        var history = await coordinator.GetHistoryAsync();
        Assert.Equal(2, history.Count);
    }

    private static IConfiguration CreateGatewayConfig(string backupRoot)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Backup:Directory"] = backupRoot,
            ["Jwt:SigningKey"] = "SuperSecretUnitTestKeyAtLeast32BytesLong!"
        }).Build();

    private sealed class TestBackupDbContext : DbContext
    {
        public TestBackupDbContext(DbContextOptions<TestBackupDbContext> options) : base(options) { }
        public DbSet<TestItem> Items => Set<TestItem>();
    }

    private sealed class TestItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class MockBackupHttpClientFactory : IHttpClientFactory
    {
        private readonly string? _failService;
        public MockBackupHttpClientFactory(string? failService) => _failService = failService;

        public HttpClient CreateClient(string name)
            => new(new MockHandler(_failService));

        private sealed class MockHandler : HttpMessageHandler
        {
            private readonly string? _failService;
            public MockHandler(string? failService) => _failService = failService;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var port = request.RequestUri?.Port ?? 0;
                var match = BackupCoordinatorService.MonitoredServices.FirstOrDefault(s => new Uri(s.DefaultUrl).Port == port);
                var svcName = match?.Name ?? "UnknownService";
                if (_failService != null && svcName.Equals(_failService, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                }

                var payload = $"{{\"service\":\"{svcName}\",\"fileName\":\"{svcName}.db\",\"sizeBytes\":1024,\"sha256\":\"{new string('a', 64)}\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                });
            }
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeClock(DateTimeOffset initial) => _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan ts) => _now = _now.Add(ts);
    }
}


// DK-03 intentional failure gate test
public class CiRedGateCheck { [Fact] public void Test_DK03_IntentionalFailure_ShouldBlockCI() => Assert.Fail("DK-03: intentional CI gate verification"); }

