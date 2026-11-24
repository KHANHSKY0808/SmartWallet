using Microsoft.EntityFrameworkCore;
using FileService.Models;

namespace FileService.Data;

public class FileDbContext : DbContext
{
    public FileDbContext(DbContextOptions<FileDbContext> options) : base(options)
    {
    }

    public DbSet<FileMetadata> FileMetadatas => Set<FileMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FileMetadata>(entity =>
        {
            entity.ToTable("FileMetadata");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.StoragePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.StorageProvider).IsRequired().HasMaxLength(20).HasDefaultValue("Local");
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Sha256).HasMaxLength(64);
            entity.Property(e => e.RelatedEntityType).HasMaxLength(50);
            entity.Property(e => e.IsEncrypted).HasDefaultValue(false);

            entity.HasIndex(e => new { e.OwnerUserId, e.Purpose, e.CreatedAt });
            entity.HasIndex(e => new { e.StorageProvider, e.StoragePath }).IsUnique();
        });
    }
}
