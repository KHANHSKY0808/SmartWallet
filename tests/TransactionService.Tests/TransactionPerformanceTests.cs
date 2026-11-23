using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransactionService.Controllers;
using TransactionService.Data;
using TransactionService.DTOs;
using TransactionService.Models;
using Xunit;
using Xunit.Abstractions;

namespace TransactionService.Tests;

public class TransactionPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly SqliteConnection _connection;
    private readonly TransactionDbContext _context;
    private readonly TransactionsController _controller;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();

    public TransactionPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TransactionDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TransactionDbContext(options);
        _context.Database.EnsureCreated();

        _controller = new TransactionsController(
            _context,
            new FakeWalletClient(),
            new FakeCategoryClient(),
            NullLogger<TransactionsController>.Instance,
            new FakeEventPublisher()
        );

        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
            new Claim("sub", _userId.ToString()),
            new Claim(ClaimTypes.Role, "User")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    [Fact]
    public async Task FIN46_Performance_50KRecords_MultiFilterQuery_ResponseUnder500ms()
    {
        // 1. Seed 50.000 records theo từng đợt để tối ưu thời gian khởi tạo
        const int totalRecords = 50_000;
        const int batchSize = 2_500;
        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            for (int b = 0; b < totalRecords / batchSize; b++)
            {
                var batch = new List<Transaction>(batchSize);
                for (int i = 0; i < batchSize; i++)
                {
                    int index = b * batchSize + i;
                    batch.Add(new Transaction
                    {
                        Id = Guid.NewGuid(),
                        UserId = _userId,
                        WalletId = (index % 5 == 0) ? _walletId : Guid.NewGuid(),
                        CategoryId = (index % 10 == 0) ? _categoryId : Guid.NewGuid(),
                        Type = (index % 2 == 0) ? TransactionType.Expense : TransactionType.Income,
                        Amount = 10_000m + (index % 500_000),
                        TransactionDate = baseDate.AddMinutes(index * 10),
                        Note = (index % 100 == 0) ? $"cơm trưa văn phòng {index}" : $"chi tiêu sinh hoạt {index}",
                        RowVersion = Guid.NewGuid(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                _context.Transactions.AddRange(batch);
                await _context.SaveChangesAsync();
                _context.ChangeTracker.Clear();
            }
            await transaction.CommitAsync();
        }

        var count = await _context.Transactions.CountAsync(t => t.UserId == _userId);
        Assert.Equal(totalRecords, count);

        // 2. Chạy Warmup 1 lần để compiled query và cache khởi tạo
        var warmupParams = new TransactionQueryParameters
        {
            Page = 1,
            PageSize = 20,
            Type = 1
        };
        await _controller.GetTransactions(warmupParams);

        // 3. Thực thi truy vấn lọc phức tạp: Lọc theo thời gian + ví + danh mục + loại + phân trang + sắp xếp
        var queryParams = new TransactionQueryParameters
        {
            Page = 1,
            PageSize = 20,
            StartDate = baseDate.AddDays(30),
            EndDate = baseDate.AddDays(180),
            WalletId = _walletId,
            CategoryId = _categoryId,
            Type = 1, // Expense
            SortBy = "transactionDate",
            SortOrder = "desc"
        };

        var stopwatch = Stopwatch.StartNew();
        var actionResult = await _controller.GetTransactions(queryParams);
        stopwatch.Stop();

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var pagedResult = Assert.IsType<PagedResult<TransactionDto>>(okResult.Value);

        _output.WriteLine($"==================================================");
        _output.WriteLine($"[FIN-46 PERFORMANCE BENCHMARK RESULT]");
        _output.WriteLine($"Database: 50,000 transactions indexed with composite indexes");
        _output.WriteLine($"Query: Multi-filter (Date Range + WalletId + CategoryId + Type=1 + Sort + Paging)");
        _output.WriteLine($"Execution Time: {stopwatch.ElapsedMilliseconds} ms (Target SLA: < 500 ms)");
        _output.WriteLine($"Matching Records Found: {pagedResult.TotalItems}");
        _output.WriteLine($"Items in Page 1: {pagedResult.Items.Count}");
        _output.WriteLine($"==================================================");

        // Khẳng định tiêu chuẩn chất lượng SLA < 500ms theo FIN-46
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"FIN-46 Violated: Truy vấn trên 50.000 bản ghi mất {stopwatch.ElapsedMilliseconds} ms (yêu cầu < 500 ms)");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}

