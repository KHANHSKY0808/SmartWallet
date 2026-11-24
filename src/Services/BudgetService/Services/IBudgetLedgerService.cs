using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using BudgetService.Data;
using BudgetService.Models;

namespace BudgetService.Services;

public interface IBudgetLedgerService
{
    Task HandleTransactionCreatedAsync(TransactionCreatedEvent @event, CancellationToken cancellationToken = default);
    Task HandleTransactionUpdatedAsync(TransactionUpdatedEvent @event, CancellationToken cancellationToken = default);
    Task HandleTransactionDeletedAsync(TransactionDeletedEvent @event, CancellationToken cancellationToken = default);
}

public class BudgetLedgerService : IBudgetLedgerService
{
    private readonly BudgetDbContext _context;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<BudgetLedgerService> _logger;

    public BudgetLedgerService(
        BudgetDbContext context,
        ILogger<BudgetLedgerService> logger,
        IEventPublisher? eventPublisher = null)
    {
        _context = context;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    public async Task HandleTransactionCreatedAsync(TransactionCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // D2, D33, P-02, P-03: Chỉ tính chi tiêu Type = 1 (Chi). Bỏ qua Type 2 (Thu) và Type 3 (Chuyển tiền)
        if (@event.Type != 1)
        {
            _logger.LogInformation("Bỏ qua giao dịch không phải chi tiêu (Type={Type}) đối với ngân sách", @event.Type);
            return;
        }

        // D31, P-11: Kỳ ngân sách tính theo giờ Việt Nam (UTC+7) từ TransactionDate
        var (month, year) = VietnamTimeHelper.GetMonthYearInVietnam(@event.TransactionDate);

        // D32, P-10: Tìm các ngân sách khớp danh mục hoặc danh mục cha
        var matchingBudgets = await _context.Budgets
            .Where(b => b.UserId == @event.UserId && b.Month == month && b.Year == year &&
                        (b.CategoryId == @event.CategoryId ||
                         (@event.CategoryParentId.HasValue && b.CategoryId == @event.CategoryParentId.Value)))
            .ToListAsync(cancellationToken);

        if (matchingBudgets.Count == 0)
        {
            return;
        }

        var alertsToPublish = new List<BudgetThresholdReachedEvent>();

        // D36: DB transaction + publish confirm
        using var dbTx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var budget in matchingBudgets)
            {
                // D33, P-04: Upsert sổ cái BudgetTransactions theo (BudgetId, TransactionId)
                var ledger = await _context.BudgetTransactions
                    .FirstOrDefaultAsync(bt => bt.BudgetId == budget.Id && bt.TransactionId == @event.TransactionId, cancellationToken);

                if (ledger == null)
                {
                    ledger = new BudgetTransaction
                    {
                        BudgetId = budget.Id,
                        TransactionId = @event.TransactionId,
                        UserId = @event.UserId,
                        Amount = @event.Amount,
                        TransactionDate = @event.TransactionDate,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.BudgetTransactions.Add(ledger);
                }
                else
                {
                    ledger.Amount = @event.Amount;
                    ledger.TransactionDate = @event.TransactionDate;
                    ledger.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync(cancellationToken);

                // D33: Tính lại CurrentSpent = SUM(Amount) từ sổ cái
                budget.CurrentSpent = await _context.BudgetTransactions
                    .Where(bt => bt.BudgetId == budget.Id)
                    .SumAsync(bt => bt.Amount, cancellationToken);
                budget.UpdatedAt = DateTime.UtcNow;

                // D35, A-01, A-02, A-03, A-04: Kiểm tra ngưỡng cảnh báo
                var alert = EvaluateAlert(budget);
                if (alert != null)
                {
                    alertsToPublish.Add(alert);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            // D36: Publish có publisher confirm
            if (_eventPublisher != null)
            {
                foreach (var alert in alertsToPublish)
                {
                    await _eventPublisher.PublishAsync(alert, "budget.threshold_reached", cancellationToken);
                }
            }

            await dbTx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi khi xử lý TransactionCreatedEvent cho ngân sách, rollback DB transaction");
            throw; // Ném exception để message được Nack và retry theo D15/D36
        }
    }

    public async Task HandleTransactionUpdatedAsync(TransactionUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.OldType != 1 && @event.NewType != 1)
        {
            return;
        }

        using var dbTx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var alertsToPublish = new List<BudgetThresholdReachedEvent>();
            var affectedBudgetIds = new HashSet<Guid>();

            // 1. Tìm các bản ghi sổ cái hiện có của giao dịch này
            var existingLedgers = await _context.BudgetTransactions
                .Where(bt => bt.TransactionId == @event.TransactionId)
                .ToListAsync(cancellationToken);

            foreach (var el in existingLedgers)
            {
                affectedBudgetIds.Add(el.BudgetId);
            }

            if (@event.NewType != 1)
            {
                // P-09: Đổi từ Chi sang loại khác (Thu, Chuyển tiền) -> xóa khỏi toàn bộ sổ cái
                _context.BudgetTransactions.RemoveRange(existingLedgers);
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // NewType == 1 (Chi)
                var (newMonth, newYear) = VietnamTimeHelper.GetMonthYearInVietnam(@event.TransactionDate);

                // Tìm các ngân sách khớp mới
                var matchingBudgets = await _context.Budgets
                    .Where(b => b.UserId == @event.UserId && b.Month == newMonth && b.Year == newYear &&
                                (b.CategoryId == @event.NewCategoryId ||
                                 (@event.NewCategoryParentId.HasValue && b.CategoryId == @event.NewCategoryParentId.Value)))
                    .ToListAsync(cancellationToken);

                var matchingBudgetIds = matchingBudgets.Select(b => b.Id).ToHashSet();

                // Xóa khỏi các ngân sách cũ mà giao dịch không còn thuộc về (đổi danh mục P-07, đổi tháng P-08)
                var ledgersToRemove = existingLedgers.Where(el => !matchingBudgetIds.Contains(el.BudgetId)).ToList();
                if (ledgersToRemove.Count > 0)
                {
                    _context.BudgetTransactions.RemoveRange(ledgersToRemove);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                // Upsert vào các ngân sách khớp mới
                foreach (var budget in matchingBudgets)
                {
                    affectedBudgetIds.Add(budget.Id);

                    var ledger = existingLedgers.FirstOrDefault(el => el.BudgetId == budget.Id);
                    if (ledger == null)
                    {
                        ledger = new BudgetTransaction
                        {
                            BudgetId = budget.Id,
                            TransactionId = @event.TransactionId,
                            UserId = @event.UserId,
                            Amount = @event.NewAmount,
                            TransactionDate = @event.TransactionDate,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _context.BudgetTransactions.Add(ledger);
                    }
                    else
                    {
                        ledger.Amount = @event.NewAmount;
                        ledger.TransactionDate = @event.TransactionDate;
                        ledger.UpdatedAt = DateTime.UtcNow;
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
            }

            // 2. Tính lại CurrentSpent và cập nhật cờ cảnh báo cho tất cả ngân sách bị ảnh hưởng
            if (affectedBudgetIds.Count > 0)
            {
                var affectedBudgets = await _context.Budgets
                    .Where(b => affectedBudgetIds.Contains(b.Id))
                    .ToListAsync(cancellationToken);

                foreach (var budget in affectedBudgets)
                {
                    budget.CurrentSpent = await _context.BudgetTransactions
                        .Where(bt => bt.BudgetId == budget.Id)
                        .SumAsync(bt => bt.Amount, cancellationToken);
                    budget.UpdatedAt = DateTime.UtcNow;

                    var alert = EvaluateAlert(budget);
                    if (alert != null)
                    {
                        alertsToPublish.Add(alert);
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
            }

            if (_eventPublisher != null)
            {
                foreach (var alert in alertsToPublish)
                {
                    await _eventPublisher.PublishAsync(alert, "budget.threshold_reached", cancellationToken);
                }
            }

            await dbTx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi khi xử lý TransactionUpdatedEvent cho ngân sách, rollback DB transaction");
            throw;
        }
    }

    public async Task HandleTransactionDeletedAsync(TransactionDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.Type != 1)
        {
            return;
        }

        using var dbTx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var ledgersToRemove = await _context.BudgetTransactions
                .Where(bt => bt.TransactionId == @event.TransactionId)
                .ToListAsync(cancellationToken);

            if (ledgersToRemove.Count == 0)
            {
                return;
            }

            var affectedBudgetIds = ledgersToRemove.Select(l => l.BudgetId).Distinct().ToList();
            _context.BudgetTransactions.RemoveRange(ledgersToRemove);
            await _context.SaveChangesAsync(cancellationToken);

            var affectedBudgets = await _context.Budgets
                .Where(b => affectedBudgetIds.Contains(b.Id))
                .ToListAsync(cancellationToken);

            foreach (var budget in affectedBudgets)
            {
                // D33, P-06: Tính lại CurrentSpent = SUM(Amount) từ sổ cái
                budget.CurrentSpent = await _context.BudgetTransactions
                    .Where(bt => bt.BudgetId == budget.Id)
                    .SumAsync(bt => bt.Amount, cancellationToken);
                budget.UpdatedAt = DateTime.UtcNow;

                // D35, A-05: Gỡ cờ cảnh báo tương ứng nếu spent giảm xuống dưới ngưỡng
                EvaluateAlert(budget); // Cập nhật cờ
            }

            await _context.SaveChangesAsync(cancellationToken);
            await dbTx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi khi xử lý TransactionDeletedEvent cho ngân sách, rollback DB transaction");
            throw;
        }
    }

    private static BudgetThresholdReachedEvent? EvaluateAlert(Budget budget)
    {
        var percent = budget.Amount > 0 ? (budget.CurrentSpent / budget.Amount) * 100m : 0m;

        if (percent >= 100m)
        {
            if (!budget.IsAlertSent100)
            {
                // A-04: Từ < 80% nhảy thẳng lên >= 100% thì chỉ phát 1 event 100%, nhưng bật cả 2 cờ
                budget.IsAlertSent80 = true;
                budget.IsAlertSent100 = true;
                return new BudgetThresholdReachedEvent
                {
                    BudgetId = budget.Id,
                    UserId = budget.UserId,
                    CategoryId = budget.CategoryId,
                    CategoryName = budget.CategoryName ?? "Danh mục",
                    Percent = 100,
                    CurrentSpent = budget.CurrentSpent,
                    BudgetLimit = budget.Amount,
                    Month = budget.Month,
                    Year = budget.Year
                };
            }
        }
        else if (percent >= 80m)
        {
            if (!budget.IsAlertSent80)
            {
                budget.IsAlertSent80 = true;
                return new BudgetThresholdReachedEvent
                {
                    BudgetId = budget.Id,
                    UserId = budget.UserId,
                    CategoryId = budget.CategoryId,
                    CategoryName = budget.CategoryName ?? "Danh mục",
                    Percent = 80,
                    CurrentSpent = budget.CurrentSpent,
                    BudgetLimit = budget.Amount,
                    Month = budget.Month,
                    Year = budget.Year
                };
            }

            // Nếu từ >=100% giảm xuống trong khoảng 80-99%: gỡ cờ 100, giữ cờ 80
            if (budget.IsAlertSent100)
            {
                budget.IsAlertSent100 = false;
            }
        }
        else
        {
            // D35, A-05: Xuống dưới 80%: gỡ cả 2 cờ
            budget.IsAlertSent80 = false;
            budget.IsAlertSent100 = false;
        }

        return null;
    }
}

