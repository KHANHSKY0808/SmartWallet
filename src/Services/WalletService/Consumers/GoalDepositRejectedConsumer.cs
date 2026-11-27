using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using WalletService.Data;
using WalletService.Models;

namespace WalletService.Consumers;

/// <summary>B5A-1 / OB-07: Hoàn tiền ví idempotent khi SavingGoal từ chối nạp tiền (mục tiêu đã hủy hoặc không tồn tại).</summary>
public class GoalDepositRejectedConsumer : BaseRabbitMqConsumer<GoalDepositRejectedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GoalDepositRejectedConsumer> _logger;

    public GoalDepositRejectedConsumer(
        IConfiguration configuration,
        ILogger<GoalDepositRejectedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "wallet.goal-deposit-rejected", routingKey: "goal.deposit_rejected")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(GoalDepositRejectedEvent @event, CancellationToken cancellationToken = default)
        => ProcessEventAsync(@event, cancellationToken);

    protected override async Task ProcessEventAsync(GoalDepositRejectedEvent @event, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var alreadyRefunded = await db.WalletBalanceHistory.AnyAsync(
            h => h.Reason == BalanceChangeReason.GoalDepositRefund && h.SourceEventId == @event.OriginalEventId,
            cancellationToken);
        if (alreadyRefunded)
        {
            _logger.LogInformation("Bỏ qua GoalDepositRejectedEvent trùng cho OriginalEventId {EventId}", @event.OriginalEventId);
            return;
        }

        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == @event.WalletId && w.UserId == @event.UserId, cancellationToken);
        if (wallet == null)
        {
            _logger.LogWarning("Không tìm thấy ví {WalletId} để hoàn tiền cho GoalDepositRejected {EventId}", @event.WalletId, @event.OriginalEventId);
            return;
        }

        wallet.Balance += @event.Amount;
        wallet.RowVersion = Guid.NewGuid().ToByteArray();

        db.WalletBalanceHistory.Add(new WalletBalanceHistory
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId,
            Reason = BalanceChangeReason.GoalDepositRefund,
            ChangeAmount = @event.Amount,
            BalanceAfter = wallet.Balance,
            EntityId = @event.GoalId,
            SourceEventId = @event.OriginalEventId,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Hoàn {Amount} vào ví {WalletId} do GoalDepositRejected (GoalId={GoalId}, OriginalEvent={EventId})",
            @event.Amount, wallet.Id, @event.GoalId, @event.OriginalEventId);
    }
}

