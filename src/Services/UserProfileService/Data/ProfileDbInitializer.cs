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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing Profile DB");
            throw;
        }
    }
}

