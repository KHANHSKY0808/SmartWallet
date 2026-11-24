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

namespace SavingGoalService.Tests;

public class MockEventPublisher : IEventPublisher
{
    public List<IntegrationEvent> PublishedEvents { get; } = new();

    public Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default) where TEvent : IntegrationEvent
    {
        PublishedEvents.Add(@event);
        return Task.CompletedTask;
    }
}

public class SavingsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SavingGoalDbContext _context;
    private readonly MockEventPublisher _publisher;
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
        _controller = new SavingsController(_context, _publisher, NullLogger<SavingsController>.Instance);

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
            WalletId = Guid.NewGuid(),
            Amount = 1_000_000m
        }) as OkObjectResult;

        Assert.NotNull(withdrawRes);
        var dto = Assert.IsType<SavingGoalDto>(withdrawRes.Value);
        Assert.Equal(2_000_000m, dto.CurrentAmount);
        Assert.Single(_publisher.PublishedEvents);
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
