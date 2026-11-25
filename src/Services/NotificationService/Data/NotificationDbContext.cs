using Microsoft.EntityFrameworkCore;
using NotificationService.Models;

namespace NotificationService.Data;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EmailQueueItem> EmailQueue => Set<EmailQueueItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.UserId, e.CreatedAt })
                .HasDatabaseName("IX_Notifications_User_CreatedAt");

            // D37, NT-05: Idempotent duy nhất theo EventId
            entity.HasIndex(e => e.EventId)
                .IsUnique()
                .HasFilter("\"EventId\" IS NOT NULL")
                .HasDatabaseName("IX_Notifications_EventId");
        });

        modelBuilder.Entity<EmailQueueItem>(entity =>
        {
            entity.ToTable("EmailQueue");
            entity.HasKey(e => e.Id);

            // D62, EM-05: Idempotent theo EventId
            entity.HasIndex(e => e.EventId)
                .IsUnique()
                .HasFilter("\"EventId\" IS NOT NULL")
                .HasDatabaseName("UX_EmailQueue_EventId");

            entity.HasIndex(e => new { e.Status, e.NextRetryAt, e.CreatedAt })
                .HasDatabaseName("IX_EmailQueue_Dispatch");
        });
    }
}

