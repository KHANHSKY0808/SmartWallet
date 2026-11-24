using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using NotificationService.Data;
using NotificationService.Models;

namespace NotificationService.Consumers;

public class DueReminderConsumer : BaseRabbitMqConsumer<DueReminderEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DueReminderConsumer> _logger;

    public DueReminderConsumer(
        IConfiguration configuration,
        ILogger<DueReminderConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "notification.due-reminder", routingKey: "reminder.due")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(DueReminderEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(DueReminderEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("NotificationService: Nhận DueReminderEvent cho User {UserId}: {Title}",
            @event.UserId, @event.Title);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        // Idempotent theo EventId nếu có
        if (@event.Id != Guid.Empty && await context.Notifications.AnyAsync(n => n.EventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua thông báo nhắc hẹn trùng lặp với EventId {EventId}", @event.Id);
            return;
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            Type = @event.ReminderType,
            Title = @event.Title,
            Message = @event.Message,
            IsRead = false,
            EventId = @event.Id,
            CreatedAt = DateTime.UtcNow
        };

        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã lưu thông báo nhắc hẹn (NotificationId: {NotificationId})", notification.Id);
    }
}

