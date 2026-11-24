using Microsoft.EntityFrameworkCore;
using NotificationService.Models;

namespace NotificationService.Data;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.UserId, e.CreatedAt })
                .HasDatabaseName("IX_Notifications_User_CreatedAt");

            // D37, N-02: Idempotent theo EventId
            entity.HasIndex(e => e.EventId)
                .HasDatabaseName("IX_Notifications_EventId");
        });
    }
}

