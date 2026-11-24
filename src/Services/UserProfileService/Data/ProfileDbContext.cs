using Microsoft.EntityFrameworkCore;
using UserProfileService.Models;

namespace UserProfileService.Data;

public class ProfileDbContext : DbContext
{
    public ProfileDbContext(DbContextOptions<ProfileDbContext> options) : base(options)
    {
    }

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<ExchangeRateHistory> ExchangeRateHistories => Set<ExchangeRateHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("UserProfiles");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.FullName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
            entity.Property(e => e.DefaultCurrency).IsRequired().HasMaxLength(10).HasDefaultValue("VND");
            entity.Property(e => e.Language).IsRequired().HasMaxLength(10).HasDefaultValue("vi");
        });

        modelBuilder.Entity<Currency>(entity =>
        {
            entity.ToTable("Currencies");
            entity.HasKey(e => e.Code);
            entity.Property(e => e.Code).HasMaxLength(10);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Symbol).IsRequired().HasMaxLength(5);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<ExchangeRate>(entity =>
        {
            entity.ToTable("ExchangeRates");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BaseCurrency).IsRequired().HasMaxLength(10);
            entity.Property(e => e.TargetCurrency).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Rate).HasColumnType("decimal(18,6)").IsRequired();
            entity.Property(e => e.Source).HasMaxLength(30).HasDefaultValue("Manual");
            entity.HasIndex(e => new { e.BaseCurrency, e.TargetCurrency }).IsUnique();
        });

        modelBuilder.Entity<ExchangeRateHistory>(entity =>
        {
            entity.ToTable("ExchangeRateHistories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BaseCurrency).IsRequired().HasMaxLength(10);
            entity.Property(e => e.TargetCurrency).IsRequired().HasMaxLength(10);
            entity.Property(e => e.OldRate).HasColumnType("decimal(18,6)");
            entity.Property(e => e.NewRate).HasColumnType("decimal(18,6)").IsRequired();
        });
    }
}

