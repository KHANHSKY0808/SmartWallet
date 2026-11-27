using Microsoft.EntityFrameworkCore;
using SharedKernel.Messaging;
using WalletService.Models;

namespace WalletService.Data;

public class WalletDbContext : DbContext, IOutboxDbContext
{
    public WalletDbContext(DbContextOptions<WalletDbContext> options) : base(options)
    {
    }

    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransfer> WalletTransfers => Set<WalletTransfer>();
    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();
    public DbSet<WalletBalanceHistory> WalletBalanceHistory => Set<WalletBalanceHistory>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).IsRequired().HasMaxLength(100);
            entity.Property(w => w.Currency).IsRequired().HasMaxLength(10).HasDefaultValue("VND");
            entity.Property(w => w.Balance).HasColumnType("decimal(18,2)");
            entity.Property(w => w.InitialBalance).HasColumnType("decimal(18,2)");
            entity.Property(w => w.RowVersion).IsConcurrencyToken().IsRequired();

            entity.HasIndex(w => new { w.UserId, w.IsArchived, w.Type });
            entity.HasIndex(w => new { w.UserId, w.Name })
                .IsUnique()
                .HasFilter("IsArchived = 0");
        });

        modelBuilder.Entity<WalletTransfer>(entity =>
        {
            entity.HasKey(wt => wt.Id);
            entity.Property(wt => wt.Amount).HasColumnType("decimal(18,2)");
            entity.Property(wt => wt.Fee).HasColumnType("decimal(18,2)");
            entity.Property(wt => wt.Note).HasMaxLength(500);
            entity.Property(wt => wt.IdempotencyKey).IsRequired().HasMaxLength(100);

            entity.HasOne(wt => wt.SourceWallet)
                .WithMany()
                .HasForeignKey(wt => wt.SourceWalletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(wt => wt.TargetWallet)
                .WithMany()
                .HasForeignKey(wt => wt.TargetWalletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(wt => new { wt.UserId, wt.IdempotencyKey }).IsUnique();
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(k => new { k.UserId, k.IdempotencyKey });
            entity.Property(k => k.IdempotencyKey).HasMaxLength(100);
            entity.Property(k => k.Operation).HasMaxLength(100);
            entity.Property(k => k.RequestHash).HasMaxLength(64);
        });

        modelBuilder.Entity<WalletBalanceHistory>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.Property(h => h.ChangeAmount).HasColumnType("decimal(18,2)");
            entity.Property(h => h.BalanceAfter).HasColumnType("decimal(18,2)");
            entity.HasIndex(h => new { h.UserId, h.WalletId });
        });

        modelBuilder.ConfigureOutboxMessages();
    }

    public override int SaveChanges()
    {
        UpdateConcurrencyTokens();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateConcurrencyTokens()
    {
        var entries = ChangeTracker.Entries<Wallet>()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
    }
}

