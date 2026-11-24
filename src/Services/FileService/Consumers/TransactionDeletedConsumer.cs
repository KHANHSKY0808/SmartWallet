using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using FileService.Data;

namespace FileService.Consumers;

public class TransactionDeletedConsumer : BaseRabbitMqConsumer<TransactionDeletedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionDeletedConsumer> _logger;

    public TransactionDeletedConsumer(
        IConfiguration configuration,
        ILogger<TransactionDeletedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "file.transaction-deleted", routingKey: "transaction.deleted")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(TransactionDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(TransactionDeletedEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.ReceiptFileId.HasValue || @event.ReceiptFileId.Value == Guid.Empty)
        {
            return;
        }

        var fileId = @event.ReceiptFileId.Value;
        _logger.LogInformation("FileService: Nhận TransactionDeletedEvent cho giao dịch {TxId}, đánh dấu mồ côi cho file {FileId}",
            @event.TransactionId, fileId);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FileDbContext>();

        var meta = await context.FileMetadatas.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (meta != null)
        {
            meta.RelatedEntityType = null;
            meta.RelatedEntityId = null;
            meta.ExpiresAt = DateTime.UtcNow.AddDays(7);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Đã đánh dấu file {FileId} là mồ côi, hết hạn sau 7 ngày", fileId);
        }
    }
}
