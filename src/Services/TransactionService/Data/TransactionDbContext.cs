using Microsoft.EntityFrameworkCore;
using TransactionService.Models;

namespace TransactionService.Data;

public class TransactionDbContext : DbContext
{
    public TransactionDbContext(DbContextOptions<TransactionDbContext> options) : base(options)
    {
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
        });
    }
}

