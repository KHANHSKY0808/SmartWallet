using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using BudgetService.Services;

namespace BudgetService.Consumers;

public class TransactionDeletedConsumer : BaseRabbitMqConsumer<TransactionDeletedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionDeletedConsumer> _logger;

    public TransactionDeletedConsumer(
        IConfiguration configuration,
        ILogger<TransactionDeletedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "budget.transaction-deleted", routingKey: "transaction.deleted")
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
        _logger.LogInformation("BudgetService: Nhận TransactionDeletedEvent {TxId}, Amount {Amount}",
            @event.TransactionId, @event.Amount);

        using var scope = _serviceProvider.CreateScope();
        var ledgerService = scope.ServiceProvider.GetRequiredService<IBudgetLedgerService>();
        await ledgerService.HandleTransactionDeletedAsync(@event, cancellationToken);
    }
}

