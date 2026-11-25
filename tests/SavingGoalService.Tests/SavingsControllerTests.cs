using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Messaging;
using SavingGoalService.Controllers;
using SavingGoalService.Data;
using SavingGoalService.DTOs;
using SavingGoalService.Models;

using SavingGoalService.Services;

namespace SavingGoalService.Tests;

public class MockEventPublisher : IEventPublisher
{
    public List<IntegrationEvent> PublishedEvents { get; } = new();
    public bool ShouldThrow { get; set; } = false;

    public Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default) where TEvent : IntegrationEvent
    {
        if (ShouldThrow)
        {
            throw new InvalidOperationException("RabbitMQ connection error");
        }
        PublishedEvents.Add(@event);
        return Task.CompletedTask;
    }
}

public class MockWalletServiceClient : IWalletServiceClient
{
    public Dictionary<Guid, WalletInfo> Wallets { get; } = new();

    public Task<WalletInfo?> GetWalletAsync(Guid walletId, CancellationToken cancellationToken = default)
    {
        Wallets.TryGetValue(walletId, out var w);
        return Task.FromResult(w);
    }
}

public class SavingsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SavingGoalDbContext _context;
    private readonly MockEventPublisher _publisher;
    private readonly MockWalletServiceClient _walletServiceClient;
    private readonly SavingsController _controller;
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public SavingsControllerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SavingGoalDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new SavingGoalDbContext(options);
        _context.Database.EnsureCreated();

        _publisher = new MockEventPublisher();
        _walletServiceClient = new MockWalletServiceClient();
        _controller = new SavingsController(_context, _publisher, NullLogger<SavingsController>.Instance, _walletServiceClient);

        SetUser(_userA);
    }

    private void SetUser(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Fact]
    public async Task SG01_CreateGoal_Returns201_WithZeroProgress()
    {
        var req = new CreateSavingGoalRequest
        {
            Name = "Du lịch Đà Lạt",
            TargetAmount = 10_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(90),
            Currency = "VND"
        };

        var result = await _controller.CreateGoal(req) as ObjectResult;
        Assert.NotNull(result);
        Assert.Equal(201, result.StatusCode);

        var dto = Assert.IsType<SavingGoalDto>(result.Value);
        Assert.Equal(0m, dto.CurrentAmount);
        Assert.Equal(0m, dto.Percent);
        Assert.Equal("InProgress", dto.Status);
        Assert.Equal(1, dto.StatusCode);
    }

    [Theory]
    [InlineData("Du lịch", 0, 90, "VND")]
    [InlineData("Du lịch", -100, 90, "VND")]
    [InlineData("  ", 1000, 90, "VND")]
    [InlineData("Du lịch", 1000, 0, "VND")]
    [InlineData("Du lịch", 1000, -5, "VND")]
    [InlineData("Du lịch", 1000, 90, "XYZ")]
    public async Task SG02_Validation_RejectsInvalidPayloads(string name, decimal target, int days, string cur)
    {
        var req = new CreateSavingGoalRequest
        {
            Name = name,
            TargetAmount = target,
            TargetDate = DateTime.UtcNow.AddDays(days),
            Currency = cur
        };

        var result = await _controller.CreateGoal(req) as BadRequestObjectResult;
        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task SG03_IDOR_Returns404_ForOtherUsersGoal()
    {
        // User A creates goal
        var createRes = await _controller.CreateGoal(new CreateSavingGoalRequest
        {
            Name = "Mục tiêu của A",
            TargetAmount = 5_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30)
        }) as ObjectResult;
        var goalId = ((SavingGoalDto)createRes!.Value!).Id;

        // Switch to User B
        SetUser(_userB);

        var getRes = await _controller.GetGoalById(goalId) as NotFoundObjectResult;
        Assert.NotNull(getRes);
        Assert.Equal(404, getRes.StatusCode);

        var putRes = await _controller.UpdateGoal(goalId, new UpdateSavingGoalRequest { Name = "Hack" }) as NotFoundObjectResult;
        Assert.NotNull(putRes);
        Assert.Equal(404, putRes.StatusCode);

        var delRes = await _controller.CancelGoal(goalId) as NotFoundObjectResult;
        Assert.NotNull(delRes);
        Assert.Equal(404, delRes.StatusCode);
    }

    [Fact]
    public async Task SG04_Response_ContainsCalculatedMetrics()
    {
        var createRes = await _controller.CreateGoal(new CreateSavingGoalRequest
        {
            Name = "Mua xe",
            TargetAmount = 60_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(180)
        }) as ObjectResult;
        var dto = (SavingGoalDto)createRes!.Value!;

        Assert.True(dto.DaysLeft > 0);
        Assert.True(dto.MonthlyRequired > 0);
    }

    [Fact]
    public async Task SG05_CancelGoal_WithFunds_Returns409()
    {
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Có tiền",
            TargetAmount = 10_000_000m,
            CurrentAmount = 1_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        var result = await _controller.CancelGoal(goal.Id) as ConflictObjectResult;
        Assert.NotNull(result);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task SG06_UpdateTargetAmount_BelowCurrent_CompletesGoal()
    {
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Gần đạt",
            TargetAmount = 10_000_000m,
            CurrentAmount = 5_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        var result = await _controller.UpdateGoal(goal.Id, new UpdateSavingGoalRequest
        {
            TargetAmount = 4_000_000m
        }) as OkObjectResult;

        Assert.NotNull(result);
        var dto = Assert.IsType<SavingGoalDto>(result.Value);
        Assert.Equal("Completed", dto.Status);
        Assert.Equal(2, dto.StatusCode);
        Assert.Single(_publisher.PublishedEvents);
    }

    [Fact]
    public async Task SG07_ListGoals_FilterByStatus()
    {
        await _context.SavingGoals.AddRangeAsync(
            new SavingGoal { UserId = _userA, Name = "G1", TargetAmount = 100, TargetDate = DateTime.UtcNow.AddDays(10), Status = 1 },
            new SavingGoal { UserId = _userA, Name = "G2", TargetAmount = 100, TargetDate = DateTime.UtcNow.AddDays(10), Status = 2 },
            new SavingGoal { UserId = _userA, Name = "G3", TargetAmount = 100, TargetDate = DateTime.UtcNow.AddDays(10), Status = 3 }
        );
        await _context.SaveChangesAsync();

        var inProgress = await _controller.GetGoals("InProgress") as OkObjectResult;
        var items1 = (System.Collections.Generic.List<SavingGoalDto>)inProgress!.Value!;
        Assert.Single(items1);

        var completed = await _controller.GetGoals("Completed") as OkObjectResult;
        var items2 = (System.Collections.Generic.List<SavingGoalDto>)completed!.Value!;
        Assert.Single(items2);
    }

    [Fact]
    public async Task GT06_Withdraw_DebitsGoalSynchronously()
    {
        var walletId = Guid.NewGuid();
        _walletServiceClient.Wallets[walletId] = new WalletInfo
        {
            Id = walletId,
            UserId = _userA,
            Currency = "VND",
            IsArchived = false
        };

        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Rút tiền",
            TargetAmount = 10_000_000m,
            CurrentAmount = 3_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        var key = Guid.NewGuid().ToString();
        var withdrawRes = await _controller.Withdraw(goal.Id, key, new WithdrawRequest
        {
            WalletId = walletId,
            Amount = 1_000_000m
        }) as OkObjectResult;

        Assert.NotNull(withdrawRes);
        var dto = Assert.IsType<SavingGoalDto>(withdrawRes.Value);
        Assert.Equal(2_000_000m, dto.CurrentAmount);
        Assert.Single(_publisher.PublishedEvents);
    }

    [Fact]
    public async Task Withdraw_PublisherFailure_RollbacksAndReturns503_GT13()
    {
        var walletId = Guid.NewGuid();
        _walletServiceClient.Wallets[walletId] = new WalletInfo
        {
            Id = walletId,
            UserId = _userA,
            Currency = "VND",
            IsArchived = false
        };

        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Mục tiêu rút lỗi",
            TargetAmount = 10_000_000m,
            CurrentAmount = 3_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        _publisher.ShouldThrow = true;

        var key = Guid.NewGuid().ToString();
        var result = await _controller.Withdraw(goal.Id, key, new WithdrawRequest
        {
            WalletId = walletId,
            Amount = 1_000_000m
        }) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(503, result.StatusCode);

        // Verify goal amount is NOT changed (rollback)
        var refreshedGoal = await _context.SavingGoals.AsNoTracking().FirstAsync(g => g.Id == goal.Id);
        Assert.Equal(3_000_000m, refreshedGoal.CurrentAmount);
    }

    [Fact]
    public async Task Withdraw_DecreasesCurrentAmount_Consistent()
    {
        var walletId = Guid.NewGuid();
        _walletServiceClient.Wallets[walletId] = new WalletInfo
        {
            Id = walletId,
            UserId = _userA,
            Currency = "VND",
            IsArchived = false
        };

        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Bảo toàn tiền",
            TargetAmount = 10_000_000m,
            CurrentAmount = 2_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        var initialAmount = goal.CurrentAmount;
        var withdrawAmount = 500_000m;

        var key = Guid.NewGuid().ToString();
        var res = await _controller.Withdraw(goal.Id, key, new WithdrawRequest
        {
            WalletId = walletId,
            Amount = withdrawAmount
        }) as OkObjectResult;

        Assert.NotNull(res);
        var dto = Assert.IsType<SavingGoalDto>(res.Value);
        Assert.Equal(initialAmount - withdrawAmount, dto.CurrentAmount);
    }

    [Fact]
    public async Task CalculateMonthlyRequired_SG04()
    {
        // Target: 10,000,000, Current: 4,000,000 => Remaining: 6,000,000
        // TargetDate in 90 days (~3 months) => MonthlyRequired ~ 2,000,000
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Mục tiêu mua xe",
            TargetAmount = 10_000_000m,
            CurrentAmount = 4_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(90),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        var res = await _controller.GetGoalById(goal.Id) as OkObjectResult;
        Assert.NotNull(res);
        var dto = Assert.IsType<SavingGoalDto>(res.Value);

        Assert.True(dto.MonthlyRequired > 0);
        Assert.Equal(6_000_000m / 3m, dto.MonthlyRequired);
    }

    [Fact]
    public async Task Withdraw_WalletValidation_N23_N24_N25()
    {
        var otherUserWalletId = Guid.NewGuid();
        _walletServiceClient.Wallets[otherUserWalletId] = new WalletInfo
        {
            Id = otherUserWalletId,
            UserId = _userB, // belongs to user B!
            Currency = "VND",
            IsArchived = false
        };

        var usdWalletId = Guid.NewGuid();
        _walletServiceClient.Wallets[usdWalletId] = new WalletInfo
        {
            Id = usdWalletId,
            UserId = _userA,
            Currency = "USD", // Mismatched currency
            IsArchived = false
        };

        var archivedWalletId = Guid.NewGuid();
        _walletServiceClient.Wallets[archivedWalletId] = new WalletInfo
        {
            Id = archivedWalletId,
            UserId = _userA,
            Currency = "VND",
            IsArchived = true
        };

        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Goal Test",
            TargetAmount = 10_000_000m,
            CurrentAmount = 3_000_000m,
            Currency = "VND",
            TargetDate = DateTime.UtcNow.AddDays(30),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        // N23: Other user's wallet -> 404
        var resN23 = await _controller.Withdraw(goal.Id, Guid.NewGuid().ToString(), new WithdrawRequest
        {
            WalletId = otherUserWalletId,
            Amount = 100_000m
        }) as ObjectResult;
        Assert.NotNull(resN23);
        Assert.Equal(404, resN23.StatusCode);

        // N24: Currency mismatch -> 400
        var resN24 = await _controller.Withdraw(goal.Id, Guid.NewGuid().ToString(), new WithdrawRequest
        {
            WalletId = usdWalletId,
            Amount = 100_000m
        }) as BadRequestObjectResult;
        Assert.NotNull(resN24);
        Assert.Equal(400, resN24.StatusCode);

        // N25: Missing wallet -> 404
        var resN25 = await _controller.Withdraw(goal.Id, Guid.NewGuid().ToString(), new WithdrawRequest
        {
            WalletId = Guid.NewGuid(),
            Amount = 100_000m
        }) as ObjectResult;
        Assert.NotNull(resN25);
        Assert.Equal(404, resN25.StatusCode);

        // Archived wallet -> 400
        var resArchived = await _controller.Withdraw(goal.Id, Guid.NewGuid().ToString(), new WithdrawRequest
        {
            WalletId = archivedWalletId,
            Amount = 100_000m
        }) as BadRequestObjectResult;
        Assert.NotNull(resArchived);
        Assert.Equal(400, resArchived.StatusCode);
    }

    [Fact]
    public async Task Idempotency_ScopedByUserId_N27()
    {
        var walletA = Guid.NewGuid();
        _walletServiceClient.Wallets[walletA] = new WalletInfo { Id = walletA, UserId = _userA, Currency = "VND" };

        var walletB = Guid.NewGuid();
        _walletServiceClient.Wallets[walletB] = new WalletInfo { Id = walletB, UserId = _userB, Currency = "VND" };

        var goalA = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Goal A",
            TargetAmount = 5_000_000m,
            CurrentAmount = 2_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30)
        };
        var goalB = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userB,
            Name = "Goal B",
            TargetAmount = 5_000_000m,
            CurrentAmount = 2_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30)
        };
        await _context.SavingGoals.AddRangeAsync(goalA, goalB);
        await _context.SaveChangesAsync();

        var sharedKey = "shared-key-123";

        // User A withdraws
        SetUser(_userA);
        var resA = await _controller.Withdraw(goalA.Id, sharedKey, new WithdrawRequest
        {
            WalletId = walletA,
            Amount = 200_000m
        }) as OkObjectResult;
        Assert.NotNull(resA);
        Assert.Equal(200, resA.StatusCode);

        // User B withdraws with same key
        SetUser(_userB);
        var resB = await _controller.Withdraw(goalB.Id, sharedKey, new WithdrawRequest
        {
            WalletId = walletB,
            Amount = 300_000m
        }) as OkObjectResult;
        Assert.NotNull(resB);
        Assert.Equal(200, resB.StatusCode);
        var dtoB = Assert.IsType<SavingGoalDto>(resB.Value);
        Assert.Equal(1_700_000m, dtoB.CurrentAmount); // Goal B executed, not cached from A!

        // User A replays same key with different payload -> 422
        SetUser(_userA);
        var resReplay = await _controller.Withdraw(goalA.Id, sharedKey, new WithdrawRequest
        {
            WalletId = walletA,
            Amount = 500_000m // different amount
        }) as ObjectResult;
        Assert.NotNull(resReplay);
        Assert.Equal(422, resReplay.StatusCode);
    }

    [Fact]
    public async Task GT08_WithdrawMoreThanSaved_Returns400()
    {
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Rút quá",
            TargetAmount = 10_000_000m,
            CurrentAmount = 500_000m,
            TargetDate = DateTime.UtcNow.AddDays(30),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        var result = await _controller.Withdraw(goal.Id, Guid.NewGuid().ToString(), new WithdrawRequest
        {
            WalletId = Guid.NewGuid(),
            Amount = 600_000m
        }) as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
