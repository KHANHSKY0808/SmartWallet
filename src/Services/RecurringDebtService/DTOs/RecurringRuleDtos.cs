using System.ComponentModel.DataAnnotations;
using RecurringDebtService.Models;

namespace RecurringDebtService.DTOs;

public class CreateRecurringRuleRequest
{
    [Required(ErrorMessage = "Tên quy tắc không được để trống")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "WalletId không được để trống")]
    public Guid WalletId { get; set; }

    [Required(ErrorMessage = "CategoryId không được để trống")]
    public Guid CategoryId { get; set; }

    [Range(1, 2, ErrorMessage = "Loại giao dịch định kỳ chỉ có thể là 1 (Chi) hoặc 2 (Thu)")]
    public int Type { get; set; }

    [Range(0.01, 1_000_000_000_000, ErrorMessage = "Số tiền phải lớn hơn 0")]
    public decimal Amount { get; set; }

    [Range(1, 4, ErrorMessage = "Tần suất chỉ có thể là 1 (Ngày), 2 (Tuần), 3 (Tháng), 4 (Năm)")]
    public int Frequency { get; set; }

    [Required(ErrorMessage = "Ngày bắt đầu là bắt buộc")]
    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string? Note { get; set; }
}

public class UpdateRecurringRuleRequest
{
    public string? Name { get; set; }

    public Guid? WalletId { get; set; }

    public Guid? CategoryId { get; set; }

    public int? Type { get; set; }

    public decimal? Amount { get; set; }

    public int? Frequency { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool? IsActive { get; set; }

    public string? Note { get; set; }
}

public class RecurringRuleDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid WalletId { get; set; }
    public Guid CategoryId { get; set; }
    public int Type { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public int Frequency { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string? EndDate { get; set; }
    public string NextExecutionDate { get; set; } = string.Empty;
    public int AnchorDay { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastExecutedDate { get; set; }
    public string? LastError { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

