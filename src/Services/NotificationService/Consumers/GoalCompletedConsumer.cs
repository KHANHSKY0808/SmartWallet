using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using NotificationService.Data;
using NotificationService.Models;

namespace NotificationService.Consumers;

public class GoalCompletedConsumer : BaseRabbitMqConsumer<GoalCompletedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GoalCompletedConsumer> _logger;

    public GoalCompletedConsumer(
        IConfiguration configuration,
        ILogger<GoalCompletedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "notification.goal-completed", routingKey: "goal.completed")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(GoalCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(GoalCompletedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("NotificationService: Nhận GoalCompletedEvent {EventId}, GoalId {GoalId}, GoalName {GoalName}",
            @event.Id, @event.GoalId, @event.GoalName);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        // Idempotency: Kiểm tra EventId
        if (@event.Id != Guid.Empty && await context.Notifications.AnyAsync(n => n.EventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua GoalCompletedEvent {EventId} vì đã xử lý", @event.Id);
            return;
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            EventId = @event.Id,
            Type = "GoalCompleted",
            Title = $"🎉 Hoàn thành mục tiêu {@event.GoalName}",
            Message = $"Chúc mừng bạn đã hoàn thành mục tiêu '{@event.GoalName}' với số tiền {@event.CurrentAmount:N0}đ!",
            IsRead = false,
            Metadata = $"{{\"goalId\":\"{@event.GoalId}\",\"targetAmount\":{@event.TargetAmount},\"currentAmount\":{@event.CurrentAmount}}}",
            CreatedAt = DateTime.UtcNow
        };

        await context.Notifications.AddAsync(notification, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã lưu thông báo hoàn thành mục tiêu {GoalId} cho user {UserId}", @event.GoalId, @event.UserId);
    }
}
