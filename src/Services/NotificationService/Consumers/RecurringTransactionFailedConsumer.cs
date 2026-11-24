using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using NotificationService.Data;
using NotificationService.Models;

namespace NotificationService.Consumers;

public class RecurringTransactionFailedConsumer : BaseRabbitMqConsumer<RecurringTransactionFailedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecurringTransactionFailedConsumer> _logger;

    public RecurringTransactionFailedConsumer(
        IConfiguration configuration,
        ILogger<RecurringTransactionFailedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "notification.recurring-failed", routingKey: "recurring.failed")
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
        _logger.LogInformation("NotificationService: Nhận RecurringTransactionFailedEvent cho Rule {RuleId}, User {UserId}",
            @event.RuleId, @event.UserId);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        // D56: Thông báo lỗi kỳ định kỳ có chuỗi "Không tạo được"
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            Type = "RecurringFailed",
            Title = "⚠️ Không tạo được giao dịch định kỳ",
            Message = $"Không tạo được giao dịch định kỳ {@event.RuleName}: {@event.Reason}",
            IsRead = false,
            EventId = @event.Id,
            CreatedAt = DateTime.UtcNow
        };

        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã lưu thông báo lỗi định kỳ (NotificationId: {NotificationId})", notification.Id);
    }
}

