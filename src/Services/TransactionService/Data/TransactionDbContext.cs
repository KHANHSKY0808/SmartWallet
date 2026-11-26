using Microsoft.EntityFrameworkCore;
using TransactionService.Models;

namespace TransactionService.Data;

public class TransactionDbContext : DbContext
{
    public TransactionDbContext(DbContextOptions<TransactionDbContext> options) : base(options)
    {
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TransactionTag> TransactionTags => Set<TransactionTag>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportJobError> ImportJobErrors => Set<ImportJobError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Color).HasMaxLength(20);
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<TransactionTag>(entity =>
        {
            entity.ToTable("TransactionTags");
            entity.HasKey(e => new { e.TransactionId, e.TagId });
            entity.HasOne(e => e.Transaction)
                  .WithMany()
                  .HasForeignKey(e => e.TransactionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Tag)
                  .WithMany()
                  .HasForeignKey(e => e.TagId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.TagId);
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.ToTable("Transactions");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.WalletId).IsRequired();
            entity.Property(e => e.CategoryId).IsRequired(false);
            entity.Property(e => e.Type).IsRequired();

            entity.Property(e => e.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(e => e.TransactionDate)
                .IsRequired();

            entity.Property(e => e.Note)
                .HasMaxLength(1000);

            entity.Property(e => e.ReceiptImageUrl)
                .HasMaxLength(500);

            entity.Property(e => e.ReceiptFileId)
                .IsRequired(false);

            // Optimistic Concurrency Token
            entity.Property(e => e.RowVersion)
                .IsConcurrencyToken()
                .IsRequired();

            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Composite Indexes theo đặc tả Sprint 3 §FIN-43
            entity.HasIndex(e => new { e.UserId, e.TransactionDate })
                .HasDatabaseName("IX_Transactions_UserId_TransactionDate");

            entity.HasIndex(e => new { e.UserId, e.CategoryId })
                .HasDatabaseName("IX_Transactions_UserId_CategoryId");

            entity.HasIndex(e => new { e.UserId, e.WalletId })
                .HasDatabaseName("IX_Transactions_UserId_WalletId");

            entity.HasIndex(e => e.ReceiptFileId)
                .IsUnique()
                .HasFilter("ReceiptFileId IS NOT NULL");

            entity.HasIndex(e => new { e.SourceType, e.SourceId, e.OccurrenceDate })
                .IsUnique()
                .HasFilter("SourceType = 2 AND SourceId IS NOT NULL AND OccurrenceDate IS NOT NULL");

            entity.HasIndex(e => new { e.SourceType, e.SourceId, e.SourceSubId })
                .IsUnique()
                .HasFilter("SourceType = 3 AND SourceSubId IS NOT NULL");

            entity.HasIndex(e => new { e.SourceType, e.SourceId, e.SourceSubId })
                .IsUnique()
                .HasDatabaseName("UX_Transactions_Import_JobRow")
                .HasFilter("SourceType = 5 AND SourceId IS NOT NULL AND SourceSubId IS NOT NULL");
        });

        modelBuilder.Entity<ImportJob>(entity =>
        {
            entity.ToTable("ImportJobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.TempFilePath).HasMaxLength(500);
            entity.Property(e => e.Extension).HasMaxLength(16);
            entity.Property(e => e.ErrorMessage).HasMaxLength(500);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasDatabaseName("UX_ImportJobs_User_Active")
                .HasFilter("Status IN (0, 1)");
        });

        modelBuilder.Entity<ImportJobError>(entity =>
        {
            entity.ToTable("ImportJobErrors");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(500);
            entity.HasOne(e => e.ImportJob)
                .WithMany(j => j.Errors)
                .HasForeignKey(e => e.ImportJobId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ImportJobId, e.RowNumber }).IsUnique();
        });
    }
}

