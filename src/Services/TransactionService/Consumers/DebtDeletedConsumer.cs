using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Data;
using TransactionService.Models;

namespace TransactionService.Consumers;

public class DebtDeletedConsumer : BaseRabbitMqConsumer<DebtDeletedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DebtDeletedConsumer> _logger;

    public DebtDeletedConsumer(
        IConfiguration configuration,
        ILogger<DebtDeletedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "transaction.debt-deleted", routingKey: "debt.deleted")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(DebtDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(DebtDeletedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TransactionService: Nhận DebtDeletedEvent cho Debt {DebtId}, UserId {UserId}",
            @event.DebtId, @event.UserId);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var eventPublisher = scope.ServiceProvider.GetService<IEventPublisher>();

        var txs = await context.Transactions
            .Where(t => t.SourceType == SourceType.Debt && t.SourceId == @event.DebtId && t.UserId == @event.UserId)
            .ToListAsync(cancellationToken);

        if (!txs.Any())
        {
            _logger.LogInformation("Không tìm thấy giao dịch nào cần xóa cho Debt {DebtId}", @event.DebtId);
            return;
        }

        foreach (var tx in txs)
        {
            context.Transactions.Remove(tx);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Đã xóa giao dịch {TxId} của khoản nợ {DebtId}", tx.Id, @event.DebtId);

            if (eventPublisher != null)
            {
                await eventPublisher.PublishAsync(new TransactionDeletedEvent
                {
                    TransactionId = tx.Id,
                    UserId = tx.UserId,
                    WalletId = tx.WalletId,
                    CategoryId = tx.CategoryId ?? Guid.Empty,
                    Type = (int)tx.Type,
                    Amount = tx.Amount
                }, "transaction.deleted", cancellationToken);
            }
        }
    }
}

