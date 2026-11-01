using IdentityService.Models;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IdentityDbContext context, ILogger logger)
    {
        try
        {
            await context.Database.EnsureCreatedAsync();

            // Seed Roles
            if (!await context.Roles.AnyAsync())
            {
                var roles = new List<Role>
                {
                    new() { Id = 1, Name = "Admin", Description = "Quản trị viên hệ thống" },
                    new() { Id = 2, Name = "User", Description = "Người dùng tiêu chuẩn" }
                };

                await context.Roles.AddRangeAsync(roles);
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded default roles (Admin, User)");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing Identity DB");
            throw;
        }
    }
}
