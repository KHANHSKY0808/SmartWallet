using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using SavingGoalService.Data;
using SavingGoalService.Models;

namespace SavingGoalService.Consumers;

public class GoalFundsDepositedConsumer : BaseRabbitMqConsumer<GoalFundsDepositedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GoalFundsDepositedConsumer> _logger;

    public GoalFundsDepositedConsumer(
        IConfiguration configuration,
        ILogger<GoalFundsDepositedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "savinggoal.funds-deposited", routingKey: "goal.funds_deposited")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(GoalFundsDepositedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(GoalFundsDepositedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SavingGoalService: Nhận GoalFundsDepositedEvent {EventId}, GoalId {GoalId}, Amount {Amount}",
            @event.Id, @event.GoalId, @event.Amount);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SavingGoalDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        // Kiểm tra Idempotency
        if (await context.ProcessedEvents.AnyAsync(p => p.EventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Event GoalFundsDeposited {EventId} đã được xử lý trước đó.", @event.Id);
            return;
        }

        var goal = await context.SavingGoals.FirstOrDefaultAsync(g => g.Id == @event.GoalId, cancellationToken);
        if (goal == null || goal.Status == 3)
        {
            var reason = goal == null ? "GOAL_NOT_FOUND" : "GOAL_CANCELLED";
            _logger.LogWarning("B5A-1: Mục tiêu {GoalId} không hợp lệ ({Reason}). Ghi Outbox GoalDepositRejectedEvent để hoàn tiền ví {WalletId}.",
                @event.GoalId, reason, @event.WalletId);

            await using var rejectTx = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await context.ProcessedEvents.AddAsync(new ProcessedEvent
                {
                    EventId = @event.Id,
                    ProcessedAt = DateTime.UtcNow
                }, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);

                await publisher.PublishAsync(new GoalDepositRejectedEvent
                {
                    Id = Guid.NewGuid(),
                    GoalId = @event.GoalId,
                    WalletId = @event.WalletId,
                    UserId = @event.UserId,
                    Amount = @event.Amount,
                    OriginalEventId = @event.Id,
                    Reason = reason
                }, "goal.deposit_rejected", cancellationToken);

                await rejectTx.CommitAsync(cancellationToken);
                return;
            }
            catch
            {
                await rejectTx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        goal.CurrentAmount += @event.Amount;
        goal.UpdatedAt = DateTime.UtcNow;
        goal.RowVersion = Guid.NewGuid().ToByteArray();

        var log = new GoalLog
        {
            Id = Guid.NewGuid(),
            GoalId = goal.Id,
            UserId = @event.UserId,
            SourceWalletId = @event.WalletId,
            Amount = @event.Amount,
            Type = 1, // Deposit
            Status = 1,
            IdempotencyKey = @event.Id.ToString(),
            CreatedDate = DateTime.UtcNow
        };
        await context.GoalLogs.AddAsync(log, cancellationToken);

        bool newlyCompleted = false;
        if (goal.CurrentAmount >= goal.TargetAmount && goal.Status != 2)
        {
            goal.Status = 2; // Completed
            goal.CompletedAt = DateTime.UtcNow;
            newlyCompleted = true;
        }

        await context.ProcessedEvents.AddAsync(new ProcessedEvent
        {
            EventId = @event.Id,
            ProcessedAt = DateTime.UtcNow
        }, cancellationToken);

        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Đã cộng {Amount} vào mục tiêu {GoalId}. CurrentAmount hiện tại: {Current}", @event.Amount, goal.Id, goal.CurrentAmount);

            if (newlyCompleted)
            {
                await publisher.PublishAsync(new GoalCompletedEvent
                {
                    Id = Guid.NewGuid(),
                    GoalId = goal.Id,
                    UserId = goal.UserId,
                    GoalName = goal.Name,
                    TargetAmount = goal.TargetAmount,
                    CurrentAmount = goal.CurrentAmount
                }, "goal.completed", cancellationToken);
                _logger.LogInformation("Phát sự kiện GoalCompleted cho mục tiêu {GoalId} ({GoalName})", goal.Id, goal.Name);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi khi lưu và phát sự kiện GoalFundsDeposited cho {GoalId}", @event.GoalId);
            throw;
        }
    }
}
