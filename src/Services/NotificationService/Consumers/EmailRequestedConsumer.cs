using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;
using NotificationService.Services;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;

namespace NotificationService.Consumers;

public class EmailRequestedConsumer : BaseRabbitMqConsumer<EmailRequestedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEmailDispatchSignal _signal;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailRequestedConsumer> _logger;

    public EmailRequestedConsumer(
        IConfiguration configuration,
        ILogger<EmailRequestedConsumer> logger,
        IServiceProvider serviceProvider,
        IEmailDispatchSignal signal,
        TimeProvider? timeProvider = null)
        : base(configuration, logger, queueName: "notification.email-requested", routingKey: "identity.email_requested")
    {
        _serviceProvider = serviceProvider;
        _signal = signal;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public Task HandleEventAsync(EmailRequestedEvent @event, CancellationToken cancellationToken = default)
        => ProcessEventAsync(@event, cancellationToken);

    protected override async Task ProcessEventAsync(EmailRequestedEvent @event, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(@event.To))
        {
            _logger.LogWarning("EmailRequestedEvent {EventId} thiếu địa chỉ To, bỏ qua.", @event.Id);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        if (@event.Id != Guid.Empty && await context.EmailQueue.AnyAsync(e => e.EventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua EmailRequestedEvent trùng lặp EventId {EventId}", @event.Id);
            return;
        }

        var (subject, bodyHtml) = EmailTemplateRenderer.Render(@event.Template, @event.Data);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var item = new EmailQueueItem
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id == Guid.Empty ? null : @event.Id,
            UserId = @event.UserId,
            ToEmail = @event.To.Trim(),
            Subject = subject,
            BodyHtml = bodyHtml,
            TemplateCode = @event.Template,
            Status = EmailQueueStatus.Pending,
            Attempts = 0,
            NextRetryAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.EmailQueue.Add(item);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            _signal.NotifyNewEmail();
            // D62/EM-06: Chỉ log metadata, không in body/token
            _logger.LogInformation("Đã lưu EmailQueue {EmailId} từ EventId {EventId} (Template: {Template})",
                item.Id, @event.Id, @event.Template);
        }
        catch (DbUpdateException)
        {
            _logger.LogInformation("Bỏ qua EmailRequestedEvent trùng lặp theo unique index {EventId}", @event.Id);
        }
    }
}

