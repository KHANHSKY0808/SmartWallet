using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using BudgetService.Services;

namespace BudgetService.Consumers;

public class TransactionCreatedConsumer : BaseRabbitMqConsumer<TransactionCreatedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionCreatedConsumer> _logger;

    public TransactionCreatedConsumer(
        IConfiguration configuration,
        ILogger<TransactionCreatedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "budget.transaction-created", routingKey: "transaction.created")
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
        _logger.LogInformation("BudgetService: Nhận TransactionCreatedEvent {TxId}, Type {Type}, Amount {Amount}",
            @event.TransactionId, @event.Type, @event.Amount);

        using var scope = _serviceProvider.CreateScope();
        var ledgerService = scope.ServiceProvider.GetRequiredService<IBudgetLedgerService>();
        await ledgerService.HandleTransactionCreatedAsync(@event, cancellationToken);
    }
}

