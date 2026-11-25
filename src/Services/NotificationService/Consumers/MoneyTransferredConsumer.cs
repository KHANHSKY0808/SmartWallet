using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;

namespace NotificationService.Consumers;

public class MoneyTransferredConsumer : BaseRabbitMqConsumer<MoneyTransferredEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MoneyTransferredConsumer> _logger;

    public MoneyTransferredConsumer(
        IConfiguration configuration,
        ILogger<MoneyTransferredConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "notification.money-transferred", routingKey: "wallet.transferred")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(MoneyTransferredEvent @event, CancellationToken cancellationToken = default)
        => ProcessEventAsync(@event, cancellationToken);

    protected override async Task ProcessEventAsync(MoneyTransferredEvent @event, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        if (@event.Id != Guid.Empty && await context.Notifications.AnyAsync(n => n.EventId == @event.Id, cancellationToken))
        {
            _logger.LogInformation("Bỏ qua thông báo MoneyTransferred trùng EventId {EventId}", @event.Id);
            return;
        }

        var formattedAmount = @event.Amount.ToString("N0", CultureInfo.InvariantCulture);
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            Type = "MoneyTransferred",
            Title = "Chuyển tiền giữa ví thành công",
            Message = $"Bạn đã chuyển {formattedAmount} giữa hai ví thành công.",
            IsRead = false,
            EventId = @event.Id,
            CreatedAt = DateTime.UtcNow
        };

        context.Notifications.Add(notification);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Bắt trường hợp race condition trùng EventId
        }
    }
}

