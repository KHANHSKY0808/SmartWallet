using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using ReportService.Data;
using ReportService.Models;
using ReportService.Services;

namespace ReportService.Consumers;

public class TransactionCreatedConsumer : BaseRabbitMqConsumer<TransactionCreatedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionCreatedConsumer> _logger;

    public TransactionCreatedConsumer(
        IConfiguration configuration,
        ILogger<TransactionCreatedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "report.transaction-created", routingKey: "transaction.created")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(TransactionCreatedEvent @event, CancellationToken cancellationToken = default)
        => ProcessEventAsync(@event, cancellationToken);

    protected override async Task ProcessEventAsync(TransactionCreatedEvent @event, CancellationToken cancellationToken)
    {
        // Đảm bảo BudgetService hoàn tất cập nhật sổ cái trước khi Dashboard phản ánh giao dịch (DS-07)
        await Task.Delay(50, cancellationToken);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var cache = scope.ServiceProvider.GetService<IReportCacheService>();
        var catLookup = scope.ServiceProvider.GetService<ICategoryLookupClient>();

        var existing = await db.TransactionProjections
            .FirstOrDefaultAsync(t => t.TransactionId == @event.TransactionId, cancellationToken);

        if (existing != null)
        {
            if (existing.IsDeleted || existing.LastEventAt >= @event.OccurredAt)
            {
                return; // RP-04, RP-05
            }
        }

        Guid? catId = @event.CategoryId == Guid.Empty ? null : @event.CategoryId;
        await EnsureCategoryCachedAsync(db, catLookup, catId, @event.CategoryParentId, @event.CategoryName, @event.UserId, cancellationToken);

        if (existing == null)
        {
            db.TransactionProjections.Add(new TransactionProjection
            {
                TransactionId = @event.TransactionId,
                UserId = @event.UserId,
                WalletId = @event.WalletId,
                CategoryId = catId,
                CategoryParentId = @event.CategoryParentId,
                CategoryName = @event.CategoryName,
                Type = @event.Type,
                Amount = @event.Amount,
                Currency = string.IsNullOrWhiteSpace(@event.Currency) ? "VND" : @event.Currency,
                TransactionDate = @event.TransactionDate,
                Note = @event.Note,
                IsDeleted = false,
                LastEventAt = @event.OccurredAt,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.WalletId = @event.WalletId;
            existing.CategoryId = catId;
            existing.CategoryParentId = @event.CategoryParentId;
            existing.CategoryName = @event.CategoryName;
            existing.Type = @event.Type;
            existing.Amount = @event.Amount;
            existing.Currency = string.IsNullOrWhiteSpace(@event.Currency) ? "VND" : @event.Currency;
            existing.TransactionDate = @event.TransactionDate;
            existing.Note = @event.Note;
            existing.LastEventAt = @event.OccurredAt;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (cache != null) await cache.InvalidateUserCacheAsync(@event.UserId, cancellationToken);
    }

    internal static async Task EnsureCategoryCachedAsync(
        ReportDbContext db,
        ICategoryLookupClient? catLookup,
        Guid? catId,
        Guid? parentId,
        string? catName,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (catId.HasValue && !string.IsNullOrWhiteSpace(catName))
        {
            var cached = await db.ReportCategories.FirstOrDefaultAsync(c => c.CategoryId == catId.Value, cancellationToken);
            if (cached == null)
            {
                db.ReportCategories.Add(new ReportCategoryCache
                {
                    CategoryId = catId.Value,
                    UserId = userId,
                    Name = catName,
                    ParentId = parentId,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        if (parentId.HasValue && catLookup != null)
        {
            var parentCached = await db.ReportCategories.AnyAsync(c => c.CategoryId == parentId.Value, cancellationToken);
            if (!parentCached)
            {
                var pRes = await catLookup.GetCategoryAsync(parentId.Value, cancellationToken);
                if (pRes.Exists && !string.IsNullOrWhiteSpace(pRes.Name))
                {
                    db.ReportCategories.Add(new ReportCategoryCache
                    {
                        CategoryId = parentId.Value,
                        UserId = userId,
                        Name = pRes.Name,
                        ParentId = pRes.ParentId,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }
        }
    }
}

public class TransactionUpdatedConsumer : BaseRabbitMqConsumer<TransactionUpdatedEvent>
{
    private readonly IServiceProvider _serviceProvider;

    public TransactionUpdatedConsumer(
        IConfiguration configuration,
        ILogger<TransactionUpdatedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "report.transaction-updated", routingKey: "transaction.updated")
    {
        _serviceProvider = serviceProvider;
    }

    public Task HandleEventAsync(TransactionUpdatedEvent @event, CancellationToken cancellationToken = default)
        => ProcessEventAsync(@event, cancellationToken);

    protected override async Task ProcessEventAsync(TransactionUpdatedEvent @event, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var cache = scope.ServiceProvider.GetService<IReportCacheService>();
        var catLookup = scope.ServiceProvider.GetService<ICategoryLookupClient>();

        var existing = await db.TransactionProjections
            .FirstOrDefaultAsync(t => t.TransactionId == @event.TransactionId, cancellationToken);

        // RP-04, RP-05: Bỏ qua event cũ hoặc đã bị xóa
        if (existing != null)
        {
            if (existing.IsDeleted || existing.LastEventAt >= @event.OccurredAt)
            {
                return;
            }
        }

        Guid? newCatId = @event.NewCategoryId == Guid.Empty ? null : @event.NewCategoryId;
        await TransactionCreatedConsumer.EnsureCategoryCachedAsync(db, catLookup, newCatId, @event.NewCategoryParentId, @event.CategoryName, @event.UserId, cancellationToken);

        if (existing == null)
        {
            db.TransactionProjections.Add(new TransactionProjection
            {
                TransactionId = @event.TransactionId,
                UserId = @event.UserId,
                WalletId = @event.NewWalletId,
                CategoryId = newCatId,
                CategoryParentId = @event.NewCategoryParentId,
                CategoryName = @event.CategoryName,
                Type = @event.NewType,
                Amount = @event.NewAmount,
                Currency = string.IsNullOrWhiteSpace(@event.Currency) ? "VND" : @event.Currency,
                TransactionDate = @event.TransactionDate,
                IsDeleted = false,
                LastEventAt = @event.OccurredAt,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.WalletId = @event.NewWalletId;
            existing.CategoryId = newCatId;
            existing.CategoryParentId = @event.NewCategoryParentId;
            existing.CategoryName = @event.CategoryName ?? existing.CategoryName;
            existing.Type = @event.NewType;
            existing.Amount = @event.NewAmount;
            existing.Currency = string.IsNullOrWhiteSpace(@event.Currency) ? existing.Currency : @event.Currency;
            existing.TransactionDate = @event.TransactionDate;
            existing.LastEventAt = @event.OccurredAt;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (cache != null) await cache.InvalidateUserCacheAsync(@event.UserId, cancellationToken);
    }
}

public class TransactionDeletedConsumer : BaseRabbitMqConsumer<TransactionDeletedEvent>
{
    private readonly IServiceProvider _serviceProvider;

    public TransactionDeletedConsumer(
        IConfiguration configuration,
        ILogger<TransactionDeletedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "report.transaction-deleted", routingKey: "transaction.deleted")
    {
        _serviceProvider = serviceProvider;
    }

    public Task HandleEventAsync(TransactionDeletedEvent @event, CancellationToken cancellationToken = default)
        => ProcessEventAsync(@event, cancellationToken);

    protected override async Task ProcessEventAsync(TransactionDeletedEvent @event, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var cache = scope.ServiceProvider.GetService<IReportCacheService>();

        var existing = await db.TransactionProjections
            .FirstOrDefaultAsync(t => t.TransactionId == @event.TransactionId, cancellationToken);

        if (existing != null)
        {
            if (existing.LastEventAt >= @event.OccurredAt && existing.IsDeleted)
            {
                return;
            }
            existing.IsDeleted = true;
            existing.LastEventAt = @event.OccurredAt;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Tombstone (RP-05): nếu Deleted đến trước Created/Updated thì tạo dòng đã xóa mềm
            db.TransactionProjections.Add(new TransactionProjection
            {
                TransactionId = @event.TransactionId,
                UserId = @event.UserId,
                WalletId = @event.WalletId,
                CategoryId = @event.CategoryId == Guid.Empty ? null : @event.CategoryId,
                CategoryParentId = @event.CategoryParentId,
                CategoryName = @event.CategoryName,
                Type = @event.Type,
                Amount = @event.Amount,
                Currency = string.IsNullOrWhiteSpace(@event.Currency) ? "VND" : @event.Currency,
                TransactionDate = DateTime.UtcNow,
                IsDeleted = true,
                LastEventAt = @event.OccurredAt,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        if (cache != null) await cache.InvalidateUserCacheAsync(@event.UserId, cancellationToken);
    }
}

public class CategoryUpdatedConsumer : BaseRabbitMqConsumer<CategoryUpdatedEvent>
{
    private readonly IServiceProvider _serviceProvider;

    public CategoryUpdatedConsumer(
        IConfiguration configuration,
        ILogger<CategoryUpdatedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "report.category-updated", routingKey: "category.updated")
    {
        _serviceProvider = serviceProvider;
    }

    public Task HandleEventAsync(CategoryUpdatedEvent @event, CancellationToken cancellationToken = default)
        => ProcessEventAsync(@event, cancellationToken);

    protected override async Task ProcessEventAsync(CategoryUpdatedEvent @event, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var cache = scope.ServiceProvider.GetService<IReportCacheService>();

        var cached = await db.ReportCategories.FirstOrDefaultAsync(c => c.CategoryId == @event.CategoryId, cancellationToken);
        if (cached == null)
        {
            db.ReportCategories.Add(new ReportCategoryCache
            {
                CategoryId = @event.CategoryId,
                UserId = @event.UserId,
                Name = @event.Name,
                ParentId = @event.ParentId,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            cached.Name = @event.Name;
            cached.ParentId = @event.ParentId;
            cached.UpdatedAt = DateTime.UtcNow;
        }

        var txs = await db.TransactionProjections
            .Where(t => t.CategoryId == @event.CategoryId)
            .ToListAsync(cancellationToken);

        foreach (var tx in txs)
        {
            tx.CategoryName = @event.Name;
            tx.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (cache != null)
        {
            if (@event.UserId.HasValue)
            {
                await cache.InvalidateUserCacheAsync(@event.UserId.Value, cancellationToken);
            }
            else
            {
                await cache.InvalidateAllUsersCacheAsync(cancellationToken);
            }
        }
    }
}

