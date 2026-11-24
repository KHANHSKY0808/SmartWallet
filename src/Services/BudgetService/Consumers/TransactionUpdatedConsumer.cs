using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using BudgetService.Services;

namespace BudgetService.Consumers;

public class TransactionUpdatedConsumer : BaseRabbitMqConsumer<TransactionUpdatedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionUpdatedConsumer> _logger;

    public TransactionUpdatedConsumer(
        IConfiguration configuration,
        ILogger<TransactionUpdatedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "budget.transaction-updated", routingKey: "transaction.updated")
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
        _logger.LogInformation("BudgetService: Nhận TransactionUpdatedEvent {TxId}, OldAmount {OldAmount}, NewAmount {NewAmount}",
            @event.TransactionId, @event.OldAmount, @event.NewAmount);

        using var scope = _serviceProvider.CreateScope();
        var ledgerService = scope.ServiceProvider.GetRequiredService<IBudgetLedgerService>();
        await ledgerService.HandleTransactionUpdatedAsync(@event, cancellationToken);
    }
}

