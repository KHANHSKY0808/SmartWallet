using System.ComponentModel.DataAnnotations;
using RecurringDebtService.Models;

namespace RecurringDebtService.DTOs;

public class CreateDebtRequest
{
    [Range(1, 2, ErrorMessage = "Loại khoản nợ chỉ có thể là 1 (Cho vay) hoặc 2 (Đi vay)")]
    public int Type { get; set; }

    [Required(ErrorMessage = "Tên người liên quan không được để trống")]
    public string PersonName { get; set; } = string.Empty;

    [Range(0.01, 1_000_000_000_000, ErrorMessage = "Số tiền phải lớn hơn 0")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Hạn thanh toán là bắt buộc")]
    public DateOnly DueDate { get; set; }

    [Required(ErrorMessage = "WalletId không được để trống")]
    public Guid WalletId { get; set; }

    public decimal? InterestRate { get; set; }

    public string? Note { get; set; }
}

public class UpdateDebtRequest
{
    public decimal? Amount { get; set; }
    public string? PersonName { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal? InterestRate { get; set; }
    public string? Note { get; set; }
}

public class DebtDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount => Amount - PaidAmount;
    public decimal? InterestRate { get; set; }
    public string Currency { get; set; } = "VND";
    public string DueDate { get; set; } = string.Empty;
    public string Status { get; set; } = "Active"; // "Active", "PaidFull", "Overdue"
    public int StatusCode { get; set; } = 1;
    public Guid WalletId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateDebtPaymentRequest
{
    [Range(0.01, 1_000_000_000_000, ErrorMessage = "Số tiền thanh toán phải lớn hơn 0")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "WalletId không được để trống")]
    public Guid WalletId { get; set; }

    public string? Note { get; set; }
}

public class DebtPaymentDto
{
    public Guid Id { get; set; }
    public Guid DebtId { get; set; }
    public Guid UserId { get; set; }
    public Guid WalletId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? Note { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
}

