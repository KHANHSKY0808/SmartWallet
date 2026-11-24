using System.ComponentModel.DataAnnotations;

namespace NotificationService.Models;

public class Notification
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = "BudgetAlert";

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; } = false;

    public string? Metadata { get; set; }

    public Guid? EventId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

