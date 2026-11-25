using IdentityService.Models;
using IdentityService.Services;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;

namespace IdentityService.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(
        IdentityDbContext context,
        ILogger logger,
        IConfiguration? configuration = null,
        IPasswordHasher? passwordHasher = null)
    {
        try
        {
            await context.Database.MigrateAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
            logger.LogInformation("Applied EF Core Migrations and set SQLite WAL mode for Identity DB");

            // D65: Seed 3 vai trò Admin, User, Support
            var roleDefs = new (int Id, string Name, string Description)[]
            {
                (1, "Admin", "Quản trị viên hệ thống"),
                (2, "User", "Người dùng tiêu chuẩn"),
                (3, "Support", "Hỗ trợ vận hành (chỉ đọc người dùng và audit)")
            };

            foreach (var (id, name, desc) in roleDefs)
            {
                if (!await context.Roles.AnyAsync(r => r.Name == name))
                {
                    context.Roles.Add(new Role { Id = id, Name = name, Description = desc });
                }
            }
            await context.SaveChangesAsync();

            // D65: Seed 7 quyền RBAC chuẩn
            var permDefs = new (string Code, string Name)[]
            {
                (AppPermissions.UsersRead, "Xem danh sách người dùng"),
                (AppPermissions.UsersLock, "Khóa / mở khóa tài khoản"),
                (AppPermissions.UsersRolesWrite, "Thay đổi vai trò người dùng"),
                (AppPermissions.AuditRead, "Tra cứu nhật ký kiểm toán"),
                (AppPermissions.RatesWrite, "Cập nhật tỷ giá ngoại tệ"),
                (AppPermissions.CategoriesDefaultsWrite, "Quản lý danh mục mặc định"),
                (AppPermissions.HealthRead, "Giám sát sức khỏe hệ thống")
            };

            foreach (var (code, name) in permDefs)
            {
                if (!await context.Permissions.AnyAsync(p => p.Code == code))
                {
                    context.Permissions.Add(new Permission { Code = code, Name = name, GroupName = "Quản trị hệ thống" });
                }
            }
            await context.SaveChangesAsync();

            var adminRole = await context.Roles.FirstAsync(r => r.Name == "Admin");
            var supportRole = await context.Roles.FirstAsync(r => r.Name == "Support");
            var allPerms = await context.Permissions.ToListAsync();

            foreach (var p in allPerms)
            {
                if (!await context.RolePermissions.AnyAsync(rp => rp.RoleId == adminRole.Id && rp.PermissionId == p.Id))
                {
                    context.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = p.Id });
                }

                if ((p.Code == AppPermissions.UsersRead || p.Code == AppPermissions.AuditRead)
                    && !await context.RolePermissions.AnyAsync(rp => rp.RoleId == supportRole.Id && rp.PermissionId == p.Id))
                {
                    context.RolePermissions.Add(new RolePermission { RoleId = supportRole.Id, PermissionId = p.Id });
                }
            }
            await context.SaveChangesAsync();

            // D66: Seed tài khoản Admin đầu tiên từ Seed:Admin:Email & Seed:Admin:Password nếu chưa có Admin nào
            var seedEmail = configuration?["Seed:Admin:Email"];
            var seedPassword = configuration?["Seed:Admin:Password"];
            if (!string.IsNullOrWhiteSpace(seedEmail) && !string.IsNullOrWhiteSpace(seedPassword))
            {
                var hasAnyAdmin = await context.UserRoles.AnyAsync(ur => ur.RoleId == adminRole.Id);
                if (!hasAnyAdmin)
                {
                    var normalizedEmail = seedEmail.Trim().ToLowerInvariant();
                    var existing = await context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
                    if (existing == null)
                    {
                        var hasher = passwordHasher ?? new PasswordHasher();
                        var admin = new User
                        {
                            Id = Guid.NewGuid(),
                            Email = normalizedEmail,
                            FullName = "System Admin",
                            PasswordHash = hasher.HashPassword(seedPassword),
                            Status = UserStatus.Active,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        admin.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
                        context.Users.Add(admin);
                        await context.SaveChangesAsync();
                        logger.LogInformation("Seeded initial Admin user from configuration.");
                    }
                    else
                    {
                        context.UserRoles.Add(new UserRole { UserId = existing.Id, RoleId = adminRole.Id });
                        existing.Status = UserStatus.Active;
                        await context.SaveChangesAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing Identity DB");
            throw;
        }
    }
}
