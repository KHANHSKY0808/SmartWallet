using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Data;
using TransactionService.Models;

namespace TransactionService.Consumers;

public class GoalFundsDepositedConsumer : BaseRabbitMqConsumer<GoalFundsDepositedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GoalFundsDepositedConsumer> _logger;

    public GoalFundsDepositedConsumer(
        IConfiguration configuration,
        ILogger<GoalFundsDepositedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "transaction.goal-deposited", routingKey: "goal.funds_deposited")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(GoalFundsDepositedEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(GoalFundsDepositedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TransactionService: Nhận GoalFundsDepositedEvent {EventId}, UserId {UserId}, Amount {Amount}",
            @event.Id, @event.UserId, @event.Amount);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();

        // Idempotency: Kiểm tra nếu đã có giao dịch gắn với EventId này
        var note = $"Nạp mục tiêu {@event.GoalName ?? "tiết kiệm"}";
        var exists = await context.Transactions.AnyAsync(t =>
            t.UserId == @event.UserId &&
            t.WalletId == @event.WalletId &&
            t.Amount == @event.Amount &&
            t.Type == TransactionType.Transfer &&
            t.Note == note &&
            t.CreatedAt >= DateTime.UtcNow.AddMinutes(-5), cancellationToken);

        if (exists)
        {
            _logger.LogInformation("Bỏ qua GoalFundsDepositedEvent {EventId} vì đã tồn tại giao dịch tương ứng", @event.Id);
            return;
        }

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            WalletId = @event.WalletId,
            CategoryId = null,
            Type = TransactionType.Transfer,
            Amount = @event.Amount,
            TransactionDate = DateTime.UtcNow,
            Note = note,
            SourceType = SourceType.Goal,
            SourceId = @event.GoalId,
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await context.Transactions.AddAsync(tx, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã ghi nhận giao dịch chuyển tiền mục tiêu Type=3 (TxId: {TxId})", tx.Id);
    }
}

public class GoalFundsWithdrawnConsumer : BaseRabbitMqConsumer<GoalFundsWithdrawnEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GoalFundsWithdrawnConsumer> _logger;

    public GoalFundsWithdrawnConsumer(
        IConfiguration configuration,
        ILogger<GoalFundsWithdrawnConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "transaction.goal-withdrawn", routingKey: "goal.funds_withdrawn")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(GoalFundsWithdrawnEvent @event, CancellationToken cancellationToken = default)
    {
        return ProcessEventAsync(@event, cancellationToken);
    }

    protected override async Task ProcessEventAsync(GoalFundsWithdrawnEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("TransactionService: Nhận GoalFundsWithdrawnEvent {EventId}, UserId {UserId}, Amount {Amount}",
            @event.Id, @event.UserId, @event.Amount);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();

        var note = $"Rút từ mục tiêu {@event.GoalName ?? "tiết kiệm"}";
        var exists = await context.Transactions.AnyAsync(t =>
            t.UserId == @event.UserId &&
            t.WalletId == @event.WalletId &&
            t.Amount == @event.Amount &&
            t.Type == TransactionType.Transfer &&
            t.Note == note &&
            t.CreatedAt >= DateTime.UtcNow.AddMinutes(-5), cancellationToken);

        if (exists)
        {
            _logger.LogInformation("Bỏ qua GoalFundsWithdrawnEvent {EventId} vì đã tồn tại giao dịch tương ứng", @event.Id);
            return;
        }

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = @event.UserId,
            WalletId = @event.WalletId,
            CategoryId = null,
            Type = TransactionType.Transfer,
            Amount = @event.Amount,
            TransactionDate = DateTime.UtcNow,
            Note = note,
            SourceType = SourceType.Goal,
            SourceId = @event.GoalId,
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await context.Transactions.AddAsync(tx, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã ghi nhận giao dịch rút tiền mục tiêu Type=3 (TxId: {TxId})", tx.Id);
    }
}
