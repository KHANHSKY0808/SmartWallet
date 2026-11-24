using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using NotificationService.Data;
using NotificationService.Models;

namespace NotificationService.Consumers;

public class BudgetThresholdReachedConsumer : BaseRabbitMqConsumer<BudgetThresholdReachedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BudgetThresholdReachedConsumer> _logger;

    public BudgetThresholdReachedConsumer(
        IConfiguration configuration,
        ILogger<BudgetThresholdReachedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "notification.budget-threshold", routingKey: "budget.threshold_reached")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(BudgetThresholdReachedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(BudgetThresholdReachedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("NotificationService: Nhận BudgetThresholdReachedEvent cho Budget {BudgetId}, User {UserId}, {Percent}%",
            @event.BudgetId, @event.UserId, @event.Percent);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        // D37, N-02: Idempotent theo EventId (bỏ qua nếu trùng EventId)
        if (@event.Id != Guid.Empty && await context.Notifications.AnyAsync(n => n.EventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua thông báo trùng lặp với EventId {EventId}", @event.Id);
            return;
        }

        // D37, N-01: Định dạng tiêu đề và nội dung thông báo
        var title = $"⚠️ Ngân sách {@event.CategoryName} đã sử dụng {@event.Percent}%";
        var message = $"Bạn đã chi {@event.CurrentSpent:N0} đ / {@event.BudgetLimit:N0} đ ({@event.Percent}%) cho danh mục {@event.CategoryName} trong tháng {@event.Month}/{@event.Year}.";

        var metadataObj = new
        {
            budgetId = @event.BudgetId,
            categoryId = @event.CategoryId,
            categoryName = @event.CategoryName,
            percent = @event.Percent,
            currentSpent = @event.CurrentSpent,
            budgetLimit = @event.BudgetLimit,
            month = @event.Month,
            year = @event.Year
        };

        var notification = new Notification
        {
            UserId = @event.UserId,
            Type = "BudgetAlert",
            Title = title,
            Message = message,
            IsRead = false,
            Metadata = JsonSerializer.Serialize(metadataObj),
            EventId = @event.Id == Guid.Empty ? null : @event.Id,
            CreatedAt = DateTime.UtcNow
        };

        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã lưu thông báo In-App {NotificationId} cho người dùng {UserId}", notification.Id, notification.UserId);
    }
}

