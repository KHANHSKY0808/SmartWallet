using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Controllers;
using TransactionService.Data;
using TransactionService.DTOs;
using TransactionService.Models;
using TransactionService.Services;

namespace TransactionService.Tests;

public class FakeWalletClient : IWalletClient
{
    public Func<Guid, bool> Validator { get; set; } = _ => true;

    public Task<bool> ValidateWalletExistsAsync(Guid walletId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Validator(walletId));
    }
}

public class FakeCategoryClient : ICategoryClient
{
    public Func<Guid, CategoryValidationResult> Validator { get; set; } = _ => new CategoryValidationResult(true, 1);

    public Task<CategoryValidationResult> ValidateCategoryAsync(Guid categoryId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Validator(categoryId));
    }
}

public class FakeEventPublisher : IEventPublisher
{
    public List<(IntegrationEvent Event, string RoutingKey)> PublishedEvents { get; } = new();

    public Task PublishAsync<T>(T @event, string routingKey, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        PublishedEvents.Add((@event, routingKey));
        return Task.CompletedTask;
    }
}

public class TransactionsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TransactionDbContext _context;
    private readonly FakeWalletClient _walletClient;
    private readonly FakeCategoryClient _categoryClient;
    private readonly FakeEventPublisher _eventPublisher;
    private readonly TransactionsController _controller;

    private readonly Guid _userIdA = Guid.NewGuid();
    private readonly Guid _userIdB = Guid.NewGuid();
    private readonly Guid _walletIdA = Guid.NewGuid();
    private readonly Guid _walletIdB = Guid.NewGuid();
    private readonly Guid _categoryExpense = Guid.NewGuid();
    private readonly Guid _categoryIncome = Guid.NewGuid();

    public TransactionsControllerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TransactionDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TransactionDbContext(options);
        _context.Database.EnsureCreated();

        _walletClient = new FakeWalletClient();
        _categoryClient = new FakeCategoryClient
        {
            Validator = id =>
            {
                if (id == _categoryExpense) return new CategoryValidationResult(true, 1);
                if (id == _categoryIncome) return new CategoryValidationResult(true, 2);
                return new CategoryValidationResult(true, 1);
            }
        };
        _eventPublisher = new FakeEventPublisher();

        _controller = new TransactionsController(
            _context,
            _walletClient,
            _categoryClient,
            NullLogger<TransactionsController>.Instance,
            _eventPublisher);

        SetCurrentUser(_userIdA);
    }

    private void SetCurrentUser(Guid userId)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString()),
            new Claim(ClaimTypes.Role, "User")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _context.Dispose();
    }

    [Fact]
    public async Task T01_CreateTransaction_ValidExpense_Returns201AndPublishesEvent()
    {
        var request = new CreateTransactionRequest
        {
            WalletId = _walletIdA,
            CategoryId = _categoryExpense,
            Type = 1,
            Amount = 50000m,
            TransactionDate = DateTime.UtcNow,
            Note = "Ăn trưa văn phòng"
        };

        var response = await _controller.CreateTransaction(request) as ObjectResult;

        Assert.NotNull(response);
        Assert.Equal(201, response.StatusCode);
        var dto = Assert.IsType<TransactionDto>(response.Value);
        Assert.Equal(50000m, dto.Amount);
        Assert.Equal(1, dto.Type);
        Assert.Equal("Expense", dto.TypeName);

        // Verify published event
        var published = Assert.Single(_eventPublisher.PublishedEvents);
        Assert.Equal("transaction.created", published.RoutingKey);
        var evt = Assert.IsType<TransactionCreatedEvent>(published.Event);
        Assert.Equal(dto.Id, evt.TransactionId);
        Assert.Equal(50000m, evt.Amount);
    }

    [Fact]
    public async Task T02_CreateTransaction_ValidIncome_Returns201AndPublishesEvent()
    {
        var request = new CreateTransactionRequest
        {
            WalletId = _walletIdA,
            CategoryId = _categoryIncome,
            Type = 2,
            Amount = 10_000_000m,
            TransactionDate = DateTime.UtcNow,
            Note = "Lương tháng 11"
        };

        var response = await _controller.CreateTransaction(request) as ObjectResult;

        Assert.NotNull(response);
        Assert.Equal(201, response.StatusCode);
        var dto = Assert.IsType<TransactionDto>(response.Value);
        Assert.Equal(10_000_000m, dto.Amount);
        Assert.Equal(2, dto.Type);

        var published = Assert.Single(_eventPublisher.PublishedEvents);
        Assert.Equal("transaction.created", published.RoutingKey);
        var evt = Assert.IsType<TransactionCreatedEvent>(published.Event);
        Assert.Equal(2, evt.Type);
    }

    [Fact]
    public async Task T03_CreateTransaction_WalletDoesNotExist_ReturnsBadRequest()
    {
        _walletClient.Validator = _ => false;

        var request = new CreateTransactionRequest
        {
            WalletId = Guid.NewGuid(),
            Type = 1,
            Amount = 20000m
        };

        var result = await _controller.CreateTransaction(request) as BadRequestObjectResult;
        Assert.NotNull(result);
    }

    [Fact]
    public async Task T04_CreateTransaction_CategoryTypeMismatch_ReturnsBadRequest()
    {
        // Category là Expense (1), nhưng Request tạo Income (2)
        var request = new CreateTransactionRequest
        {
            WalletId = _walletIdA,
            CategoryId = _categoryExpense,
            Type = 2,
            Amount = 50000m
        };

        var result = await _controller.CreateTransaction(request) as BadRequestObjectResult;
        Assert.NotNull(result);
    }

    [Fact]
    public async Task T05_GetTransactions_AdvancedFilter_ReturnsMatching()
    {
        var now = DateTime.UtcNow;
        _context.Transactions.AddRange(
            new Transaction { UserId = _userIdA, WalletId = _walletIdA, CategoryId = _categoryExpense, Type = TransactionType.Expense, Amount = 50000m, TransactionDate = now.AddDays(-2), Note = "Cà phê sáng" },
            new Transaction { UserId = _userIdA, WalletId = _walletIdA, CategoryId = _categoryExpense, Type = TransactionType.Expense, Amount = 150000m, TransactionDate = now.AddDays(-1), Note = "Cơm trưa" },
            new Transaction { UserId = _userIdA, WalletId = _walletIdA, CategoryId = _categoryIncome, Type = TransactionType.Income, Amount = 5000000m, TransactionDate = now, Note = "Thưởng dự án" },
            new Transaction { UserId = _userIdB, WalletId = _walletIdB, CategoryId = _categoryExpense, Type = TransactionType.Expense, Amount = 90000m, TransactionDate = now, Note = "User B txn" }
        );
        await _context.SaveChangesAsync();

        // Lọc theo Type = Expense, MinAmount = 100000
        var query = new TransactionQueryParameters
        {
            Type = 1,
            MinAmount = 100000m
        };

        var result = await _controller.GetTransactions(query) as OkObjectResult;
        Assert.NotNull(result);
        var paged = Assert.IsType<PagedResult<TransactionDto>>(result.Value);
        Assert.Single(paged.Items);
        Assert.Equal(150000m, paged.Items[0].Amount);
    }

    [Fact]
    public async Task T06_GetTransactions_Pagination_ReturnsCorrectPageAndPageSize()
    {
        for (int i = 1; i <= 25; i++)
        {
            _context.Transactions.Add(new Transaction
            {
                UserId = _userIdA,
                WalletId = _walletIdA,
                Type = TransactionType.Expense,
                Amount = i * 1000m,
                TransactionDate = DateTime.UtcNow.AddMinutes(-i),
                Note = $"Giao dịch #{i}"
            });
        }
        await _context.SaveChangesAsync();

        var query = new TransactionQueryParameters
        {
            Page = 2,
            PageSize = 10
        };

        var result = await _controller.GetTransactions(query) as OkObjectResult;
        Assert.NotNull(result);
        var paged = Assert.IsType<PagedResult<TransactionDto>>(result.Value);
        Assert.Equal(25, paged.TotalItems);
        Assert.Equal(3, paged.TotalPages);
        Assert.Equal(10, paged.Items.Count);
        Assert.Equal(2, paged.Page);
    }

    [Fact]
    public async Task T07_GetTransaction_ById_BelongsToOtherUser_Returns404()
    {
        var tx = new Transaction
        {
            UserId = _userIdB,
            WalletId = _walletIdB,
            Type = TransactionType.Expense,
            Amount = 10000m
        };
        _context.Transactions.Add(tx);
        await _context.SaveChangesAsync();

        var result = await _controller.GetTransaction(tx.Id);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task T08_UpdateTransaction_OptimisticLockConflict_Returns409()
    {
        var tx = new Transaction
        {
            UserId = _userIdA,
            WalletId = _walletIdA,
            Type = TransactionType.Expense,
            Amount = 50000m,
            RowVersion = Guid.NewGuid()
        };
        _context.Transactions.Add(tx);
        await _context.SaveChangesAsync();

        var updateRequest = new UpdateTransactionRequest
        {
            WalletId = _walletIdA,
            Type = 1,
            Amount = 70000m,
            RowVersion = Guid.NewGuid() // Stale RowVersion
        };

        var result = await _controller.UpdateTransaction(tx.Id, updateRequest) as ObjectResult;
        Assert.NotNull(result);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task T09_UpdateTransaction_Success_UpdatesAndPublishesEvent()
    {
        var tx = new Transaction
        {
            UserId = _userIdA,
            WalletId = _walletIdA,
            CategoryId = _categoryExpense,
            Type = TransactionType.Expense,
            Amount = 50000m,
            RowVersion = Guid.NewGuid()
        };
        _context.Transactions.Add(tx);
        await _context.SaveChangesAsync();

        var updateRequest = new UpdateTransactionRequest
        {
            WalletId = _walletIdA,
            CategoryId = _categoryExpense,
            Type = 1,
            Amount = 70000m,
            RowVersion = tx.RowVersion,
            Note = "Ăn trưa nâng cấp"
        };

        var result = await _controller.UpdateTransaction(tx.Id, updateRequest) as OkObjectResult;
        Assert.NotNull(result);
        var dto = Assert.IsType<TransactionDto>(result.Value);
        Assert.Equal(70000m, dto.Amount);

        // Verify published event
        var published = Assert.Single(_eventPublisher.PublishedEvents);
        Assert.Equal("transaction.updated", published.RoutingKey);
        var evt = Assert.IsType<TransactionUpdatedEvent>(published.Event);
        Assert.Equal(50000m, evt.OldAmount);
        Assert.Equal(70000m, evt.NewAmount);
    }

    [Fact]
    public async Task T10_DeleteTransaction_Success_RemovesAndPublishesEvent()
    {
        var tx = new Transaction
        {
            UserId = _userIdA,
            WalletId = _walletIdA,
            CategoryId = _categoryExpense,
            Type = TransactionType.Expense,
            Amount = 50000m
        };
        _context.Transactions.Add(tx);
        await _context.SaveChangesAsync();

        var result = await _controller.DeleteTransaction(tx.Id) as OkObjectResult;
        Assert.NotNull(result);

        Assert.False(_context.Transactions.Any(t => t.Id == tx.Id));

        var published = Assert.Single(_eventPublisher.PublishedEvents);
        Assert.Equal("transaction.deleted", published.RoutingKey);
        var evt = Assert.IsType<TransactionDeletedEvent>(published.Event);
        Assert.Equal(tx.Id, evt.TransactionId);
        Assert.Equal(50000m, evt.Amount);
    }

    [Fact]
    public async Task T11_DeleteTransaction_TransferType_ReturnsBadRequest()
    {
        var tx = new Transaction
        {
            UserId = _userIdA,
            WalletId = _walletIdA,
            Type = TransactionType.Transfer,
            Amount = 50000m
        };
        _context.Transactions.Add(tx);
        await _context.SaveChangesAsync();

        var result = await _controller.DeleteTransaction(tx.Id) as BadRequestObjectResult;
        Assert.NotNull(result);
    }

    [Fact]
    public async Task T12_ReassignCategory_MovesTransactionsToReplacementCategory()
    {
        var oldCat = Guid.NewGuid();
        var newCat = Guid.NewGuid();

        _context.Transactions.AddRange(
            new Transaction { UserId = _userIdA, WalletId = _walletIdA, CategoryId = oldCat, Type = TransactionType.Expense, Amount = 10000m },
            new Transaction { UserId = _userIdA, WalletId = _walletIdA, CategoryId = oldCat, Type = TransactionType.Expense, Amount = 20000m },
            new Transaction { UserId = _userIdB, WalletId = _walletIdB, CategoryId = oldCat, Type = TransactionType.Expense, Amount = 30000m }
        );
        await _context.SaveChangesAsync();

        var request = new ReassignCategoryRequest
        {
            OldCategoryId = oldCat,
            ReplacementCategoryId = newCat
        };

        var result = await _controller.ReassignCategory(request) as OkObjectResult;
        Assert.NotNull(result);

        // User A's transactions moved to newCat
        var userATx = _context.Transactions.Where(t => t.UserId == _userIdA).ToList();
        Assert.All(userATx, t => Assert.Equal(newCat, t.CategoryId));

        // User B's transaction unchanged
        var userBTx = _context.Transactions.First(t => t.UserId == _userIdB);
        Assert.Equal(oldCat, userBTx.CategoryId);
    }
}

