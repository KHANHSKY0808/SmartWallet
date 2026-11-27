using Microsoft.EntityFrameworkCore;
using RecurringDebtService.Models;
using SharedKernel.Messaging;

namespace RecurringDebtService.Data;

public class RecurringDebtDbContext : DbContext, IOutboxDbContext
{
    public RecurringDebtDbContext(DbContextOptions<RecurringDebtDbContext> options) : base(options)
    {
    }

    public DbSet<RecurringRule> RecurringRules => Set<RecurringRule>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<DebtPayment> DebtPayments => Set<DebtPayment>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RecurringRule>(entity =>
        {
            entity.ToTable("RecurringRules");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.RowVersion).IsConcurrencyToken();
            entity.HasIndex(e => new { e.UserId, e.IsActive });
            entity.HasIndex(e => e.NextExecutionDate);
        });

        modelBuilder.Entity<Debt>(entity =>
        {
            entity.ToTable("Debts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.PaidAmount).HasPrecision(18, 2);
            entity.Property(e => e.InterestRate).HasPrecision(9, 4);
            entity.Property(e => e.RowVersion).IsConcurrencyToken();
            entity.HasIndex(e => new { e.UserId, e.Status, e.DueDate });
            entity.HasIndex(e => e.DueDate);
        });

        modelBuilder.Entity<DebtPayment>(entity =>
        {
            entity.ToTable("DebtPayments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.HasIndex(e => new { e.DebtId, e.PaymentDate });
            entity.HasIndex(e => new { e.UserId, e.PaymentDate });
            entity.HasIndex(e => e.IdempotencyKey);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(e => new { e.UserId, e.Key });
            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("ProcessedEvents");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.ConfigureOutboxMessages();
    }
}

