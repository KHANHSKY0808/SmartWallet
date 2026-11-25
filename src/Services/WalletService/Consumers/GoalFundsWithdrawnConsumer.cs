using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using WalletService.Data;
using WalletService.Models;

namespace WalletService.Consumers;

public class GoalFundsWithdrawnConsumer : BaseRabbitMqConsumer<GoalFundsWithdrawnEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GoalFundsWithdrawnConsumer> _logger;

    public GoalFundsWithdrawnConsumer(
        IConfiguration configuration,
        ILogger<GoalFundsWithdrawnConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "wallet.funds-withdrawn", routingKey: "goal.funds_withdrawn")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(GoalFundsWithdrawnEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(GoalFundsWithdrawnEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("WalletService: Nhận GoalFundsWithdrawnEvent {EventId}, WalletId {WalletId}, Amount {Amount}",
            @event.Id, @event.WalletId, @event.Amount);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        // Idempotency check theo SourceEventId
        if (@event.Id != Guid.Empty &&
            await context.WalletBalanceHistory.AnyAsync(h => h.SourceEventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua GoalFundsWithdrawnEvent đã xử lý: EventId {EventId}", @event.Id);
            return;
        }

        var wallet = await context.Wallets.FirstOrDefaultAsync(w => w.Id == @event.WalletId, cancellationToken);
        if (wallet == null || wallet.UserId != @event.UserId || wallet.IsArchived)
        {
            _logger.LogError("Ví {WalletId} không hợp lệ để cộng tiền rút từ mục tiêu (Missing, wrong user, or archived)", @event.WalletId);
            throw new InvalidOperationException($"Ví {@event.WalletId} không hợp lệ để nhận tiền từ mục tiêu.");
        }

        if (!string.IsNullOrWhiteSpace(@event.Currency) &&
            !string.Equals(wallet.Currency, @event.Currency, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Ví {WalletId} có tiền tệ {WalletCurrency} không khớp với tiền tệ mục tiêu {GoalCurrency}",
                @event.WalletId, wallet.Currency, @event.Currency);
            throw new InvalidOperationException($"Loại tiền của ví ({wallet.Currency}) không khớp với mục tiêu ({@event.Currency}).");
        }

        wallet.Balance += @event.Amount;
        wallet.RowVersion = Guid.NewGuid().ToByteArray();

        var history = new WalletBalanceHistory
        {
            WalletId = wallet.Id,
            UserId = wallet.UserId,
            Reason = BalanceChangeReason.GoalWithdraw,
            ChangeAmount = @event.Amount,
            BalanceAfter = wallet.Balance,
            EntityId = @event.GoalId,
            SourceEventId = @event.Id,
            CreatedAt = DateTime.UtcNow
        };

        await context.WalletBalanceHistory.AddAsync(history, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã cộng {Amount} vào ví {WalletId}. Số dư mới: {NewBalance}", @event.Amount, wallet.Id, wallet.Balance);
    }
}
