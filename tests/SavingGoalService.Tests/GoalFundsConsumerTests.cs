using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using SavingGoalService.Consumers;
using SavingGoalService.Data;
using SavingGoalService.Models;

namespace SavingGoalService.Tests;

public class GoalFundsConsumerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SavingGoalDbContext _context;
    private readonly MockEventPublisher _publisher;
    private readonly IServiceProvider _serviceProvider;
    private readonly GoalFundsDepositedConsumer _consumer;
    private readonly Guid _userId = Guid.NewGuid();

    public GoalFundsConsumerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SavingGoalDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new SavingGoalDbContext(options);
        _context.Database.EnsureCreated();

        _publisher = new MockEventPublisher();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton<IEventPublisher>(_publisher);
        _serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder().Build();
        _consumer = new GoalFundsDepositedConsumer(config, NullLogger<GoalFundsDepositedConsumer>.Instance, _serviceProvider);
    }

    [Fact]
    public async Task GT12_Consumer_DuplicateEvent_ProcessesOnce()
    {
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Name = "Mục tiêu test nạp",
            TargetAmount = 10_000_000m,
            CurrentAmount = 1_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var depEvent = new GoalFundsDepositedEvent
        {
            Id = eventId,
            GoalId = goal.Id,
            WalletId = Guid.NewGuid(),
            UserId = _userId,
            Amount = 2_000_000m,
            GoalName = goal.Name
        };

        // First delivery
        await _consumer.HandleEventAsync(depEvent);

        // Second delivery (duplicate event GT12)
        await _consumer.HandleEventAsync(depEvent);

        var updated = await _context.SavingGoals.FindAsync(goal.Id);
        Assert.NotNull(updated);
        Assert.Equal(3_000_000m, updated.CurrentAmount); // Only added once!

        var logs = await _context.GoalLogs.Where(l => l.GoalId == goal.Id).ToListAsync();
        Assert.Single(logs);
    }

    [Fact]
    public async Task GT09_DepositReachesTarget_CompletesGoal_AndPublishesGoalCompleted()
    {
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Name = "Mục tiêu về đích",
            TargetAmount = 3_000_000m,
            CurrentAmount = 2_000_000m,
            TargetDate = DateTime.UtcNow.AddDays(30),
            Status = 1
        };
        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        var depEvent = new GoalFundsDepositedEvent
        {
            Id = Guid.NewGuid(),
            GoalId = goal.Id,
            WalletId = Guid.NewGuid(),
            UserId = _userId,
            Amount = 1_000_000m,
            GoalName = goal.Name
        };

        await _consumer.HandleEventAsync(depEvent);

        var updated = await _context.SavingGoals.FindAsync(goal.Id);
        Assert.NotNull(updated);
        Assert.Equal(3_000_000m, updated.CurrentAmount);
        Assert.Equal(2, updated.Status); // Completed!
        Assert.Single(_publisher.PublishedEvents);
        Assert.IsType<GoalCompletedEvent>(_publisher.PublishedEvents[0]);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
