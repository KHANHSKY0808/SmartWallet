using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Data;
using TransactionService.Models;
using TransactionService.Services;

namespace TransactionService.Consumers;

public class MoneyTransferredConsumer : BaseRabbitMqConsumer<MoneyTransferredEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MoneyTransferredConsumer> _logger;

    public MoneyTransferredConsumer(
        IConfiguration configuration,
        ILogger<MoneyTransferredConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "transaction.money-transferred", routingKey: "wallet.transferred")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(MoneyTransferredEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(MoneyTransferredEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing MoneyTransferredEvent for TransferId {TransferId}, UserId {UserId}, Amount {Amount}",
            @event.TransferId, @event.UserId, @event.Amount);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();

        // FIN-34b / D2: Idempotency theo TransferId / EventId
        if (@event.TransferId != Guid.Empty &&
            await context.Transactions.AnyAsync(t => t.TransferId == @event.TransferId, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua MoneyTransferredEvent đã được xử lý trước đó cho TransferId {TransferId}", @event.TransferId);
            return;
        }

        var sourceWalletId = @event.SourceWalletId != Guid.Empty ? @event.SourceWalletId : @event.FromWalletId;

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            WalletId = sourceWalletId,
            CategoryId = null,
            Type = TransactionType.Transfer,
            Amount = @event.Amount,
            TransactionDate = DateTime.UtcNow,
            Note = !string.IsNullOrWhiteSpace(@event.Note) ? @event.Note : "Chuyển tiền ví",
            TransferId = @event.TransferId != Guid.Empty ? @event.TransferId : null,
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await using var dbTx = await context.Database.BeginTransactionAsync(cancellationToken);
        context.Transactions.Add(tx);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã tạo bản ghi Transaction Type=3 cho TransferId {TransferId}", @event.TransferId);

        // Phát TransactionCreatedEvent (Type=3) để ReportService đồng bộ đầy đủ cho Export sheet GiaoDich (D60)
        var eventPublisher = scope.ServiceProvider.GetService<IEventPublisher>();
        if (eventPublisher != null)
        {
            string currency = "VND";
            var walletClient = scope.ServiceProvider.GetService<IWalletClient>();
            if (walletClient != null)
            {
                var wRes = await walletClient.ValidateWalletAsync(tx.WalletId, null, cancellationToken);
                if (wRes.Exists) currency = wRes.Currency;
            }

            await eventPublisher.PublishAsync(new TransactionCreatedEvent
            {
                TransactionId = tx.Id,
                UserId = tx.UserId,
                WalletId = tx.WalletId,
                CategoryId = Guid.Empty,
                Type = (int)tx.Type,
                Amount = tx.Amount,
                Currency = currency,
                TransactionDate = tx.TransactionDate,
                Note = tx.Note
            }, "transaction.created", cancellationToken);
        }
        await dbTx.CommitAsync(cancellationToken);
    }
}
