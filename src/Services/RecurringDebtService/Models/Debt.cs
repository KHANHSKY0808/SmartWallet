using System.ComponentModel.DataAnnotations;

namespace RecurringDebtService.Models;

public enum DebtType
{
    Lend = 1,   // Cho vay (ví giảm, category Cho vay)
    Borrow = 2  // Đi vay (ví tăng, category Đi vay)
}

public enum DebtStatus
{
    Active = 1,
    PaidFull = 2,
    Overdue = 3
}

public class Debt
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public DebtType Type { get; set; }

    [Required]
    [MaxLength(100)]
    public string PersonName { get; set; } = string.Empty;

    [Required]
    public decimal Amount { get; set; }

    public decimal PaidAmount { get; set; } = 0;

    public decimal? InterestRate { get; set; } // % / năm

    [MaxLength(10)]
    public string Currency { get; set; } = "VND";

    [Required]
    public DateOnly DueDate { get; set; }

    [Required]
    public DebtStatus Status { get; set; } = DebtStatus.Active;

    [Required]
    public Guid WalletId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateOnly? ReminderSentOn { get; set; }

    public bool OverdueNotified { get; set; } = false;

    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

