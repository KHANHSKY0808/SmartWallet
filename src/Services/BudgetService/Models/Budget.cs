using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetService.Models;

public class Budget
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid CategoryId { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    public int Month { get; set; }

    [Required]
    public int Year { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentSpent { get; set; } = 0m;

    public bool IsAlertSent80 { get; set; } = false;

    public bool IsAlertSent100 { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<BudgetTransaction> Transactions { get; set; } = new List<BudgetTransaction>();
}

