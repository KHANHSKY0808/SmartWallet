using Microsoft.EntityFrameworkCore;
using ReportService.Data;
using ReportService.DTOs;
using ReportService.Models;

namespace ReportService.Services;

public interface IReportQueryService
{
    Task<DashboardResponse> GetDashboardAsync(Guid userId, int month, int year, string? bearerToken, CancellationToken cancellationToken = default);
    Task<AnalysisResponse> GetAnalysisAsync(Guid userId, DateOnly from, DateOnly to, string groupBy, bool comparePrevious, CancellationToken cancellationToken = default);
}

public class ReportQueryService : IReportQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly ReportDbContext _db;
    private readonly IReportCacheService _cache;
    private readonly IExchangeRateClient _exchangeRateClient;
    private readonly IBudgetClient _budgetClient;
    private readonly ICategoryLookupClient? _categoryClient;
    private readonly ILogger<ReportQueryService> _logger;

    public ReportQueryService(
        ReportDbContext db,
        IReportCacheService cache,
        IExchangeRateClient exchangeRateClient,
        IBudgetClient budgetClient,
        ILogger<ReportQueryService> logger,
        ICategoryLookupClient? categoryClient = null)
    {
        _db = db;
        _cache = cache;
        _exchangeRateClient = exchangeRateClient;
        _budgetClient = budgetClient;
        _logger = logger;
        _categoryClient = categoryClient;
    }

    public async Task<DashboardResponse> GetDashboardAsync(
        Guid userId, int month, int year, string? bearerToken, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"dash:{month:D2}:{year:D4}";
        var aggregates = await _cache.GetAsync<DashboardAggregatesDto>(userId, cacheKey, cancellationToken);

        if (aggregates == null)
        {
            aggregates = await ComputeDashboardAggregatesAsync(userId, month, year, cancellationToken);
            await _cache.SetAsync(userId, cacheKey, aggregates, CacheTtl, cancellationToken);
        }

        // Budgets đọc trực tiếp từ BudgetService với timeout 2s (D59)
        var (budgets, budgetsUnavailable) = await _budgetClient.GetBudgetsAsync(month, year, bearerToken, cancellationToken);

        return new DashboardResponse
        {
            Month = aggregates.Month,
            Year = aggregates.Year,
            TotalIncome = aggregates.TotalIncome,
            TotalExpense = aggregates.TotalExpense,
            Net = aggregates.Net,
            ByCategory = aggregates.ByCategory,
            TopExpenses = aggregates.TopExpenses,
            Budgets = budgets,
            BudgetsUnavailable = budgetsUnavailable,
            ExcludedCurrencies = aggregates.ExcludedCurrencies
        };
    }

    private async Task<DashboardAggregatesDto> ComputeDashboardAggregatesAsync(
        Guid userId, int month, int year, CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = VietnamTimeHelper.GetMonthRangeUtc(month, year);

        var rows = await _db.TransactionProjections
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && !t.IsDeleted
                        && (t.Type == 1 || t.Type == 2)
                        && t.TransactionDate >= startUtc
                        && t.TransactionDate < endUtc)
            .Select(t => new
            {
                t.TransactionId,
                t.CategoryId,
                t.CategoryParentId,
                t.CategoryName,
                t.Type,
                t.Amount,
                t.Currency,
                t.TransactionDate,
                t.Note
            })
            .ToListAsync(cancellationToken);

        var rateMap = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase) { ["VND"] = 1m };
        var excludedCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Nạp cache danh mục 1 lần
        var catNames = await _db.ReportCategories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .ToDictionaryAsync(c => c.CategoryId, c => c.Name, cancellationToken);

        decimal totalIncome = 0m;
        decimal totalExpense = 0m;
        var categorySums = new Dictionary<Guid, decimal>();
        string? uncategorizedKeyName = null;
        decimal uncategorizedAmount = 0m;
        var expenseList = new List<TopExpenseDto>();

        foreach (var r in rows)
        {
            var cur = string.IsNullOrWhiteSpace(r.Currency) ? "VND" : r.Currency.Trim().ToUpperInvariant();
            if (!rateMap.TryGetValue(cur, out var rate))
            {
                rate = await _exchangeRateClient.GetRateToVndAsync(cur, cancellationToken);
                rateMap[cur] = rate;
            }

            if (rate == null)
            {
                excludedCurrencies.Add(cur);
                continue;
            }

            var vndAmount = Math.Round(r.Amount * rate.Value, 2, MidpointRounding.AwayFromZero);

            if (r.Type == 2)
            {
                totalIncome += vndAmount;
            }
            else if (r.Type == 1)
            {
                totalExpense += vndAmount;

                var effectiveCatId = r.CategoryParentId ?? r.CategoryId;
                if (effectiveCatId.HasValue && effectiveCatId.Value != Guid.Empty)
                {
                    categorySums[effectiveCatId.Value] = categorySums.GetValueOrDefault(effectiveCatId.Value) + vndAmount;

                    // Lưu tên danh mục nếu chưa có trong dictionary
                    if (!catNames.ContainsKey(effectiveCatId.Value))
                    {
                        if (!r.CategoryParentId.HasValue && !string.IsNullOrWhiteSpace(r.CategoryName))
                        {
                            catNames[effectiveCatId.Value] = r.CategoryName;
                        }
                        else if (_categoryClient != null)
                        {
                            var lookup = await _categoryClient.GetCategoryAsync(effectiveCatId.Value, cancellationToken);
                            if (lookup.Exists && !string.IsNullOrWhiteSpace(lookup.Name))
                            {
                                catNames[effectiveCatId.Value] = lookup.Name;
                            }
                        }
                    }
                }
                else
                {
                    uncategorizedKeyName = "Khác";
                    uncategorizedAmount += vndAmount;
                }

                var txCatName = r.CategoryName;
                if (string.IsNullOrWhiteSpace(txCatName) && r.CategoryId.HasValue && catNames.TryGetValue(r.CategoryId.Value, out var n))
                {
                    txCatName = n;
                }

                expenseList.Add(new TopExpenseDto
                {
                    TransactionId = r.TransactionId,
                    CategoryId = r.CategoryId,
                    CategoryName = string.IsNullOrWhiteSpace(txCatName) ? "Khác" : txCatName,
                    Amount = vndAmount,
                    OriginalAmount = r.Amount,
                    Currency = cur,
                    TransactionDate = r.TransactionDate,
                    Note = r.Note
                });
            }
        }

        var byCategory = categorySums
            .Select(kvp => new CategoryBreakdownDto
            {
                CategoryId = kvp.Key,
                CategoryName = catNames.GetValueOrDefault(kvp.Key) ?? "Khác",
                Amount = kvp.Value,
                Percent = totalExpense > 0m ? Math.Round(kvp.Value / totalExpense * 100m, 1, MidpointRounding.AwayFromZero) : 0m
            })
            .ToList();

        if (uncategorizedAmount > 0m && uncategorizedKeyName != null)
        {
            byCategory.Add(new CategoryBreakdownDto
            {
                CategoryId = null,
                CategoryName = uncategorizedKeyName,
                Amount = uncategorizedAmount,
                Percent = totalExpense > 0m ? Math.Round(uncategorizedAmount / totalExpense * 100m, 1, MidpointRounding.AwayFromZero) : 0m
            });
        }

        byCategory = byCategory.OrderByDescending(c => c.Amount).ToList();

        var topExpenses = expenseList
            .OrderByDescending(e => e.Amount)
            .Take(5)
            .ToList();

        return new DashboardAggregatesDto
        {
            Month = month,
            Year = year,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            Net = totalIncome - totalExpense,
            ByCategory = byCategory,
            TopExpenses = topExpenses,
            ExcludedCurrencies = excludedCurrencies.OrderBy(c => c).ToList()
        };
    }

    public async Task<AnalysisResponse> GetAnalysisAsync(
        Guid userId, DateOnly from, DateOnly to, string groupBy, bool comparePrevious, CancellationToken cancellationToken = default)
    {
        var normGroup = groupBy.Trim().ToLowerInvariant();
        var cacheKey = $"ana:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}:{normGroup}:{comparePrevious}";
        var cached = await _cache.GetAsync<AnalysisResponse>(userId, cacheKey, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var rateMap = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase) { ["VND"] = 1m };
        var excludedCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var (points, totals) = await ComputeAnalysisPeriodAsync(userId, from, to, normGroup, rateMap, excludedCurrencies, cancellationToken);

        PreviousPeriodDto? previousDto = null;
        PeriodChangeDto? changeDto = null;

        if (comparePrevious)
        {
            var (prevFrom, prevTo) = ComputePreviousPeriod(from, to);
            var (_, prevTotals) = await ComputeAnalysisPeriodAsync(userId, prevFrom, prevTo, normGroup, rateMap, excludedCurrencies, cancellationToken);

            previousDto = new PreviousPeriodDto
            {
                From = prevFrom.ToString("yyyy-MM-dd"),
                To = prevTo.ToString("yyyy-MM-dd"),
                Totals = prevTotals
            };

            changeDto = new PeriodChangeDto
            {
                Income = CalculatePercentChange(totals.Income, prevTotals.Income),
                Expense = CalculatePercentChange(totals.Expense, prevTotals.Expense),
                Net = CalculatePercentChange(totals.Net, prevTotals.Net)
            };
        }

        var response = new AnalysisResponse
        {
            From = from.ToString("yyyy-MM-dd"),
            To = to.ToString("yyyy-MM-dd"),
            GroupBy = normGroup,
            Points = points,
            Totals = totals,
            Previous = previousDto,
            Change = changeDto,
            ExcludedCurrencies = excludedCurrencies.OrderBy(c => c).ToList()
        };

        await _cache.SetAsync(userId, cacheKey, response, CacheTtl, cancellationToken);
        return response;
    }

    private async Task<(List<AnalysisPointDto> Points, AnalysisTotalsDto Totals)> ComputeAnalysisPeriodAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        string groupBy,
        Dictionary<string, decimal?> rateMap,
        HashSet<string> excludedCurrencies,
        CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = VietnamTimeHelper.GetDateRangeUtc(from, to);

        var rows = await _db.TransactionProjections
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && !t.IsDeleted
                        && (t.Type == 1 || t.Type == 2)
                        && t.TransactionDate >= startUtc
                        && t.TransactionDate < endUtc)
            .Select(t => new { t.Type, t.Amount, t.Currency, t.TransactionDate })
            .ToListAsync(cancellationToken);

        var buckets = BuildEmptyBuckets(from, to, groupBy);
        var bucketMap = buckets.ToDictionary(b => b.Period, b => b);

        decimal totalIncome = 0m;
        decimal totalExpense = 0m;

        foreach (var r in rows)
        {
            var cur = string.IsNullOrWhiteSpace(r.Currency) ? "VND" : r.Currency.Trim().ToUpperInvariant();
            if (!rateMap.TryGetValue(cur, out var rate))
            {
                rate = await _exchangeRateClient.GetRateToVndAsync(cur, cancellationToken);
                rateMap[cur] = rate;
            }

            if (rate == null)
            {
                excludedCurrencies.Add(cur);
                continue;
            }

            var vndAmount = Math.Round(r.Amount * rate.Value, 2, MidpointRounding.AwayFromZero);
            var vnDate = VietnamTimeHelper.ToVietnamDate(r.TransactionDate);
            var periodKey = GetPeriodKey(vnDate, groupBy);

            if (!bucketMap.TryGetValue(periodKey, out var point))
            {
                continue;
            }

            if (r.Type == 2)
            {
                point.Income += vndAmount;
                totalIncome += vndAmount;
            }
            else if (r.Type == 1)
            {
                point.Expense += vndAmount;
                totalExpense += vndAmount;
            }
            point.Net = point.Income - point.Expense;
        }

        return (buckets, new AnalysisTotalsDto
        {
            Income = totalIncome,
            Expense = totalExpense,
            Net = totalIncome - totalExpense
        });
    }

    public static List<AnalysisPointDto> BuildEmptyBuckets(DateOnly from, DateOnly to, string groupBy)
    {
        var list = new List<AnalysisPointDto>();
        switch (groupBy)
        {
            case "day":
                for (var d = from; d <= to; d = d.AddDays(1))
                {
                    list.Add(new AnalysisPointDto { Period = d.ToString("yyyy-MM-dd") });
                }
                break;

            case "week":
                var startMonday = VietnamTimeHelper.GetMondayOfWeek(from);
                var endMonday = VietnamTimeHelper.GetMondayOfWeek(to);
                for (var d = startMonday; d <= endMonday; d = d.AddDays(7))
                {
                    list.Add(new AnalysisPointDto { Period = d.ToString("yyyy-MM-dd") });
                }
                break;

            case "month":
                var mStart = new DateOnly(from.Year, from.Month, 1);
                var mEnd = new DateOnly(to.Year, to.Month, 1);
                for (var d = mStart; d <= mEnd; d = d.AddMonths(1))
                {
                    list.Add(new AnalysisPointDto { Period = d.ToString("yyyy-MM") });
                }
                break;

            case "year":
                for (int y = from.Year; y <= to.Year; y++)
                {
                    list.Add(new AnalysisPointDto { Period = y.ToString("D4") });
                }
                break;
        }
        return list;
    }

    public static string GetPeriodKey(DateOnly vnDate, string groupBy) => groupBy switch
    {
        "day" => vnDate.ToString("yyyy-MM-dd"),
        "week" => VietnamTimeHelper.GetMondayOfWeek(vnDate).ToString("yyyy-MM-dd"),
        "month" => vnDate.ToString("yyyy-MM"),
        "year" => vnDate.ToString("yyyy"),
        _ => vnDate.ToString("yyyy-MM")
    };

    public static (DateOnly PrevFrom, DateOnly PrevTo) ComputePreviousPeriod(DateOnly from, DateOnly to)
    {
        // D59: Nếu from là ngày 1 và to là ngày cuối tháng (khoảng gồm trọn tháng) -> lùi đúng số tháng lịch
        if (from.Day == 1 && to.Day == DateTime.DaysInMonth(to.Year, to.Month))
        {
            int monthCount = (to.Year - from.Year) * 12 + (to.Month - from.Month) + 1;
            var prevFrom = from.AddMonths(-monthCount);
            var prevTo = from.AddDays(-1);
            return (prevFrom, prevTo);
        }

        // Các trường hợp khác: khoảng cùng số ngày, liền trước from
        int days = to.DayNumber - from.DayNumber + 1;
        var pTo = from.AddDays(-1);
        var pFrom = pTo.AddDays(-(days - 1));
        return (pFrom, pTo);
    }

    public static decimal? CalculatePercentChange(decimal current, decimal previous)
    {
        if (previous == 0m) return null;
        return Math.Round((current - previous) / previous * 100m, 1, MidpointRounding.AwayFromZero);
    }
}

