using System.ComponentModel.DataAnnotations;

namespace AuditService.Models;

/// <summary>D48/D63: Nhật ký kiểm toán bất biến (không cập nhật, không xóa).</summary>
public class AuditLog
{
    [Key]
    public long Id { get; set; }

    public Guid? EventId { get; set; }

    public Guid? UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string ServiceName { get; set; } = "Unknown";

    [Required]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? EntityType { get; set; }

    [MaxLength(100)]
    public string? EntityId { get; set; }

    public string? OldValueJson { get; set; }

    public string? NewValueJson { get; set; }

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

