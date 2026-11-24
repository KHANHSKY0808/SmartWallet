using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using BudgetService.Data;
using BudgetService.Models;
using BudgetService.Services;

namespace BudgetService.Tests;

public class TransactionEventsConsumerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BudgetDbContext _context;
    private readonly FakeEventPublisher _eventPublisher;
    private readonly BudgetLedgerService _ledgerService;
    private readonly Guid _userId = Guid.NewGuid();

    public TransactionEventsConsumerTests()
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
    public async Task P01_TransactionCreated_IncreasesSpent()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
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
            Type = 1, // Expense
            Amount = 500000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var updatedBudget = await _context.Budgets.FindAsync(budget.Id);
        Assert.NotNull(updatedBudget);
        Assert.Equal(500000m, updatedBudget.CurrentSpent);
    }

    [Fact]
    public async Task P02_TransactionCreated_Income_DoesNotChangeSpent()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 300000m
        };
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 2, // Income
            Amount = 10000000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var updatedBudget = await _context.Budgets.FindAsync(budget.Id);
        Assert.NotNull(updatedBudget);
        Assert.Equal(300000m, updatedBudget.CurrentSpent); // Không đổi
    }

    [Fact]
    public async Task P03_TransactionCreated_Transfer_DoesNotChangeSpent()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 300000m
        };
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 3, // Transfer
            Amount = 5000000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var updatedBudget = await _context.Budgets.FindAsync(budget.Id);
        Assert.NotNull(updatedBudget);
        Assert.Equal(300000m, updatedBudget.CurrentSpent); // Bỏ qua
    }

    [Fact]
    public async Task P04_TransactionCreated_DuplicateEvent_Idempotent()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
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
            Amount = 500000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        // Giao 2 lần
        await _ledgerService.HandleTransactionCreatedAsync(txEvent);
        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var updatedBudget = await _context.Budgets.FindAsync(budget.Id);
        Assert.NotNull(updatedBudget);
        Assert.Equal(500000m, updatedBudget.CurrentSpent); // Chỉ tính 1 lần

        var ledgerCount = await _context.BudgetTransactions.CountAsync(bt => bt.BudgetId == budget.Id);
        Assert.Equal(1, ledgerCount);
    }

    [Fact]
    public async Task P05_TransactionUpdated_AmountDecreased_UpdatesSpent()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 1600000m,
            IsAlertSent80 = true
        };
        _context.Budgets.Add(budget);

        var txId = Guid.NewGuid();
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budget.Id,
            TransactionId = txId,
            UserId = _userId,
            Amount = 1600000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        var updateEvent = new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            OldWalletId = Guid.NewGuid(),
            NewWalletId = Guid.NewGuid(),
            OldCategoryId = categoryId,
            NewCategoryId = categoryId,
            OldType = 1,
            NewType = 1,
            OldAmount = 1600000m,
            NewAmount = 1500000m, // Giảm còn 1.5M (75%)
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionUpdatedAsync(updateEvent);

        var updatedBudget = await _context.Budgets.FindAsync(budget.Id);
        Assert.NotNull(updatedBudget);
        Assert.Equal(1500000m, updatedBudget.CurrentSpent);
        Assert.False(updatedBudget.IsAlertSent80); // Gỡ cờ khi spent < 80% (1.5M / 2.0M = 75%)
    }

    [Fact]
    public async Task P06_TransactionDeleted_ReducesSpent()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 1000000m
        };
        _context.Budgets.Add(budget);

        var txId = Guid.NewGuid();
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budget.Id,
            TransactionId = txId,
            UserId = _userId,
            Amount = 400000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        var deleteEvent = new TransactionDeletedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 400000m
        };

        await _ledgerService.HandleTransactionDeletedAsync(deleteEvent);

        var updatedBudget = await _context.Budgets.FindAsync(budget.Id);
        Assert.NotNull(updatedBudget);
        Assert.Equal(0m, updatedBudget.CurrentSpent); // Sổ cái chỉ có 1 tx này, xóa đi còn 0
    }

    [Fact]
    public async Task P07_TransactionUpdated_CategoryChanged_MovesLedger()
    {
        var catA = Guid.NewGuid();
        var catB = Guid.NewGuid();

        var budgetA = new Budget
        {
            UserId = _userId,
            CategoryId = catA,
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 500000m
        };
        var budgetB = new Budget
        {
            UserId = _userId,
            CategoryId = catB,
            Amount = 1000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 0m
        };
        _context.Budgets.AddRange(budgetA, budgetB);

        var txId = Guid.NewGuid();
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budgetA.Id,
            TransactionId = txId,
            UserId = _userId,
            Amount = 500000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        var updateEvent = new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            OldWalletId = Guid.NewGuid(),
            NewWalletId = Guid.NewGuid(),
            OldCategoryId = catA,
            NewCategoryId = catB,
            OldType = 1,
            NewType = 1,
            OldAmount = 500000m,
            NewAmount = 500000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionUpdatedAsync(updateEvent);

        var bA = await _context.Budgets.FindAsync(budgetA.Id);
        var bB = await _context.Budgets.FindAsync(budgetB.Id);

        Assert.Equal(0m, bA!.CurrentSpent);
        Assert.Equal(500000m, bB!.CurrentSpent);
    }

    [Fact]
    public async Task P08_TransactionUpdated_DateChangedToNextMonth_MovesLedger()
    {
        var categoryId = Guid.NewGuid();
        var budgetNov = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 500000m
        };
        var budgetDec = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 12,
            Year = 2026,
            CurrentSpent = 0m
        };
        _context.Budgets.AddRange(budgetNov, budgetDec);

        var txId = Guid.NewGuid();
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budgetNov.Id,
            TransactionId = txId,
            UserId = _userId,
            Amount = 500000m,
            TransactionDate = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        var updateEvent = new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            OldWalletId = Guid.NewGuid(),
            NewWalletId = Guid.NewGuid(),
            OldCategoryId = categoryId,
            NewCategoryId = categoryId,
            OldType = 1,
            NewType = 1,
            OldAmount = 500000m,
            NewAmount = 500000m,
            TransactionDate = new DateTime(2026, 12, 5, 10, 0, 0, DateTimeKind.Utc) // Chuyển sang tháng 12
        };

        await _ledgerService.HandleTransactionUpdatedAsync(updateEvent);

        var bNov = await _context.Budgets.FindAsync(budgetNov.Id);
        var bDec = await _context.Budgets.FindAsync(budgetDec.Id);

        Assert.Equal(0m, bNov!.CurrentSpent);
        Assert.Equal(500000m, bDec!.CurrentSpent);
    }

    [Fact]
    public async Task P09_TransactionUpdated_TypeChangedToIncome_RemovesFromLedger()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 500000m
        };
        _context.Budgets.Add(budget);

        var txId = Guid.NewGuid();
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budget.Id,
            TransactionId = txId,
            UserId = _userId,
            Amount = 500000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        var updateEvent = new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            OldWalletId = Guid.NewGuid(),
            NewWalletId = Guid.NewGuid(),
            OldCategoryId = categoryId,
            NewCategoryId = categoryId,
            OldType = 1,
            NewType = 2, // Đổi sang Thu
            OldAmount = 500000m,
            NewAmount = 500000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionUpdatedAsync(updateEvent);

        var b = await _context.Budgets.FindAsync(budget.Id);
        Assert.Equal(0m, b!.CurrentSpent);
        var ledger = await _context.BudgetTransactions.FirstOrDefaultAsync(bt => bt.BudgetId == budget.Id && bt.TransactionId == txId);
        Assert.Null(ledger);
    }

    [Fact]
    public async Task P10_TransactionCreated_ChildCategory_UpdatesBothParentAndChild()
    {
        var parentCat = Guid.NewGuid();
        var childCat = Guid.NewGuid();

        var budgetParent = new Budget
        {
            UserId = _userId,
            CategoryId = parentCat,
            Amount = 5000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 0m
        };
        var budgetChild = new Budget
        {
            UserId = _userId,
            CategoryId = childCat,
            Amount = 1000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 0m
        };
        _context.Budgets.AddRange(budgetParent, budgetChild);
        await _context.SaveChangesAsync();

        // Giao dịch ở danh mục con
        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = childCat,
            CategoryParentId = parentCat,
            Type = 1,
            Amount = 200000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var bp = await _context.Budgets.FindAsync(budgetParent.Id);
        var bc = await _context.Budgets.FindAsync(budgetChild.Id);

        Assert.Equal(200000m, bp!.CurrentSpent); // Ngân sách cha tăng
        Assert.Equal(200000m, bc!.CurrentSpent); // Ngân sách con tăng
    }

    [Fact]
    public async Task P11_TransactionCreated_UtcBoundary_BelongsToVietnamMonth()
    {
        var categoryId = Guid.NewGuid();
        var budgetDec = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 12,
            Year = 2026,
            CurrentSpent = 0m
        };
        _context.Budgets.Add(budgetDec);
        await _context.SaveChangesAsync();

        // 2026-11-30T18:00Z -> 01:00 AM ngày 01/12/2026 giờ Việt Nam
        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = categoryId,
            Type = 1,
            Amount = 100000m,
            TransactionDate = new DateTime(2026, 11, 30, 18, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionCreatedAsync(txEvent);

        var bDec = await _context.Budgets.FindAsync(budgetDec.Id);
        Assert.Equal(100000m, bDec!.CurrentSpent); // Thuộc tháng 12
    }

    [Fact]
    public async Task P12_TransactionEvents_UpdatedBeforeCreated_UpsertHandles()
    {
        var categoryId = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 0m
        };
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        var txId = Guid.NewGuid();
        var updateEvent = new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            OldWalletId = Guid.NewGuid(),
            NewWalletId = Guid.NewGuid(),
            OldCategoryId = categoryId,
            NewCategoryId = categoryId,
            OldType = 1,
            NewType = 1,
            OldAmount = 300000m,
            NewAmount = 400000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        // Event Updated đến trước
        await _ledgerService.HandleTransactionUpdatedAsync(updateEvent);

        var b = await _context.Budgets.FindAsync(budget.Id);
        Assert.Equal(400000m, b!.CurrentSpent);
    }

    [Fact]
    public async Task P13_TransactionCreated_NoBudget_DoesNotThrow()
    {
        var txEvent = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Type = 1,
            Amount = 100000m,
            TransactionDate = DateTime.UtcNow
        };

        var ex = await Record.ExceptionAsync(() => _ledgerService.HandleTransactionCreatedAsync(txEvent));
        Assert.Null(ex);
    }

    [Fact]
    public async Task P14_TransactionReassigned_UpdatesBudget()
    {
        var oldCat = Guid.NewGuid();
        var newCat = Guid.NewGuid();

        var budgetOld = new Budget
        {
            UserId = _userId,
            CategoryId = oldCat,
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 400000m
        };
        var budgetNew = new Budget
        {
            UserId = _userId,
            CategoryId = newCat,
            Amount = 3000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 0m
        };
        _context.Budgets.AddRange(budgetOld, budgetNew);

        var txId = Guid.NewGuid();
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budgetOld.Id,
            TransactionId = txId,
            UserId = _userId,
            Amount = 400000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        // Giao dịch được reassign sang newCat
        var reassignEvent = new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            OldWalletId = Guid.NewGuid(),
            NewWalletId = Guid.NewGuid(),
            OldCategoryId = oldCat,
            NewCategoryId = newCat,
            OldType = 1,
            NewType = 1,
            OldAmount = 400000m,
            NewAmount = 400000m,
            TransactionDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc)
        };

        await _ledgerService.HandleTransactionUpdatedAsync(reassignEvent);

        var bo = await _context.Budgets.FindAsync(budgetOld.Id);
        var bn = await _context.Budgets.FindAsync(budgetNew.Id);

        Assert.Equal(0m, bo!.CurrentSpent);
        Assert.Equal(400000m, bn!.CurrentSpent);
    }
}

