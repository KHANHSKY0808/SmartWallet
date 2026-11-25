using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using BudgetService.Data;

namespace BudgetService.Consumers;

public class CategoryUpdatedConsumer : BaseRabbitMqConsumer<CategoryUpdatedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CategoryUpdatedConsumer> _logger;

    public CategoryUpdatedConsumer(
        IConfiguration configuration,
        ILogger<CategoryUpdatedConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "budget.category-updated", routingKey: "category.updated")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task HandleEventAsync(CategoryUpdatedEvent @event, CancellationToken cancellationToken = default)
        => ProcessEventAsync(@event, cancellationToken);

    protected override async Task ProcessEventAsync(CategoryUpdatedEvent @event, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();

        var budgets = await context.Budgets
            .Where(b => b.CategoryId == @event.CategoryId)
            .ToListAsync(cancellationToken);

        if (budgets.Count == 0)
        {
            return;
        }

        foreach (var b in budgets)
        {
            b.CategoryName = @event.Name;
            b.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("BudgetService: Đã cập nhật tên danh mục {CategoryId} -> {Name} cho {Count} ngân sách",
            @event.CategoryId, @event.Name, budgets.Count);
    }
}

