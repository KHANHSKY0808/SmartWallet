using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using BudgetService.Controllers;
using BudgetService.Data;
using BudgetService.DTOs;
using BudgetService.Models;
using BudgetService.Services;

namespace BudgetService.Tests;

public class FakeCategoryClient : ICategoryClient
{
    public Func<Guid, CategoryValidationResult> Validator { get; set; } = 
        id => new CategoryValidationResult(true, 1, null, "Ăn uống", new List<Guid>());

    public Task<CategoryValidationResult> ValidateCategoryAsync(Guid categoryId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Validator(categoryId));
    }
}

public class FakeTransactionClient : ITransactionClient
{
    public Func<IEnumerable<Guid>, DateTime, DateTime, BackfillResult> BackfillHandler { get; set; } = 
        (_, _, _) => new BackfillResult(true, new List<BackfillTransactionItem>());

    public Task<BackfillResult> GetExpenseTransactionsAsync(IEnumerable<Guid> categoryIds, DateTime startUtc, DateTime endUtc, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(BackfillHandler(categoryIds, startUtc, endUtc));
    }
}

public class FakeEventPublisher : IEventPublisher
{
    public List<(IntegrationEvent Event, string RoutingKey)> PublishedEvents { get; } = new();
    public bool ShouldThrow { get; set; }

    public Task PublishAsync<T>(T @event, string routingKey, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated broker failure");
        }
        PublishedEvents.Add((@event, routingKey));
        return Task.CompletedTask;
    }
}

public class BudgetsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BudgetDbContext _context;
    private readonly FakeCategoryClient _categoryClient;
    private readonly FakeTransactionClient _transactionClient;
    private readonly FakeEventPublisher _eventPublisher;
    private readonly BudgetsController _controller;
    private readonly Guid _userId = Guid.NewGuid();

    public BudgetsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BudgetDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new BudgetDbContext(options);
        _context.Database.EnsureCreated();

        _categoryClient = new FakeCategoryClient();
        _transactionClient = new FakeTransactionClient();
        _eventPublisher = new FakeEventPublisher();

        _controller = new BudgetsController(
            _context,
            _categoryClient,
            _transactionClient,
            NullLogger<BudgetsController>.Instance,
            _eventPublisher);

        SetCurrentUser(_userId);
    }

    private void SetCurrentUser(Guid userId)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task B01_CreateBudget_Normal_Returns201()
    {
        var categoryId = Guid.NewGuid();
        var request = new CreateBudgetRequest
        {
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };

        var result = await _controller.CreateBudget(request);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var dto = Assert.IsType<BudgetDto>(created.Value);
        Assert.Equal(0, dto.CurrentSpent);
        Assert.Equal(0, dto.Percent);
        Assert.Equal("normal", dto.Status);
    }

    [Theory]
    [InlineData(0, 11, 2026)]
    [InlineData(-500, 11, 2026)]
    [InlineData(2000000, 13, 2026)]
    [InlineData(2000000, 11, 1999)]
    public async Task B02_CreateBudget_InvalidInput_Returns400(decimal amount, int month, int year)
    {
        var request = new CreateBudgetRequest
        {
            CategoryId = Guid.NewGuid(),
            Amount = amount,
            Month = month,
            Year = year
        };

        var result = await _controller.CreateBudget(request);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task B03_CreateBudget_IncomeCategory_Returns400()
    {
        var categoryId = Guid.NewGuid();
        _categoryClient.Validator = _ => new CategoryValidationResult(true, 2, null, "Lương", new List<Guid>()); // Type 2 = Income

        var request = new CreateBudgetRequest
        {
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };

        var result = await _controller.CreateBudget(request);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task B04_CreateBudget_NonExistentCategory_Returns400()
    {
        var categoryId = Guid.NewGuid();
        _categoryClient.Validator = _ => new CategoryValidationResult(false, null, null, null, new List<Guid>());

        var request = new CreateBudgetRequest
        {
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };

        var result = await _controller.CreateBudget(request);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task B05_CreateBudget_Duplicate_Returns409()
    {
        var categoryId = Guid.NewGuid();
        var request = new CreateBudgetRequest
        {
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };

        var firstResult = await _controller.CreateBudget(request);
        Assert.IsType<ObjectResult>(firstResult);

        var secondResult = await _controller.CreateBudget(request);
        var conflict = Assert.IsType<ObjectResult>(secondResult);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task B06_Budget_IdorProtection_Returns404()
    {
        var otherUser = Guid.NewGuid();
        var budget = new Budget
        {
            UserId = otherUser,
            CategoryId = Guid.NewGuid(),
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        // User hiện tại (_userId) cố đọc, sửa, xóa
        var getResult = await _controller.GetBudget(budget.Id);
        Assert.IsType<NotFoundObjectResult>(getResult);

        var updateResult = await _controller.UpdateBudget(budget.Id, new UpdateBudgetRequest { Amount = 3000000m });
        Assert.IsType<NotFoundObjectResult>(updateResult);

        var deleteResult = await _controller.DeleteBudget(budget.Id);
        Assert.IsType<NotFoundObjectResult>(deleteResult);
    }

    [Fact]
    public async Task B07_GetBudgets_NoParameters_ReturnsCurrentMonthInVietnam()
    {
        var (vnMonth, vnYear) = VietnamTimeHelper.GetCurrentMonthYearInVietnam();
        var budgetCurrent = new Budget
        {
            UserId = _userId,
            CategoryId = Guid.NewGuid(),
            Amount = 2000000m,
            Month = vnMonth,
            Year = vnYear
        };
        var budgetOld = new Budget
        {
            UserId = _userId,
            CategoryId = Guid.NewGuid(),
            Amount = 1000000m,
            Month = vnMonth == 1 ? 12 : vnMonth - 1,
            Year = vnMonth == 1 ? vnYear - 1 : vnYear
        };
        _context.Budgets.AddRange(budgetCurrent, budgetOld);
        await _context.SaveChangesAsync();

        var result = await _controller.GetBudgets(new BudgetFilterParameters());
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<BudgetDto>>(ok.Value).ToList();

        Assert.Single(list);
        Assert.Equal(budgetCurrent.Id, list[0].Id);
    }

    [Fact]
    public async Task B08_GetBudgets_WithMonthYear_ReturnsFiltered()
    {
        var budgetTarget = new Budget
        {
            UserId = _userId,
            CategoryId = Guid.NewGuid(),
            Amount = 2000000m,
            Month = 10,
            Year = 2026
        };
        var budgetOther = new Budget
        {
            UserId = _userId,
            CategoryId = Guid.NewGuid(),
            Amount = 1000000m,
            Month = 11,
            Year = 2026
        };
        _context.Budgets.AddRange(budgetTarget, budgetOther);
        await _context.SaveChangesAsync();

        var result = await _controller.GetBudgets(new BudgetFilterParameters { Month = 10, Year = 2026 });
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<BudgetDto>>(ok.Value).ToList();

        Assert.Single(list);
        Assert.Equal(budgetTarget.Id, list[0].Id);
    }

    [Fact]
    public async Task B09_CreateBudget_WithExistingTransactions_CalculatesSpent()
    {
        var parentCatId = Guid.NewGuid();
        var childCatId = Guid.NewGuid();

        _categoryClient.Validator = _ => new CategoryValidationResult(
            true, 1, null, "Ăn uống", new List<Guid> { childCatId });

        _transactionClient.BackfillHandler = (_, _, _) => new BackfillResult(true, new List<BackfillTransactionItem>
        {
            new(Guid.NewGuid(), _userId, 1200000m, DateTime.UtcNow, parentCatId),
            new(Guid.NewGuid(), _userId, 300000m, DateTime.UtcNow, childCatId)
        });

        var request = new CreateBudgetRequest
        {
            CategoryId = parentCatId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };

        var result = await _controller.CreateBudget(request);
        var created = Assert.IsType<ObjectResult>(result);
        var dto = Assert.IsType<BudgetDto>(created.Value);

        Assert.Equal(1500000m, dto.CurrentSpent);
        Assert.Equal(75.0m, dto.Percent);
        Assert.Equal("normal", dto.Status);
    }

    [Fact]
    public async Task B10_CreateBudget_WithHighExistingTransactions_TriggersAlert80()
    {
        var categoryId = Guid.NewGuid();
        _transactionClient.BackfillHandler = (_, _, _) => new BackfillResult(true, new List<BackfillTransactionItem>
        {
            new(Guid.NewGuid(), _userId, 1700000m, DateTime.UtcNow, categoryId)
        });

        var request = new CreateBudgetRequest
        {
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };

        var result = await _controller.CreateBudget(request);
        var created = Assert.IsType<ObjectResult>(result);
        var dto = Assert.IsType<BudgetDto>(created.Value);

        Assert.Equal(1700000m, dto.CurrentSpent);
        Assert.Equal(85.0m, dto.Percent);
        Assert.True(dto.IsAlertSent80);
        Assert.Single(_eventPublisher.PublishedEvents);
        var alert = Assert.IsType<BudgetThresholdReachedEvent>(_eventPublisher.PublishedEvents[0].Event);
        Assert.Equal(80, alert.Percent);
    }

    [Fact]
    public async Task B11_CreateBudget_TransactionServiceUnavailable_Returns503()
    {
        _transactionClient.BackfillHandler = (_, _, _) => new BackfillResult(false, new List<BackfillTransactionItem>(), "Network error");

        var request = new CreateBudgetRequest
        {
            CategoryId = Guid.NewGuid(),
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };

        var result = await _controller.CreateBudget(request);
        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, error.StatusCode);

        // Đảm bảo không tạo bất kỳ ngân sách nào
        var count = await _context.Budgets.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task B12_CreateBudget_BackfillMultiplePages_CalculatesAll()
    {
        var categoryId = Guid.NewGuid();
        var items = Enumerable.Range(1, 150)
            .Select(i => new BackfillTransactionItem(Guid.NewGuid(), _userId, 10000m, DateTime.UtcNow, categoryId))
            .ToList();

        _transactionClient.BackfillHandler = (_, _, _) => new BackfillResult(true, items);

        var request = new CreateBudgetRequest
        {
            CategoryId = categoryId,
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };

        var result = await _controller.CreateBudget(request);
        var created = Assert.IsType<ObjectResult>(result);
        var dto = Assert.IsType<BudgetDto>(created.Value);

        Assert.Equal(1500000m, dto.CurrentSpent);
        var ledgerCount = await _context.BudgetTransactions.CountAsync(bt => bt.BudgetId == dto.Id);
        Assert.Equal(150, ledgerCount);
    }

    [Fact]
    public async Task B13_UpdateBudget_DecreaseLimitAbove80Percent_TriggersAlert()
    {
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = Guid.NewGuid(),
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 1300000m,
            IsAlertSent80 = false
        };
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        // Giảm hạn mức xuống 1.5M -> 1.3M / 1.5M = 86.7% (>= 80%)
        var result = await _controller.UpdateBudget(budget.Id, new UpdateBudgetRequest { Amount = 1500000m });
        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<BudgetDto>(ok.Value);

        Assert.True(dto.IsAlertSent80);
        Assert.Single(_eventPublisher.PublishedEvents);
        var alert = Assert.IsType<BudgetThresholdReachedEvent>(_eventPublisher.PublishedEvents[0].Event);
        Assert.Equal(80, alert.Percent);
    }

    [Fact]
    public async Task B14_UpdateBudget_IncreaseLimitBelow80Percent_UnsetsFlags()
    {
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = Guid.NewGuid(),
            Amount = 2000000m,
            Month = 11,
            Year = 2026,
            CurrentSpent = 1700000m,
            IsAlertSent80 = true,
            IsAlertSent100 = false
        };
        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();

        // Tăng hạn mức lên 5M -> 1.7M / 5M = 34% (< 80%)
        var result = await _controller.UpdateBudget(budget.Id, new UpdateBudgetRequest { Amount = 5000000m });
        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<BudgetDto>(ok.Value);

        Assert.False(dto.IsAlertSent80);
        Assert.False(dto.IsAlertSent100);
        Assert.Empty(_eventPublisher.PublishedEvents); // Không phát event
    }

    [Fact]
    public async Task B15_DeleteBudget_DeletesBudgetAndLedger_Returns200()
    {
        var budget = new Budget
        {
            UserId = _userId,
            CategoryId = Guid.NewGuid(),
            Amount = 2000000m,
            Month = 11,
            Year = 2026
        };
        _context.Budgets.Add(budget);
        _context.BudgetTransactions.Add(new BudgetTransaction
        {
            BudgetId = budget.Id,
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            Amount = 500000m,
            TransactionDate = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _controller.DeleteBudget(budget.Id);
        Assert.IsType<OkObjectResult>(result);

        var budgetInDb = await _context.Budgets.FindAsync(budget.Id);
        Assert.Null(budgetInDb);

        var ledgerInDb = await _context.BudgetTransactions.Where(bt => bt.BudgetId == budget.Id).ToListAsync();
        Assert.Empty(ledgerInDb);
    }
}

