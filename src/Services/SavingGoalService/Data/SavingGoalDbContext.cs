using Microsoft.EntityFrameworkCore;
using SavingGoalService.Models;

namespace SavingGoalService.Data;

public class SavingGoalDbContext : DbContext
{
    public SavingGoalDbContext(DbContextOptions<SavingGoalDbContext> options) : base(options)
    {
    }

    public DbSet<SavingGoal> SavingGoals => Set<SavingGoal>();
    public DbSet<GoalLog> GoalLogs => Set<GoalLog>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SavingGoal>(entity =>
        {
            entity.ToTable("SavingGoals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(150);
            entity.Property(e => e.TargetAmount).HasColumnType("decimal(18,2)").IsRequired();
            entity.Property(e => e.CurrentAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(10).HasDefaultValue("VND");
            entity.Property(e => e.Status).HasDefaultValue(1);
            entity.Property(e => e.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
            entity.HasIndex(e => new { e.UserId, e.Status, e.TargetDate });
        });

        modelBuilder.Entity<GoalLog>(entity =>
        {
            entity.ToTable("GoalLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)").IsRequired();
            entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
            entity.HasIndex(e => new { e.GoalId, e.CreatedDate });
            entity.HasIndex(e => new { e.UserId, e.CreatedDate });
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("ProcessedEvents");
            entity.HasKey(e => e.EventId);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(100);
        });
    }
}
