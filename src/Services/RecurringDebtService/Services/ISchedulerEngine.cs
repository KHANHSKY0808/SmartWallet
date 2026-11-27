using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using RecurringDebtService.Data;
using RecurringDebtService.Models;

namespace RecurringDebtService.Services;

public interface ISchedulerEngine
{
    Task RunSweepAsync(DateOnly asOfDate, CancellationToken cancellationToken = default);
    DateOnly GetTodayVietnam();
    DateOnly ComputeNextOccurrence(DateOnly current, FrequencyType freq, int anchorDay, DateOnly startDate);
    DateOnly FindInitialNextExecutionDate(DateOnly startDate, FrequencyType freq, int anchorDay, DateOnly today);
}

public class SchedulerEngine : ISchedulerEngine
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventPublisher? _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SchedulerEngine> _logger;

    public SchedulerEngine(
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        ILogger<SchedulerEngine> logger,
        IEventPublisher? eventPublisher = null)
    {
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    public DateOnly GetTodayVietnam()
    {
        var vnOffset = TimeSpan.FromHours(7);
        var vnNow = _timeProvider.GetUtcNow().ToOffset(vnOffset);
        return DateOnly.FromDateTime(vnNow.DateTime);
    }

    public DateOnly ComputeNextOccurrence(DateOnly current, FrequencyType freq, int anchorDay, DateOnly startDate)
    {
        return freq switch
        {
            FrequencyType.Daily => current.AddDays(1),
            FrequencyType.Weekly => current.AddDays(7),
            FrequencyType.Monthly => ComputeNextMonthly(current, anchorDay),
            FrequencyType.Yearly => ComputeNextYearly(current, startDate),
            _ => current.AddDays(1)
        };
    }

    private static DateOnly ComputeNextMonthly(DateOnly current, int anchorDay)
    {
        // Neo theo ngày ban đầu (anchorDay): ví dụ neo 31: 31/01 -> 28/02 (hoặc 29/02) -> 31/03
        var nextMonthDate = current.AddMonths(1);
        var year = nextMonthDate.Year;
        var month = nextMonthDate.Month;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var day = Math.Min(anchorDay, daysInMonth);
        return new DateOnly(year, month, day);
    }

    private static DateOnly ComputeNextYearly(DateOnly current, DateOnly startDate)
    {
        // Năm nhuận 29/02: các năm không nhuận rơi vào 28/02, năm nhuận kế tiếp rơi vào 29/02
        var nextYear = current.Year + 1;
        var month = startDate.Month;
        var daysInMonth = DateTime.DaysInMonth(nextYear, month);
        var day = Math.Min(startDate.Day, daysInMonth);
        return new DateOnly(nextYear, month, day);
    }

    public DateOnly FindInitialNextExecutionDate(DateOnly startDate, FrequencyType freq, int anchorDay, DateOnly today)
    {
        // D55: Không sinh bù quá khứ trước lúc tạo rule. NextExecutionDate = kỳ đầu tiên >= hôm nay (giờ VN)
        if (startDate >= today)
        {
            return startDate;
        }

        var candidate = startDate;
        while (candidate < today)
        {
            candidate = ComputeNextOccurrence(candidate, freq, anchorDay, startDate);
        }
        return candidate;
    }

    public async Task RunSweepAsync(DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Bắt đầu vòng quét Scheduler cho ngày asOf: {AsOfDate} (Giờ VN)", asOfDate);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RecurringDebtDbContext>();
        var publisher = scope.ServiceProvider.GetService<IEventPublisher>() ?? _eventPublisher;
        await using var dbTx = await context.Database.BeginTransactionAsync(cancellationToken);

        // ------------------------------------------------------------
        // 1. Quét quy tắc định kỳ (RecurringRules)
        // ------------------------------------------------------------
        // D55: Rule tạm dừng thì không chạy bù các kỳ trong lúc dừng. Tiến NextExecutionDate qua asOfDate mà không sinh giao dịch.
        var inactiveRules = await context.RecurringRules
            .Where(r => !r.IsActive && r.NextExecutionDate <= asOfDate)
            .ToListAsync(cancellationToken);

        foreach (var rule in inactiveRules)
        {
            while (rule.NextExecutionDate <= asOfDate)
            {
                rule.NextExecutionDate = ComputeNextOccurrence(rule.NextExecutionDate, rule.Frequency, rule.AnchorDay, rule.StartDate);
            }
        }

        var dueRules = await context.RecurringRules
            .Where(r => r.IsActive && r.NextExecutionDate <= asOfDate)
            .ToListAsync(cancellationToken);

        foreach (var rule in dueRules)
        {
            // Vòng lặp chạy bù các kỳ bị lỡ cho tới asOfDate (D43, D55)
            while (rule.NextExecutionDate <= asOfDate)
            {
                if (rule.EndDate.HasValue && rule.NextExecutionDate > rule.EndDate.Value)
                {
                    rule.IsActive = false;
                    break;
                }

                var occurrence = rule.NextExecutionDate;

                // Publish sự kiện để TransactionService tạo giao dịch
                if (publisher != null)
                {
                    await publisher.PublishAsync(new RecurringTransactionDueEvent
                    {
                        RuleId = rule.Id,
                        UserId = rule.UserId,
                        WalletId = rule.WalletId,
                        CategoryId = rule.CategoryId,
                        Type = rule.Type,
                        Amount = rule.Amount,
                        OccurrenceDate = occurrence,
                        RuleName = rule.Name
                    }, "recurring.due", cancellationToken);
                }

                rule.LastExecutedDate = asOfDate.ToDateTime(TimeOnly.MinValue);
                rule.NextExecutionDate = ComputeNextOccurrence(occurrence, rule.Frequency, rule.AnchorDay, rule.StartDate);

                if (rule.EndDate.HasValue && rule.NextExecutionDate > rule.EndDate.Value)
                {
                    rule.IsActive = false;
                    break;
                }
            }
        }

        // ------------------------------------------------------------
        // 2. Nhắc hẹn quy tắc định kỳ (0-3 ngày tới, D57 / DR-01..03)
        // ------------------------------------------------------------
        var maxReminderDate = asOfDate.AddDays(3);
        var reminderRules = await context.RecurringRules
            .Where(r => r.IsActive && r.NextExecutionDate >= asOfDate && r.NextExecutionDate <= maxReminderDate)
            .ToListAsync(cancellationToken);

        foreach (var rule in reminderRules)
        {
            if (rule.ReminderSentOn != asOfDate)
            {
                if (publisher != null)
                {
                    await publisher.PublishAsync(new DueReminderEvent
                    {
                        UserId = rule.UserId,
                        SourceId = rule.Id,
                        Title = "📅 Giao dịch định kỳ đến hạn",
                        Message = $"📅 {rule.Name} đến hạn ngày {rule.NextExecutionDate:dd/MM}: {rule.Amount:N0}đ",
                        ReminderType = "Recurring",
                        DueDate = rule.NextExecutionDate,
                        Amount = rule.Amount
                    }, "reminder.due", cancellationToken);
                }
                rule.ReminderSentOn = asOfDate;
            }
        }

        // ------------------------------------------------------------
        // 3. Quét khoản nợ (Debts: quá hạn & nhắc hẹn, D57 / DR-04..06)
        // ------------------------------------------------------------
        // 3.1. Chuyển sang Overdue nếu DueDate < asOfDate (DR-05)
        var overdueDebts = await context.Debts
            .Where(d => d.Status == DebtStatus.Active && d.DueDate < asOfDate)
            .ToListAsync(cancellationToken);

        foreach (var debt in overdueDebts)
        {
            debt.Status = DebtStatus.Overdue;
            if (!debt.OverdueNotified)
            {
                if (publisher != null)
                {
                    await publisher.PublishAsync(new DueReminderEvent
                    {
                        UserId = debt.UserId,
                        SourceId = debt.Id,
                        Title = "⚠️ Khoản nợ quá hạn",
                        Message = $"Khoản nợ {debt.PersonName} ({debt.Amount:N0}đ) đã quá hạn ngày {debt.DueDate:dd/MM}",
                        ReminderType = "Overdue",
                        DueDate = debt.DueDate,
                        Amount = debt.Amount
                    }, "reminder.due", cancellationToken);
                }
                debt.OverdueNotified = true;
            }
        }

        // 3.2. Nhắc hẹn khoản nợ đến hạn trong 0-3 ngày tới (DR-04)
        var reminderDebts = await context.Debts
            .Where(d => d.Status == DebtStatus.Active && d.DueDate >= asOfDate && d.DueDate <= maxReminderDate)
            .ToListAsync(cancellationToken);

        foreach (var debt in reminderDebts)
        {
            if (debt.ReminderSentOn != asOfDate)
            {
                if (publisher != null)
                {
                    await publisher.PublishAsync(new DueReminderEvent
                    {
                        UserId = debt.UserId,
                        SourceId = debt.Id,
                        Title = "📅 Khoản nợ đến hạn",
                        Message = $"📅 Khoản nợ {debt.PersonName} ({debt.Amount:N0}đ) đến hạn ngày {debt.DueDate:dd/MM}",
                        ReminderType = "Debt",
                        DueDate = debt.DueDate,
                        Amount = debt.Amount
                    }, "reminder.due", cancellationToken);
                }
                debt.ReminderSentOn = asOfDate;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await dbTx.CommitAsync(cancellationToken);
        _logger.LogInformation("Hoàn thành vòng quét Scheduler cho ngày asOf: {AsOfDate}", asOfDate);
    }
}

