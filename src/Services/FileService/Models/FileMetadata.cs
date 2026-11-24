namespace FileService.Models;

public class FileMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public int Purpose { get; set; } = 1; // 1: Receipt, 2: Avatar, 3: ReportExport, 4: ImportSource, 5: ImportErrors
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = "Local";
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public bool IsEncrypted { get; set; } = false;
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
