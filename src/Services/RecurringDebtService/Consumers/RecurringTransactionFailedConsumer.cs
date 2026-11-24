using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using RecurringDebtService.Data;

namespace RecurringDebtService.Consumers;

public class RecurringTransactionFailedConsumer : BaseRabbitMqConsumer<RecurringTransactionFailedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecurringTransactionFailedConsumer> _logger;

    public RecurringTransactionFailedConsumer(
        IConfiguration configuration,
        ILogger<RecurringTransactionFailedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "recurring.failed-rule", routingKey: "recurring.failed")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(RecurringTransactionFailedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(RecurringTransactionFailedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RecurringDebtService: Nhận RecurringTransactionFailedEvent cho Rule {RuleId}: {Reason}",
            @event.RuleId, @event.Reason);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RecurringDebtDbContext>();

        var rule = await context.RecurringRules.FirstOrDefaultAsync(r => r.Id == @event.RuleId, cancellationToken);
        if (rule != null)
        {
            rule.LastError = @event.Reason;
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Đã cập nhật LastError cho Rule {RuleId}: {LastError}", rule.Id, rule.LastError);
        }
    }
}

