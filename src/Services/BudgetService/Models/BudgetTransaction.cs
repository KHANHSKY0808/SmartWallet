using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetService.Models;

public class BudgetTransaction
{
    [Required]
    public Guid BudgetId { get; set; }

    [Required]
    public Guid TransactionId { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    public DateTime TransactionDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Budget? Budget { get; set; }
}

