using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using WalletService.Data;
using WalletService.Models;

namespace WalletService.Consumers;

public class TransactionUpdatedConsumer : BaseRabbitMqConsumer<TransactionUpdatedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionUpdatedConsumer> _logger;

    public TransactionUpdatedConsumer(
        IConfiguration configuration,
        ILogger<TransactionUpdatedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "wallet.transaction-updated", routingKey: "transaction.updated")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(TransactionUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(TransactionUpdatedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing TransactionUpdatedEvent: TxId {TxId}, OldWallet {OldW}, NewWallet {NewW}",
            @event.TransactionId, @event.OldWalletId, @event.NewWalletId);

        // Bỏ qua nếu là chuyển tiền (Type = 3)
        if (@event.OldType == 3 || @event.NewType == 3)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        // Idempotency check theo SourceEventId
        if (@event.Id != Guid.Empty &&
            await context.WalletBalanceHistory.AnyAsync(h => h.SourceEventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua TransactionUpdatedEvent đã xử lý: EventId {EventId}", @event.Id);
            return;
        }

        if (@event.OldWalletId == @event.NewWalletId)
        {
            var wallet = await context.Wallets.FirstOrDefaultAsync(
                w => w.Id == @event.NewWalletId && w.UserId == @event.UserId, cancellationToken);

            if (wallet == null)
            {
                _logger.LogWarning("Không tìm thấy ví {WalletId} của user {UserId}", @event.NewWalletId, @event.UserId);
                return;
            }

            decimal oldEffect = @event.OldType == 1 ? -@event.OldAmount : (@event.OldType == 2 ? @event.OldAmount : 0);
            decimal newEffect = @event.NewType == 1 ? -@event.NewAmount : (@event.NewType == 2 ? @event.NewAmount : 0);
            decimal delta = newEffect - oldEffect;

            wallet.Balance += delta;

            context.WalletBalanceHistory.Add(new WalletBalanceHistory
            {
                WalletId = wallet.Id,
                UserId = wallet.UserId,
                Reason = BalanceChangeReason.TxUpdated,
                ChangeAmount = delta,
                BalanceAfter = wallet.Balance,
                EntityId = @event.TransactionId,
                SourceEventId = @event.Id,
                CreatedAt = DateTime.UtcNow
            });

            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Đã cập nhật số dư ví {WalletId} theo delta {Delta}: {BalanceAfter}", wallet.Id, delta, wallet.Balance);
        }
        else
        {
            // Trường hợp đổi ví: Hoàn tác tác động ở ví cũ, áp dụng tác động ở ví mới
            var oldWallet = await context.Wallets.FirstOrDefaultAsync(
                w => w.Id == @event.OldWalletId && w.UserId == @event.UserId, cancellationToken);
            var newWallet = await context.Wallets.FirstOrDefaultAsync(
                w => w.Id == @event.NewWalletId && w.UserId == @event.UserId, cancellationToken);

            if (oldWallet != null)
            {
                decimal refundAmount = @event.OldType == 1 ? @event.OldAmount : -@event.OldAmount;
                oldWallet.Balance += refundAmount;
                context.WalletBalanceHistory.Add(new WalletBalanceHistory
                {
                    WalletId = oldWallet.Id,
                    UserId = oldWallet.UserId,
                    Reason = BalanceChangeReason.TxUpdated,
                    ChangeAmount = refundAmount,
                    BalanceAfter = oldWallet.Balance,
                    EntityId = @event.TransactionId,
                    SourceEventId = @event.Id,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (newWallet != null)
            {
                decimal applyAmount = @event.NewType == 1 ? -@event.NewAmount : @event.NewAmount;
                newWallet.Balance += applyAmount;
                context.WalletBalanceHistory.Add(new WalletBalanceHistory
                {
                    WalletId = newWallet.Id,
                    UserId = newWallet.UserId,
                    Reason = BalanceChangeReason.TxUpdated,
                    ChangeAmount = applyAmount,
                    BalanceAfter = newWallet.Balance,
                    EntityId = @event.TransactionId,
                    SourceEventId = @event.Id,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Đã chuyển đổi số dư giữa 2 ví: {OldWalletId} và {NewWalletId}", @event.OldWalletId, @event.NewWalletId);
        }
    }
}
