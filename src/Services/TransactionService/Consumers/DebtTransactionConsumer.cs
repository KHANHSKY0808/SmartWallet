using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Data;
using TransactionService.Models;
using TransactionService.Services;

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

        var subId = @event.PaymentId ?? @event.DebtId;

        // 0. Kiểm tra trùng lặp theo (SourceType=Debt, SourceId=DebtId, SourceSubId=PaymentId??DebtId) (N36 / D44)
        var alreadyExists = await context.Transactions.AnyAsync(t =>
            t.SourceType == SourceType.Debt &&
            t.SourceId == @event.DebtId &&
            t.SourceSubId == subId, cancellationToken);

        if (alreadyExists)
        {
            _logger.LogInformation("Bỏ qua giao dịch sổ nợ đã xử lý: Debt {DebtId}, SubId {SubId}", @event.DebtId, subId);
            return;
        }

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
            SourceSubId = subId,
            Note = @event.Note,
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Transactions.Add(tx);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogInformation("Bỏ qua giao dịch sổ nợ bị trùng đồng thời (unique index): Debt {DebtId}, SubId {SubId}", @event.DebtId, subId);
            return;
        }

        _logger.LogInformation("Đã tạo giao dịch sổ nợ {TxId} cho Debt {DebtId}", tx.Id, @event.DebtId);

        // 2. Publish sự kiện TransactionCreatedEvent để cập nhật số dư ví và read model ReportService (D58)
        if (eventPublisher != null)
        {
            var walletClient = scope.ServiceProvider.GetService<IWalletClient>();
            var categoryClient = scope.ServiceProvider.GetService<ICategoryClient>();

            string currency = "VND";
            if (walletClient != null)
            {
                var wRes = await walletClient.ValidateWalletAsync(tx.WalletId, null, cancellationToken);
                if (wRes.Exists) currency = wRes.Currency;
            }

            string? categoryName = null;
            Guid? categoryParentId = null;
            if (categoryClient != null && tx.CategoryId.HasValue)
            {
                var cRes = await categoryClient.ValidateCategoryAsync(tx.CategoryId.Value, null, cancellationToken);
                if (cRes.Exists)
                {
                    categoryName = cRes.Name;
                    categoryParentId = cRes.ParentId;
                }
            }

            await eventPublisher.PublishAsync(new TransactionCreatedEvent
            {
                TransactionId = tx.Id,
                UserId = tx.UserId,
                WalletId = tx.WalletId,
                CategoryId = tx.CategoryId ?? Guid.Empty,
                CategoryParentId = categoryParentId,
                CategoryName = categoryName,
                Type = (int)tx.Type,
                Amount = tx.Amount,
                Currency = currency,
                TransactionDate = tx.TransactionDate,
                Note = tx.Note
            }, "transaction.created", cancellationToken);
        }
    }
}

