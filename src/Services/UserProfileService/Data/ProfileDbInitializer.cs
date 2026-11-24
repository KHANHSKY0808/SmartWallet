using Microsoft.EntityFrameworkCore;
using UserProfileService.Models;

namespace UserProfileService.Data;

public static class ProfileDbInitializer
{
    public static async Task InitializeAsync(ProfileDbContext context, ILogger logger)
    {
        try
        {
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
            logger.LogInformation("Ensured created and set SQLite WAL mode for Profile DB");

            if (!await context.Currencies.AnyAsync())
            {
                var currencies = new List<Currency>
                {
                    new() { Code = "VND", Name = "Việt Nam Đồng", Symbol = "₫", IsActive = true },
                    new() { Code = "USD", Name = "Đô la Mỹ", Symbol = "$", IsActive = true },
                    new() { Code = "EUR", Name = "Euro", Symbol = "€", IsActive = true },
                    new() { Code = "JPY", Name = "Yên Nhật", Symbol = "¥", IsActive = true }
                };

                await context.Currencies.AddRangeAsync(currencies);
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded default currencies (VND, USD, EUR, JPY)");
            }

            if (!await context.ExchangeRates.AnyAsync())
            {
                var rates = new List<ExchangeRate>
                {
                    new() { BaseCurrency = "USD", TargetCurrency = "VND", Rate = 25450m, Source = "Manual", UpdatedAt = DateTime.UtcNow },
                    new() { BaseCurrency = "EUR", TargetCurrency = "VND", Rate = 27500m, Source = "Manual", UpdatedAt = DateTime.UtcNow }
                };

                await context.ExchangeRates.AddRangeAsync(rates);
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded default exchange rates (USD->VND: 25,450, EUR->VND: 27,500)");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing Profile DB");
            throw;
        }
    }
}

