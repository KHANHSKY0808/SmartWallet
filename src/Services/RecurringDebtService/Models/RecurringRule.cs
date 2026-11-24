using System.ComponentModel.DataAnnotations;

namespace RecurringDebtService.Models;

public enum FrequencyType
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4
}

public class RecurringRule
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid WalletId { get; set; }

    [Required]
    public Guid CategoryId { get; set; }

    [Required]
    public int Type { get; set; } // 1=Expense, 2=Income

    [Required]
    public decimal Amount { get; set; }

    [MaxLength(10)]
    public string Currency { get; set; } = "VND";

    [Required]
    public FrequencyType Frequency { get; set; } = FrequencyType.Monthly;

    [Required]
    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    [Required]
    public DateOnly NextExecutionDate { get; set; }

    public int AnchorDay { get; set; } = 1; // 1-31

    public bool IsActive { get; set; } = true;

    public DateTime? LastExecutedDate { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }

    public DateOnly? ReminderSentOn { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

