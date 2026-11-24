using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Data;
using TransactionService.Models;
using TransactionService.Services;

namespace TransactionService.Consumers;

public class RecurringTransactionDueConsumer : BaseRabbitMqConsumer<RecurringTransactionDueEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecurringTransactionDueConsumer> _logger;

    public RecurringTransactionDueConsumer(
        IConfiguration configuration,
        ILogger<RecurringTransactionDueConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "transaction.recurring-due", routingKey: "recurring.due")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(RecurringTransactionDueEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(RecurringTransactionDueEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TransactionService: Nhận RecurringTransactionDueEvent cho Rule {RuleId}, kỳ {OccurrenceDate}",
            @event.RuleId, @event.OccurrenceDate);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var walletClient = scope.ServiceProvider.GetRequiredService<IWalletClient>();
        var categoryClient = scope.ServiceProvider.GetRequiredService<ICategoryClient>();
        var eventPublisher = scope.ServiceProvider.GetService<IEventPublisher>();

        // 1. Kiểm tra ví (D56)
        var walletResult = await walletClient.ValidateWalletAsync(@event.WalletId, null, cancellationToken);
        if (!walletResult.Exists || walletResult.IsArchived)
        {
            _logger.LogWarning("Kỳ định kỳ {RuleId} thất bại: Ví {WalletId} không tồn tại hoặc đã bị lưu trữ", @event.RuleId, @event.WalletId);
            if (eventPublisher != null)
            {
                await eventPublisher.PublishAsync(new RecurringTransactionFailedEvent
                {
                    RuleId = @event.RuleId,
                    UserId = @event.UserId,
                    OccurrenceDate = @event.OccurrenceDate,
                    Reason = "Ví đã bị lưu trữ hoặc không tồn tại",
                    RuleName = @event.RuleName
                }, "recurring.failed", cancellationToken);
            }
            return;
        }

        // 2. Kiểm tra danh mục (D56)
        var catResult = await categoryClient.ValidateCategoryAsync(@event.CategoryId, null, cancellationToken);
        if (!catResult.Exists)
        {
            _logger.LogWarning("Kỳ định kỳ {RuleId} thất bại: Danh mục {CategoryId} không tồn tại", @event.RuleId, @event.CategoryId);
            if (eventPublisher != null)
            {
                await eventPublisher.PublishAsync(new RecurringTransactionFailedEvent
                {
                    RuleId = @event.RuleId,
                    UserId = @event.UserId,
                    OccurrenceDate = @event.OccurrenceDate,
                    Reason = "Danh mục không tồn tại",
                    RuleName = @event.RuleName
                }, "recurring.failed", cancellationToken);
            }
            return;
        }

        // 3. Chống tạo trùng theo (SourceType=Recurring, SourceId=RuleId, OccurrenceDate) (SC-06, D55)
        var alreadyExists = await context.Transactions.AnyAsync(t =>
            t.SourceType == SourceType.Recurring &&
            t.SourceId == @event.RuleId &&
            t.OccurrenceDate == @event.OccurrenceDate, cancellationToken);

        if (alreadyExists)
        {
            _logger.LogInformation("Bỏ qua kỳ định kỳ đã xử lý: Rule {RuleId}, kỳ {OccurrenceDate}", @event.RuleId, @event.OccurrenceDate);
            return;
        }

        // 4. Tạo giao dịch định kỳ
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            WalletId = @event.WalletId,
            CategoryId = @event.CategoryId,
            Type = (TransactionType)@event.Type,
            Amount = @event.Amount,
            TransactionDate = new DateTime(@event.OccurrenceDate.Year, @event.OccurrenceDate.Month, @event.OccurrenceDate.Day, 0, 0, 0, DateTimeKind.Utc),
            OccurrenceDate = @event.OccurrenceDate,
            SourceType = SourceType.Recurring,
            SourceId = @event.RuleId,
            Note = @event.RuleName,
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Transactions.Add(tx);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã tạo giao dịch định kỳ {TxId} cho Rule {RuleId}, kỳ {OccurrenceDate}", tx.Id, @event.RuleId, @event.OccurrenceDate);

        // 5. Publish sự kiện TransactionCreatedEvent để cập nhật số dư ví và ngân sách
        if (eventPublisher != null)
        {
            await eventPublisher.PublishAsync(new TransactionCreatedEvent
            {
                TransactionId = tx.Id,
                UserId = tx.UserId,
                WalletId = tx.WalletId,
                CategoryId = tx.CategoryId ?? Guid.Empty,
                CategoryParentId = catResult.ParentId,
                Type = (int)tx.Type,
                Amount = tx.Amount,
                TransactionDate = tx.TransactionDate,
                Note = tx.Note
            }, "transaction.created", cancellationToken);
        }
    }
}

