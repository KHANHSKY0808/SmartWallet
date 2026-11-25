using AuditService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuditService.Data;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options)
    {
    }

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.EventId)
                .IsUnique()
                .HasFilter("\"EventId\" IS NOT NULL")
                .HasDatabaseName("UX_AuditLogs_EventId");

            entity.HasIndex(e => new { e.UserId, e.Timestamp })
                .HasDatabaseName("IX_AuditLogs_User_Time");

            entity.HasIndex(e => new { e.Action, e.Timestamp })
                .HasDatabaseName("IX_AuditLogs_Action_Time");

            entity.HasIndex(e => new { e.ServiceName, e.Timestamp })
                .HasDatabaseName("IX_AuditLogs_Service_Time");

            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_AuditLogs_Time");
        });
    }

    public static async Task EnsureImmutableTriggersAsync(AuditDbContext context)
    {
        // D48 / D63 / AU-05: Trigger SQLite chặn mọi UPDATE và DELETE trực tiếp trên bảng AuditLogs
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TRIGGER IF NOT EXISTS TR_AuditLogs_PreventUpdate
            BEFORE UPDATE ON AuditLogs
            BEGIN
                SELECT RAISE(ABORT, 'AuditLogs is immutable');
            END;");

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TRIGGER IF NOT EXISTS TR_AuditLogs_PreventDelete
            BEFORE DELETE ON AuditLogs
            BEGIN
                SELECT RAISE(ABORT, 'AuditLogs is immutable');
            END;");
    }
}

