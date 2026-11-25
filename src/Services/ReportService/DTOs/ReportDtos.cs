using System.ComponentModel.DataAnnotations;

namespace ReportService.DTOs;

public class CategoryBreakdownDto
{
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Percent { get; set; }
}

public class TopExpenseDto
{
    public Guid TransactionId { get; set; }
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal OriginalAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
}

public class DashboardBudgetDto
{
    public Guid BudgetId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Limit { get; set; }
    public decimal Spent { get; set; }
    public decimal Percent { get; set; }
}

public class DashboardAggregatesDto
{
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal Net { get; set; }
    public List<CategoryBreakdownDto> ByCategory { get; set; } = new();
    public List<TopExpenseDto> TopExpenses { get; set; } = new();
    public List<string> ExcludedCurrencies { get; set; } = new();
}

public class DashboardResponse
{
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal Net { get; set; }
    public List<CategoryBreakdownDto> ByCategory { get; set; } = new();
    public List<TopExpenseDto> TopExpenses { get; set; } = new();
    public List<DashboardBudgetDto>? Budgets { get; set; }
    public bool BudgetsUnavailable { get; set; }
    public List<string> ExcludedCurrencies { get; set; } = new();
}

public class AnalysisPointDto
{
    public string Period { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net { get; set; }
}

public class AnalysisTotalsDto
{
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net { get; set; }
}

public class PreviousPeriodDto
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public AnalysisTotalsDto Totals { get; set; } = new();
}

public class PeriodChangeDto
{
    public decimal? Income { get; set; }
    public decimal? Expense { get; set; }
    public decimal? Net { get; set; }
}

public class AnalysisResponse
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string GroupBy { get; set; } = "month";
    public List<AnalysisPointDto> Points { get; set; } = new();
    public AnalysisTotalsDto Totals { get; set; } = new();
    public PreviousPeriodDto? Previous { get; set; }
    public PeriodChangeDto? Change { get; set; }
    public List<string> ExcludedCurrencies { get; set; } = new();
}

public class CreateExportRequest
{
    [Required]
    public string Format { get; set; } = "xlsx";

    [Required]
    public string From { get; set; } = string.Empty;

    [Required]
    public string To { get; set; } = string.Empty;
}

public class ExportJobDto
{
    public Guid JobId { get; set; }
    public Guid Id => JobId;
    public string Format { get; set; } = "xlsx";
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class ExportParameters
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

