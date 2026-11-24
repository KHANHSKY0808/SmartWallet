using System.ComponentModel.DataAnnotations;

namespace RecurringDebtService.Models;

public class IdempotencyRecord
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Operation { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string RequestHash { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string ResponseBody { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
}

