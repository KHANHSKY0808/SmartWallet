using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using NotificationService.Data;
using NotificationService.Models;

namespace NotificationService.Consumers;

public class ExportCompletedConsumer : BaseRabbitMqConsumer<ExportCompletedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExportCompletedConsumer> _logger;

    public ExportCompletedConsumer(
        IConfiguration configuration,
        ILogger<ExportCompletedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "notification.export-completed", routingKey: "report.export_completed")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(ExportCompletedEvent @event, CancellationToken cancellationToken = default)
        => ProcessEventAsync(@event, cancellationToken);

    protected override async Task ProcessEventAsync(ExportCompletedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("NotificationService: Nhận ExportCompletedEvent {EventId}, JobId {JobId}, Format {Format}",
            @event.Id, @event.JobId, @event.Format);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        if (@event.Id != Guid.Empty && await context.Notifications.AnyAsync(n => n.EventId == @event.Id, cancellationToken))
        {
            return;
        }

        var fmt = string.Equals(@event.Format, "pdf", StringComparison.OrdinalIgnoreCase) ? "PDF" : "Excel (.xlsx)";
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            EventId = @event.Id,
            Type = "ExportCompleted",
            Title = "Báo cáo đã sẵn sàng",
            Message = $"Báo cáo tài chính định dạng {fmt} của bạn đã được tạo thành công và sẵn sàng tải xuống.",
            IsRead = false,
            Metadata = $"{{\"jobId\":\"{@event.JobId}\",\"format\":\"{@event.Format}\"}}",
            CreatedAt = DateTime.UtcNow
        };

        await context.Notifications.AddAsync(notification, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}

