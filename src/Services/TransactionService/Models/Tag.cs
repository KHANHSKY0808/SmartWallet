using System.ComponentModel.DataAnnotations;

namespace TransactionService.Models;

public class Tag
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Color { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TransactionTag
{
    public Guid TransactionId { get; set; }
    public Guid TagId { get; set; }

    public Transaction? Transaction { get; set; }
    public Tag? Tag { get; set; }
}
