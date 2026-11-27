using Microsoft.EntityFrameworkCore;
using ReportService.Models;
using SharedKernel.Messaging;

namespace ReportService.Data;

public class ReportDbContext : DbContext, IOutboxDbContext
{
    public ReportDbContext(DbContextOptions<ReportDbContext> options) : base(options)
    {
    }

    public DbSet<TransactionProjection> TransactionProjections => Set<TransactionProjection>();
    public DbSet<ReportCategoryCache> ReportCategories => Set<ReportCategoryCache>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TransactionProjection>(entity =>
        {
            entity.ToTable("TransactionProjections");
            entity.HasKey(e => e.TransactionId);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(10).HasDefaultValue("VND");

            // Covering indexes cho Dashboard < 2s với 50.000 bản ghi (FIN-104 / DS-10)
            entity.HasIndex(e => new { e.UserId, e.TransactionDate })
                .HasDatabaseName("IX_TxProj_User_Date");

            entity.HasIndex(e => new { e.UserId, e.CategoryId, e.TransactionDate })
                .HasDatabaseName("IX_TxProj_User_Category_Date");
        });

        modelBuilder.Entity<ReportCategoryCache>(entity =>
        {
            entity.ToTable("ReportCategories");
            entity.HasKey(e => e.CategoryId);
        });

        modelBuilder.Entity<ExportJob>(entity =>
        {
            entity.ToTable("ExportJobs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt })
                .HasDatabaseName("IX_ExportJobs_User_Date");
            entity.HasIndex(e => new { e.Status, e.CreatedAt })
                .HasDatabaseName("IX_ExportJobs_Queue");
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("ProcessedEvents");
            entity.HasKey(e => e.EventId);
        });

        modelBuilder.ConfigureOutboxMessages();
    }
}

