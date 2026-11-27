using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReportService.Consumers;
using ReportService.Data;
using ReportService.Models;
using ReportService.Services;
using SharedKernel.Events.EventContracts;
using SharedKernel.Extensions;

namespace ReportService.Tests;

public class ReportSyncAndBackfillTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _sp;
    private readonly ReportDbContext _db;
    private readonly IConfiguration _config;

    public ReportSyncAndBackfillTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<ReportDbContext>(o => o.UseSqlite(_connection));
        _sp = services.BuildServiceProvider();
        _db = _sp.GetRequiredService<ReportDbContext>();
        _db.Database.EnsureCreated();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "RabbitMQ:Host", "localhost" },
                { "RabbitMQ:Port", "5672" },
                { "Jwt:SigningKey", "QA-E2E-TEST-ONLY-signing-key-do-not-use-0123456789" }
            })
            .Build();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task Test_RP04_OutOfOrderOlderUpdateIgnored()
    {
        var consumer = new TransactionUpdatedConsumer(_config, NullLogger<TransactionUpdatedConsumer>.Instance, _sp);
        var txId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tNew = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        var tOld = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

        await consumer.HandleEventAsync(new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = userId,
            NewWalletId = Guid.NewGuid(),
            NewCategoryId = Guid.NewGuid(),
            CategoryName = "Mới",
            NewType = 1,
            OldAmount = 100_000m,
            NewAmount = 500_000m,
            Currency = "VND",
            TransactionDate = tNew,
            OccurredAt = tNew
        });

        // Event cũ đến sau
        await consumer.HandleEventAsync(new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = userId,
            NewWalletId = Guid.NewGuid(),
            NewCategoryId = Guid.NewGuid(),
            CategoryName = "Cũ",
            NewType = 1,
            OldAmount = 50_000m,
            NewAmount = 100_000m,
            Currency = "VND",
            TransactionDate = tOld,
            OccurredAt = tOld
        });

        _db.ChangeTracker.Clear();
        var proj = await _db.TransactionProjections.FirstAsync(p => p.TransactionId == txId);
        Assert.Equal(500_000m, proj.Amount);
        Assert.Equal("Mới", proj.CategoryName);
    }

    [Fact]
    public async Task Test_RP05_DuplicateAndUpdatedAfterDeleted_IdempotentAndNotResurrected()
    {
        var createdConsumer = new TransactionCreatedConsumer(_config, NullLogger<TransactionCreatedConsumer>.Instance, _sp);
        var deletedConsumer = new TransactionDeletedConsumer(_config, NullLogger<TransactionDeletedConsumer>.Instance, _sp);
        var updatedConsumer = new TransactionUpdatedConsumer(_config, NullLogger<TransactionUpdatedConsumer>.Instance, _sp);

        var txId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var t1 = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMinutes(1);
        var t3 = t2.AddMinutes(1);

        var createdEvt = new TransactionCreatedEvent
        {
            TransactionId = txId,
            UserId = userId,
            WalletId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            CategoryName = "Ăn uống",
            Type = 1,
            Amount = 200_000m,
            Currency = "VND",
            TransactionDate = t1,
            OccurredAt = t1
        };

        await createdConsumer.HandleEventAsync(createdEvt);
        await createdConsumer.HandleEventAsync(createdEvt); // Giao lại 2 lần

        Assert.Equal(1, await _db.TransactionProjections.CountAsync(p => p.TransactionId == txId));

        await deletedConsumer.HandleEventAsync(new TransactionDeletedEvent
        {
            TransactionId = txId,
            UserId = userId,
            WalletId = createdEvt.WalletId,
            Type = 1,
            Amount = 200_000m,
            OccurredAt = t2
        });

        // Updated đến sau Deleted (thậm chí với OccurredAt lớn hơn) không được làm sống lại giao dịch
        await updatedConsumer.HandleEventAsync(new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = userId,
            NewWalletId = createdEvt.WalletId,
            NewCategoryId = createdEvt.CategoryId,
            NewType = 1,
            OldAmount = 200_000m,
            NewAmount = 999_000m,
            TransactionDate = t3,
            OccurredAt = t3
        });

        _db.ChangeTracker.Clear();
        var proj = await _db.TransactionProjections.FirstAsync(p => p.TransactionId == txId);
        Assert.True(proj.IsDeleted);
        Assert.Equal(200_000m, proj.Amount);
    }

    private class FakeBackfillClient : ITransactionBackfillClient
    {
        public List<BackfillTransactionItem> SourceItems { get; } = new();
        public Task<(List<BackfillTransactionItem> Items, int TotalCount)> FetchPageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var pageItems = SourceItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult((pageItems, SourceItems.Count));
        }
    }

    private class FakeWalletLookup : IWalletLookupClient
    {
        public Task<(bool Exists, string Currency)> GetWalletAsync(Guid walletId, CancellationToken cancellationToken = default)
            => Task.FromResult((true, "VND"));
    }

    private class FakeCategoryLookup : ICategoryLookupClient
    {
        public Task<(bool Exists, string Name, Guid? ParentId)> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
            => Task.FromResult<(bool, string, Guid?)>((true, "Ăn uống", null));
    }

    [Fact]
    public async Task Test_RP06_BackfillWhenProjectionEmpty_PopulatesProjections()
    {
        var fakeTxClient = new FakeBackfillClient();
        fakeTxClient.SourceItems.Add(new BackfillTransactionItem(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1, 150_000m, new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), "Backfill", DateTime.UtcNow));

        var backfill = new BackfillService(_db, fakeTxClient, new FakeWalletLookup(), new FakeCategoryLookup(), NullLogger<BackfillService>.Instance);
        var count = await backfill.BackfillIfEmptyAsync();

        Assert.Equal(1, count);
        Assert.Equal(1, await _db.TransactionProjections.CountAsync());
    }

    [Fact]
    public async Task Test_N52_TransactionCreatedConsumer_ThroughputAtLeast200EventsPerSecond()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"report-perf-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<ReportDbContext>(o => o.UseSqlite($"Data Source={tempDb};Cache=Shared"));
            using var sp = services.BuildServiceProvider();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
                db.Database.EnsureCreated();
                db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;");
            }

            var consumer = new TransactionCreatedConsumer(_config, NullLogger<TransactionCreatedConsumer>.Instance, sp);
            var userId = Guid.NewGuid();
            var walletId = Guid.NewGuid();
            var catId = Guid.NewGuid();
            const int eventCount = 600;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < eventCount; i++)
            {
                await consumer.HandleEventAsync(new TransactionCreatedEvent
                {
                    TransactionId = Guid.NewGuid(),
                    UserId = userId,
                    WalletId = walletId,
                    CategoryId = catId,
                    CategoryName = "Ăn uống",
                    Type = 1,
                    Amount = 10_000m + i,
                    Currency = "VND",
                    TransactionDate = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
                    OccurredAt = DateTime.UtcNow
                });
            }
            sw.Stop();

            var rate = eventCount / sw.Elapsed.TotalSeconds;
            Assert.True(rate >= 200.0, $"Throughput too slow: {rate:F1} events/s (< 200 events/s), elapsed={sw.Elapsed.TotalSeconds:F2}s");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var p = tempDb + suffix;
                if (File.Exists(p)) File.Delete(p);
            }
        }
    }
}

