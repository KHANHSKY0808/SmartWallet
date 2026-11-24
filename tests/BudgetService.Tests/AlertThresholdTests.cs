using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events.EventContracts;
using BudgetService.Data;
using BudgetService.Models;
using BudgetService.Services;

namespace BudgetService.Tests;

public class AlertThresholdTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BudgetDbContext _context;
    private readonly FakeEventPublisher _eventPublisher;
    private readonly BudgetLedgerService _ledgerService;
    private readonly Guid _userId = Guid.NewGuid();

    public AlertThresholdTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BudgetDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new BudgetDbContext(options);
        _context.Database.EnsureCreated();

        _eventPublisher = new FakeEventPublisher();
        _ledgerService = new BudgetLedgerService(_context, NullLogger<BudgetLedgerService>.Instance, _eventPublisher);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task A01_SpentReaches80Percent_Publishes80AlertOnce()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            CategoryName = "Ăn uống",
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 0m,
            IsAlertSent80 = false
        };
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        // Chi 1.6M (80% của 2M)
        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 1600000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var updated = await _context.Budgets.FindAsync(budget.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsAlertSent80);
        Assert.False(updated.IsAlertSent100);
        Assert.Single(_eventPublisher.PublishedEvents);

        var alert = Assert.IsType<BudgetThresholdReachedEvent>(_eventPublisher.PublishedEvents[0].Event);
        Assert.Equal(80, alert.Percent);
        Assert.Equal("Ăn uống", alert.CategoryName);
        Assert.Equal("Ăn uống", alert.CategoryName);
        Assert.Equal(1600000m, alert.CurrentSpent);
        Assert.Equal(2000000m, alert.BudgetLimit);
    }

    [Fact]
    public async Task A02_SpentReaches100Percent_Publishes100Alert()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            CategoryName = "Ăn uống",
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 1600000m,
            IsAlertSent80 = true,
            IsAlertSent100 = false
        };
        _context.Budgets.Add(budget);
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budget.Id,
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            Amount = 1600000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        // Chi thêm 500K -> Tổng chi 2.1M (105% >= 100%)
        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 500000m,
            TransactionDate = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var updated = await _context.Budgets.FindAsync(budget.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsAlertSent80);
        Assert.True(updated.IsAlertSent100);
        Assert.Single(_eventPublisher.PublishedEvents);

        var alert = Assert.IsType<BudgetThresholdReachedEvent>(_eventPublisher.PublishedEvents[0].Event);
        Assert.Equal(100, alert.Percent);
        Assert.Equal("Ăn uống", alert.CategoryName);
        Assert.Equal("Ăn uống", alert.CategoryName);
        Assert.Equal(2100000m, alert.CurrentSpent);
    }

    [Fact]
    public async Task A03_SpentExceedsFurther_DoesNotPublishDuplicate()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            CategoryName = "Ăn uống",
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 2100000m,
            IsAlertSent80 = true,
            IsAlertSent100 = true
        };
        _context.Budgets.Add(budget);
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budget.Id,
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            Amount = 2100000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        // Chi tiếp 3 lần nữa
        for (int i = 0; i < 3; i++)
        {
            var txEvent = new TransactionCreatedEvent
            {
                TransactionId = Guid.NewGuid(),
                UserId = _userId,
                WalletId = Guid.NewGuid(),
                CategoryId = categoryId,
                Type = 1,
                Amount = 100000m,
                TransactionDate = new DateTime(2026, 11, 17 + i, 10, 0, 0, DateTimeKind.Utc)
            };
            await _ledgerService.HandleTransactionCreatedAsync(txEvent);
        }

        // Không phát thêm bất kỳ alert nào vì cả 2 cờ đã bật
        Assert.Empty(_eventPublisher.PublishedEvents);
    }

    [Fact]
    public async Task A04_SpentJumpsFromBelow80ToAbove100_PublishesSingle100Alert()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            CategoryName = "Ăn uống",
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 1000000m, // 50%
            IsAlertSent80 = false,
            IsAlertSent100 = false
        };
        _context.Budgets.Add(budget);
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budget.Id,
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            Amount = 1000000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        // Chi 1 lần 1.4M -> Tổng 2.4M (120% >= 100%)
        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 1400000m,
            TransactionDate = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var updated = await _context.Budgets.FindAsync(budget.Id);
        Assert.NotNull(updated);
        // D35: Bật cả 2 cờ nhưng CHỈ phát đúng 1 event Percent = 100
        Assert.True(updated.IsAlertSent80);
        Assert.True(updated.IsAlertSent100);
        Assert.Single(_eventPublisher.PublishedEvents);

        var alert = Assert.IsType<BudgetThresholdReachedEvent>(_eventPublisher.PublishedEvents[0].Event);
        Assert.Equal(100, alert.Percent);
        Assert.Equal("Ăn uống", alert.CategoryName);
    }

    [Fact]
    public async Task A05_SpentDropsBelowThresholdAndRisesAgain_ReAlerts()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            CategoryName = "Ăn uống",
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 1700000m, // 85%
            IsAlertSent80 = true,
            IsAlertSent100 = false
        };
        _context.Budgets.Add(budget);

        var txId1 = Guid.NewGuid();
        var txId2 = Guid.NewGuid();
        _context.BudgetTransactions.AddRange(
            new BudgetTransaction
            {
                BudgetId = budget.Id,
                TransactionId = txId1,
                UserId = _userId,
                Amount = 1400000m,
                TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
            },
            new BudgetTransaction
            {
                BudgetId = budget.Id,
                TransactionId = txId2,
                UserId = _userId,
                Amount = 300000m,
                TransactionDate = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc)
            });
        await _context.SaveChangesAsync();

        // 1. Xóa txId2 (300K) -> Spent giảm xuống 1.4M (70% < 80%) -> Cờ 80 được gỡ
        var deleteEvent = new TransactionDeletedEvent
        {
            TransactionId = txId2,
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 300000m
        };
        await _ledgerService.HandleTransactionDeletedAsync(deleteEvent);

        var bAfterDelete = await _context.Budgets.FindAsync(budget.Id);
        Assert.False(bAfterDelete!.IsAlertSent80); // Đã gỡ cờ 80

        // 2. Chi lại 240K -> Spent tăng lên 1.64M (82% >= 80%) -> Phát LẠI event 80%
        var newTxEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 240000m,
            TransactionDate = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc)
        };
        await _ledgerService.HandleTransactionCreatedAsync(newTxEvent);

        var bAfterNewTx = await _context.Budgets.FindAsync(budget.Id);
        Assert.True(bAfterNewTx!.IsAlertSent80);
        Assert.Single(_eventPublisher.PublishedEvents);

        var alert = Assert.IsType<BudgetThresholdReachedEvent>(_eventPublisher.PublishedEvents[0].Event);
        Assert.Equal(80, alert.Percent);
        Assert.Equal("Ăn uống", alert.CategoryName);
    }

    [Fact]
    public async Task A06_ConcurrentTransactions_PublishesSingleAlert()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            CategoryName = "Ăn uống",
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 1500000m,
            IsAlertSent80 = false
        };
        _context.Budgets.Add(budget);
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budget.Id,
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            Amount = 1500000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        var txEvent1 = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 150000m,
            TransactionDate = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc)
        };

        var txEvent2 = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 150000m,
            TransactionDate = new DateTime(2026, 11, 16, 10, 0, 1, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent1);
        await _ledgerService.HandleTransactionCreatedAsync(txEvent2);

        // Chỉ có đúng 1 event 80% được phát ra
        Assert.Single(_eventPublisher.PublishedEvents);
    }

    [Fact]
    public async Task A07_EventPublishFailure_RollsBackTransaction()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            CategoryName = "Ăn uống",
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 0m,
            IsAlertSent80 = false
        };
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        _eventPublisher.ShouldThrow = true;

        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 1600000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _ledgerService.HandleTransactionCreatedAsync(txEvent));

        // DB transaction đã rollback: Reload entity từ DB để kiểm tra rollback thực sự ở database
        await _context.Entry(budget).ReloadAsync();
        Assert.Equal(0m, budget.CurrentSpent);
        Assert.False(budget.IsAlertSent80);
    }

    [Fact]
    public async Task A08_EventPayload_ContainsRequiredFields()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            CategoryName = "Ăn uống",
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 0m
        };
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 1600000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var alert = Assert.IsType<BudgetThresholdReachedEvent>(_eventPublisher.PublishedEvents[0].Event);
        Assert.NotEqual(Guid.Empty, alert.Id);
        Assert.Equal(budget.Id, alert.BudgetId);
        Assert.Equal(_userId, alert.UserId);
        Assert.Equal(categoryId, alert.CategoryId);
        Assert.Equal("Ăn uống", alert.CategoryName);
        Assert.Equal(80, alert.Percent);
        Assert.Equal(1600000m, alert.CurrentSpent);
        Assert.Equal(2000000m, alert.BudgetLimit);
        Assert.Equal(11, alert.Month);
        Assert.Equal(2026, alert.Year);
    }
}
