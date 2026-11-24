using System.ComponentModel.DataAnnotations;

namespace RecurringDebtService.Models;

public class DebtPayment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid DebtId { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid WalletId { get; set; }

    [Required]
    public decimal Amount { get; set; }

    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Note { get; set; }

    public Guid? TransactionId { get; set; }

    [MaxLength(100)]
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

