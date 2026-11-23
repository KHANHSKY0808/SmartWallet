using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using WalletService.Data;
using WalletService.Models;

namespace WalletService.Consumers;

public class TransactionDeletedConsumer : BaseRabbitMqConsumer<TransactionDeletedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionDeletedConsumer> _logger;

    public TransactionDeletedConsumer(
        IConfiguration configuration,
        ILogger<TransactionDeletedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "wallet.transaction-deleted", routingKey: "transaction.deleted")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(TransactionDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(TransactionDeletedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing TransactionDeletedEvent: TxId {TxId}, WalletId {WalletId}, Type {Type}, Amount {Amount}",
            @event.TransactionId, @event.WalletId, @event.Type, @event.Amount);

        // Bỏ qua nếu là chuyển tiền (Type = 3)
        if (@event.Type == 3)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        // Idempotency check theo SourceEventId
        if (@event.Id != Guid.Empty &&
            await context.WalletBalanceHistory.AnyAsync(h => h.SourceEventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua TransactionDeletedEvent đã xử lý: EventId {EventId}", @event.Id);
            return;
        }

        var wallet = await context.Wallets.FirstOrDefaultAsync(
            w => w.Id == @event.WalletId && w.UserId == @event.UserId, cancellationToken);

        if (wallet == null)
        {
            _logger.LogWarning("Không tìm thấy ví {WalletId} của user {UserId}", @event.WalletId, @event.UserId);
            return;
        }

        decimal refundAmount;
        if (@event.Type == 1) // Expense: hoàn tiền lại vào ví (+Amount)
        {
            refundAmount = @event.Amount;
            wallet.Balance += @event.Amount;
        }
        else if (@event.Type == 2) // Income: thu hồi số tiền thu nhập (-Amount)
        {
            refundAmount = -@event.Amount;
            wallet.Balance -= @event.Amount;
        }
        else
        {
            return;
        }

        context.WalletBalanceHistory.Add(new WalletBalanceHistory
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId,
            Reason = BalanceChangeReason.TxDeleted,
            ChangeAmount = refundAmount,
            BalanceAfter = wallet.Balance,
            EntityId = @event.TransactionId,
            SourceEventId = @event.Id,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã hoàn tiền vào ví {WalletId}: {BalanceAfter} ({RefundAmount})", wallet.Id, wallet.Balance, refundAmount);
    }
}
