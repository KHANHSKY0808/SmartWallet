using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportService.Models;

public class TransactionProjection
{
    [Key]
    public Guid TransactionId { get; set; }

    public Guid UserId { get; set; }
    public Guid WalletId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? CategoryParentId { get; set; }

    [MaxLength(100)]
    public string? CategoryName { get; set; }

    public int Type { get; set; } // 1=Expense, 2=Income, 3=Transfer

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [MaxLength(10)]
    public string Currency { get; set; } = "VND";

    public DateTime TransactionDate { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime LastEventAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ReportCategoryCache
{
    [Key]
    public Guid CategoryId { get; set; }

    public Guid? UserId { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum ExportFormat
{
    Xlsx = 1,
    Pdf = 2
}

public enum ExportJobStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public class ExportJob
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [MaxLength(50)]
    public string ReportType { get; set; } = "Transactions";

    public ExportFormat Format { get; set; } = ExportFormat.Xlsx;

    public string ParametersJson { get; set; } = "{}";

    public ExportJobStatus Status { get; set; } = ExportJobStatus.Queued;

    /// <summary>
    /// Để null ở đợt 6a theo D60 (ReportService tự lưu file trong thư mục riêng tên GUID).
    /// </summary>
    public Guid? FileId { get; set; }

    [MaxLength(500)]
    public string? StorageFileName { get; set; }

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class ProcessedEvent
{
    [Key]
    public Guid EventId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

