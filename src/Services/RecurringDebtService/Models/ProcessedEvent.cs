using System.ComponentModel.DataAnnotations;

namespace RecurringDebtService.Models;

public class ProcessedEvent
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

