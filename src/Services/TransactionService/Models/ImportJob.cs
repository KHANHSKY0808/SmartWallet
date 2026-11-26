using System.ComponentModel.DataAnnotations;

namespace TransactionService.Models;

public enum ImportJobStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public class ImportJob
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? TempFilePath { get; set; }

    [MaxLength(16)]
    public string Extension { get; set; } = ".csv";

    public string? ColumnMappingJson { get; set; }

    public int TotalRows { get; set; }

    public int Succeeded { get; set; }

    public int Failed { get; set; }

    public ImportJobStatus Status { get; set; } = ImportJobStatus.Queued;

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public List<ImportJobError> Errors { get; set; } = new();
}

public class ImportJobError
{
    [Key]
    public long Id { get; set; }

    public Guid ImportJobId { get; set; }

    public int RowNumber { get; set; }

    [MaxLength(100)]
    public string? DateRaw { get; set; }

    [MaxLength(100)]
    public string? AmountRaw { get; set; }

    [MaxLength(50)]
    public string? TypeRaw { get; set; }

    [MaxLength(200)]
    public string? CategoryRaw { get; set; }

    [MaxLength(200)]
    public string? WalletRaw { get; set; }

    [MaxLength(1000)]
    public string? NoteRaw { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public ImportJob? ImportJob { get; set; }
}

