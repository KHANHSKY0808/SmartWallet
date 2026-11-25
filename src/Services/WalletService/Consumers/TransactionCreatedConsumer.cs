using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using WalletService.Data;
using WalletService.Models;

namespace WalletService.Consumers;

public class TransactionCreatedConsumer : BaseRabbitMqConsumer<TransactionCreatedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionCreatedConsumer> _logger;

    public TransactionCreatedConsumer(
        IConfiguration configuration,
        ILogger<TransactionCreatedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "wallet.transaction-created", routingKey: "transaction.created")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(TransactionCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(TransactionCreatedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing TransactionCreatedEvent: TxId {TxId}, WalletId {WalletId}, Type {Type}, Amount {Amount}",
            @event.TransactionId, @event.WalletId, @event.Type, @event.Amount);

        // FIN-36 & D2: Bỏ qua Type=3 (Transfer) vì số dư ví đã được cập nhật trong Transfer saga
        if (@event.Type == 3)
        {
            _logger.LogInformation("Bỏ qua cập nhật số dư cho giao dịch chuyển tiền Type=3");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        // Idempotency check theo SourceEventId
        if (@event.Id != Guid.Empty &&
            await context.WalletBalanceHistory.AnyAsync(h => h.SourceEventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua TransactionCreatedEvent đã xử lý: EventId {EventId}", @event.Id);
            return;
        }

        var wallet = await context.Wallets.FirstOrDefaultAsync(
            w => w.Id == @event.WalletId && w.UserId == @event.UserId, cancellationToken);

        if (wallet == null)
        {
            _logger.LogError("Không tìm thấy ví {WalletId} của user {UserId} khi xử lý TransactionCreatedEvent {TxId}",
                @event.WalletId, @event.UserId, @event.TransactionId);
            throw new InvalidOperationException($"Không tìm thấy ví {@event.WalletId} của user {@event.UserId}.");
        }

        decimal changeAmount;
        if (@event.Type == 1) // Expense: trừ tiền ví
        {
            changeAmount = -@event.Amount;
            wallet.Balance -= @event.Amount;
        }
        else if (@event.Type == 2) // Income: cộng tiền ví
        {
            changeAmount = @event.Amount;
            wallet.Balance += @event.Amount;
        }
        else
        {
            return;
        }

        context.WalletBalanceHistory.Add(new WalletBalanceHistory
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId,
            Reason = BalanceChangeReason.TxCreated,
            ChangeAmount = changeAmount,
            BalanceAfter = wallet.Balance,
            EntityId = @event.TransactionId,
            SourceEventId = @event.Id,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã cập nhật số dư ví {WalletId}: {BalanceAfter} ({ChangeAmount})", wallet.Id, wallet.Balance, changeAmount);
    }
}
