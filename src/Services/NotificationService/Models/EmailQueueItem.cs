using System.ComponentModel.DataAnnotations;

namespace NotificationService.Models;

public enum EmailQueueStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

/// <summary>Hàng đợi email bền vững theo D62 (đóng O5).</summary>
public class EmailQueueItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? EventId { get; set; }

    public Guid? UserId { get; set; }

    [Required]
    [MaxLength(150)]
    public string ToEmail { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>D62/EM-06: Xóa (đặt null) sau khi Sent quá 24 giờ để không lưu token lâu dài.</summary>
    public string? BodyHtml { get; set; }

    [MaxLength(100)]
    public string TemplateCode { get; set; } = string.Empty;

    public EmailQueueStatus Status { get; set; } = EmailQueueStatus.Pending;

    public int Attempts { get; set; } = 0;

    public DateTime? NextRetryAt { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SentAt { get; set; }
}

