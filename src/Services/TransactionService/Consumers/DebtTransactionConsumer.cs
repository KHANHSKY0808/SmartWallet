using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Data;
using TransactionService.Models;

namespace TransactionService.Consumers;

public class DebtTransactionConsumer : BaseRabbitMqConsumer<DebtTransactionRequestedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DebtTransactionConsumer> _logger;

    public DebtTransactionConsumer(
        IConfiguration configuration,
        ILogger<DebtTransactionConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "transaction.debt-requested", routingKey: "debt.transaction_requested")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(DebtTransactionRequestedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(DebtTransactionRequestedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TransactionService: Nhận DebtTransactionRequestedEvent cho Debt {DebtId}, Type {Type}, Amount {Amount}",
            @event.DebtId, @event.Type, @event.Amount);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var eventPublisher = scope.ServiceProvider.GetService<IEventPublisher>();

        // 1. Tạo giao dịch sổ nợ
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            WalletId = @event.WalletId,
            CategoryId = @event.CategoryId,
            Type = (TransactionType)@event.Type,
            Amount = @event.Amount,
            TransactionDate = @event.TransactionDate,
            SourceType = SourceType.Debt,
            SourceId = @event.DebtId,
            Note = @event.Note,
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Transactions.Add(tx);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã tạo giao dịch sổ nợ {TxId} cho Debt {DebtId}", tx.Id, @event.DebtId);

        // 2. Publish sự kiện TransactionCreatedEvent để cập nhật số dư ví
        if (eventPublisher != null)
        {
            await eventPublisher.PublishAsync(new TransactionCreatedEvent
            {
                TransactionId = tx.Id,
                UserId = tx.UserId,
                WalletId = tx.WalletId,
                CategoryId = tx.CategoryId ?? Guid.Empty,
                Type = (int)tx.Type,
                Amount = tx.Amount,
                TransactionDate = tx.TransactionDate,
                Note = tx.Note
            }, "transaction.created", cancellationToken);
        }
    }
}

