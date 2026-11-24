using Microsoft.EntityFrameworkCore;
using BudgetService.Models;

namespace BudgetService.Data;

public class BudgetDbContext : DbContext
{
    public BudgetDbContext(DbContextOptions<BudgetDbContext> options) : base(options)
    {
    }

    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetTransaction> BudgetTransactions => Set<BudgetTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CategoryName)
                .HasMaxLength(100);

            // D31, FIN-65: Unique (UserId, CategoryId, Month, Year)
            entity.HasIndex(e => new { e.UserId, e.CategoryId, e.Month, e.Year })
                .IsUnique()
                .HasDatabaseName("IX_Budgets_User_Category_Month_Year");

            entity.HasIndex(e => new { e.UserId, e.Month, e.Year })
                .HasDatabaseName("IX_Budgets_User_Month_Year");
        });

        modelBuilder.Entity<BudgetTransaction>(entity =>
        {
            // D32: Composite PK (BudgetId, TransactionId)
            entity.HasKey(e => new { e.BudgetId, e.TransactionId });

            entity.HasIndex(e => new { e.UserId, e.TransactionDate })
                .HasDatabaseName("IX_BudgetTransactions_User_Date");

            entity.HasIndex(e => e.TransactionId)
                .HasDatabaseName("IX_BudgetTransactions_TransactionId");

            entity.HasOne(e => e.Budget)
                .WithMany(b => b.Transactions)
                .HasForeignKey(e => e.BudgetId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

