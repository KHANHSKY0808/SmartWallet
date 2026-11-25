using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ReportService.Controllers;
using ReportService.Data;
using ReportService.DTOs;
using ReportService.Models;
using ReportService.Services;
using Xunit.Abstractions;

namespace ReportService.Tests;

public class DashboardAnalysisPerformanceTests : IDisposable
{
    private class InMemoryCacheService : IReportCacheService
    {
        private readonly Dictionary<string, object> _store = new();

        public Task<T?> GetAsync<T>(Guid userId, string cacheSubKey, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue($"{userId}:{cacheSubKey}", out var val) ? (T?)val : default);

        public Task SetAsync<T>(Guid userId, string cacheSubKey, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _store[$"{userId}:{cacheSubKey}"] = value!;
            return Task.CompletedTask;
        }

        public Task InvalidateUserCacheAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateAllUsersCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class NullCacheService : IReportCacheService
    {
        public Task<T?> GetAsync<T>(Guid userId, string cacheSubKey, CancellationToken cancellationToken = default) => Task.FromResult(default(T));
        public Task SetAsync<T>(Guid userId, string cacheSubKey, T value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateUserCacheAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateAllUsersCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeRateClient : IExchangeRateClient
    {
        public Task<decimal?> GetRateToVndAsync(string baseCurrency, CancellationToken cancellationToken = default)
            => Task.FromResult<decimal?>(baseCurrency == "VND" ? 1m : 25000m);
    }

    private class UnavailableBudgetClient : IBudgetClient
    {
        public Task<(List<DashboardBudgetDto>? Budgets, bool Unavailable)> GetBudgetsAsync(int month, int year, string? bearerToken, CancellationToken cancellationToken = default)
            => Task.FromResult<(List<DashboardBudgetDto>?, bool)>((null, true));
    }

    private readonly ITestOutputHelper _output;
    private readonly SqliteConnection _connection;
    private readonly ReportDbContext _db;

    public DashboardAnalysisPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ReportDbContext>().UseSqlite(_connection).Options;
        _db = new ReportDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task Test_DS08_BudgetServiceDown_Returns200WithBudgetsUnavailable()
    {
        var queryService = new ReportQueryService(_db, new NullCacheService(), new FakeRateClient(), new UnavailableBudgetClient(), NullLogger<ReportQueryService>.Instance);
        var sw = Stopwatch.StartNew();
        var res = await queryService.GetDashboardAsync(Guid.NewGuid(), 11, 2026, "Bearer token");
        sw.Stop();

        Assert.True(res.BudgetsUnavailable);
        Assert.Null(res.Budgets);
        Assert.True(sw.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public async Task Test_DS09_InvalidMonthYearAndUnauthorized()
    {
        var queryService = new ReportQueryService(_db, new NullCacheService(), new FakeRateClient(), new UnavailableBudgetClient(), NullLogger<ReportQueryService>.Instance);
        var controller = new ReportsController(_db, queryService, new ConfigurationBuilder().Build());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "Test"))
            }
        };

        Assert.IsType<BadRequestObjectResult>(await controller.GetDashboard(13, 2026, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetDashboard(1, 1999, CancellationToken.None));
    }

    [Fact]
    public async Task Test_DS10_Performance_50000_Transactions_Under_2s_And_Cached_Under_200ms()
    {
        var userId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        _db.ReportCategories.Add(new ReportCategoryCache { CategoryId = catId, UserId = userId, Name = "Ăn uống" });

        // Tạo 50.000 bản ghi giao dịch trong TransactionProjections
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        var baseDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var batch = new List<TransactionProjection>(50000);
        for (int i = 0; i < 50000; i++)
        {
            batch.Add(new TransactionProjection
            {
                TransactionId = Guid.NewGuid(),
                UserId = userId,
                WalletId = walletId,
                CategoryId = catId,
                CategoryName = "Ăn uống",
                Type = (i % 5 == 0) ? 2 : 1,
                Amount = 100_000m,
                Currency = "VND",
                TransactionDate = baseDate.AddMinutes(i % 40000),
                LastEventAt = DateTime.UtcNow
            });
        }
        await _db.TransactionProjections.AddRangeAsync(batch);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.AutoDetectChangesEnabled = true;

        var cache = new InMemoryCacheService();
        var queryService = new ReportQueryService(_db, cache, new FakeRateClient(), new UnavailableBudgetClient(), NullLogger<ReportQueryService>.Instance);

        // Lần 1: Chưa có cache (< 2000 ms)
        var swUncached = Stopwatch.StartNew();
        var res1 = await queryService.GetDashboardAsync(userId, 11, 2026, null);
        swUncached.Stop();

        // Lần 2: Có cache (< 200 ms)
        var swCached = Stopwatch.StartNew();
        var res2 = await queryService.GetDashboardAsync(userId, 11, 2026, null);
        swCached.Stop();

        _output.WriteLine($"DS-10 (50,000 rows): Uncached = {swUncached.ElapsedMilliseconds} ms, Cached = {swCached.ElapsedMilliseconds} ms");

        Assert.Equal(res1.TotalExpense, res2.TotalExpense);
        Assert.True(swUncached.ElapsedMilliseconds < 2000, $"Uncached too slow: {swUncached.ElapsedMilliseconds}ms");
        Assert.True(swCached.ElapsedMilliseconds < 200, $"Cached too slow: {swCached.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test_AN04_WeekGroupingStartsMonday_Sunday2330VietnamInSameWeek()
    {
        var userId = Guid.NewGuid();
        // 2026-11-29 là Chủ Nhật. 23:30 VN = 2026-11-29T16:30:00Z -> thuộc tuần bắt đầu thứ Hai 2026-11-23
        var sunday2330VnInUtc = new DateTime(2026, 11, 29, 16, 30, 0, DateTimeKind.Utc);
        _db.TransactionProjections.Add(new TransactionProjection
        {
            TransactionId = Guid.NewGuid(),
            UserId = userId,
            WalletId = Guid.NewGuid(),
            Type = 1,
            Amount = 75_000m,
            Currency = "VND",
            TransactionDate = sunday2330VnInUtc,
            LastEventAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var queryService = new ReportQueryService(_db, new NullCacheService(), new FakeRateClient(), new UnavailableBudgetClient(), NullLogger<ReportQueryService>.Instance);
        var res = await queryService.GetAnalysisAsync(userId, new DateOnly(2026, 11, 23), new DateOnly(2026, 12, 6), "week", false);

        var week1 = res.Points.First(p => p.Period == "2026-11-23");
        Assert.Equal(75_000m, week1.Expense);
    }

    [Fact]
    public async Task Test_AN05_Validation_InvalidRangesReturn400()
    {
        var queryService = new ReportQueryService(_db, new NullCacheService(), new FakeRateClient(), new UnavailableBudgetClient(), NullLogger<ReportQueryService>.Instance);
        var controller = new ReportsController(_db, queryService, new ConfigurationBuilder().Build());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "Test"))
            }
        };

        Assert.IsType<BadRequestObjectResult>(await controller.GetAnalysis("2026-11-30", "2026-11-01", "month"));
        Assert.IsType<BadRequestObjectResult>(await controller.GetAnalysis("2026-11-01", "2026-11-30", "hour"));
        Assert.IsType<BadRequestObjectResult>(await controller.GetAnalysis("2023-01-01", "2026-11-30", "month"));
        Assert.IsType<BadRequestObjectResult>(await controller.GetAnalysis("2025-01-01", "2026-11-30", "day"));
    }

    [Fact]
    public void Test_AN06_PercentChange_RoundingAwayFromZeroOneDecimal()
    {
        // (105 - 100) / 100 * 100 = 5.0
        Assert.Equal(5.0m, ReportQueryService.CalculatePercentChange(105m, 100m));
        // 1/8 = 12.5%; 0.15% midpoint away from zero: (1001.5 - 1000) / 1000 * 100 = 0.15 -> 0.2
        Assert.Equal(0.2m, ReportQueryService.CalculatePercentChange(1001.5m, 1000m));
        Assert.Null(ReportQueryService.CalculatePercentChange(500m, 0m));
    }
}

