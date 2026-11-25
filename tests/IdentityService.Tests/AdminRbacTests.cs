using System.Security.Claims;
using System.Text.Json;
using IdentityService.Controllers;
using IdentityService.Data;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Extensions;

namespace IdentityService.Tests;

public class AdminRbacTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IdentityDbContext _context;
    private readonly PasswordHasher _hasher = new();

    public AdminRbacTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var opts = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(_connection).Options;
        _context = new IdentityDbContext(opts);
    }

    [Fact]
    public async Task Test_D66_AD04_AdminSeedAndLockedLoginReturns403AccountDisabled()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:Admin:Email"] = "seedadmin@qa.local",
                ["Seed:Admin:Password"] = "QaAdmin@12345"
            })
            .Build();

        await DbInitializer.InitializeAsync(_context, NullLogger.Instance, cfg, _hasher);

        var admin = await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == "seedadmin@qa.local");
        Assert.NotNull(admin);
        Assert.Equal(UserStatus.Active, admin.Status);
        Assert.Contains(admin.UserRoles, ur => ur.Role.Name == "Admin");

        // Lock a normal user and verify login contract D64 (403 ACCOUNT_DISABLED on valid pass, 401 on wrong pass)
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "locked@qa.local",
            PasswordHash = _hasher.HashPassword("RightPass@123"),
            Status = UserStatus.Locked
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = 2 });
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var tokenService = new TokenService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "SuperSecretUnitTestKeyAtLeast32BytesLong!"
        }).Build());

        var authCtrl = new AuthController(_context, _hasher, tokenService, NullLogger<AuthController>.Instance);

        var validRes = Assert.IsType<ObjectResult>(await authCtrl.Login(new DTOs.LoginRequest { Email = "locked@qa.local", Password = "RightPass@123" }));
        Assert.Equal(403, validRes.StatusCode);
        Assert.Contains("ACCOUNT_DISABLED", JsonSerializer.Serialize(validRes.Value));

        var wrongRes = await authCtrl.Login(new DTOs.LoginRequest { Email = "locked@qa.local", Password = "Wrong" });
        Assert.IsType<UnauthorizedObjectResult>(wrongRes);
    }

    [Fact]
    public async Task Test_AD06_RB03_RB04_CannotLockSelfOrLastAdminAndRejectsUnknownRole()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:Admin:Email"] = "onlyadmin@qa.local",
                ["Seed:Admin:Password"] = "QaAdmin@12345"
            })
            .Build();
        await DbInitializer.InitializeAsync(_context, NullLogger.Instance, cfg, _hasher);
        var admin = await _context.Users.FirstAsync(u => u.Email == "onlyadmin@qa.local");

        var rev = new FakeRevocationStore();
        var ctrl = new AdminUsersController(_context, rev, NullLogger<AdminUsersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                        new Claim(ClaimTypes.Role, "Admin")
                    ], "test"))
                }
            }
        };

        var selfLock = Assert.IsType<ConflictObjectResult>(await ctrl.LockUser(admin.Id, new LockUserRequest("test")));
        Assert.Contains("CANNOT_LOCK_SELF", JsonSerializer.Serialize(selfLock.Value));

        // Another admin tries to lock the only real admin
        ctrl.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ], "test"));
        var lastAdminLock = Assert.IsType<ConflictObjectResult>(await ctrl.LockUser(admin.Id, new LockUserRequest("test")));
        Assert.Contains("LAST_ADMIN", JsonSerializer.Serialize(lastAdminLock.Value));

        var lastAdminRole = Assert.IsType<ConflictObjectResult>(await ctrl.UpdateUserRoles(admin.Id, new UpdateUserRolesRequest(["User"])));
        Assert.Contains("LAST_ADMIN", JsonSerializer.Serialize(lastAdminRole.Value));

        var invalidRole = await ctrl.UpdateUserRoles(admin.Id, new UpdateUserRolesRequest(["Root"]));
        Assert.IsType<BadRequestObjectResult>(invalidRole);
    }

    [Fact]
    public async Task Test_RB06_InternalEndpoints_RequireSystemOnlyPolicy_ClosingB5A2()
    {
        await Task.CompletedTask;
        var controllersAndActions = new (Type Ctrl, string Method)[]
        {
            (typeof(global::CategoryService.Controllers.CategoriesController), "GetInternalCategory"),
            (typeof(global::WalletService.Controllers.WalletsController), "GetInternalWallet"),
            (typeof(global::UserProfileService.Controllers.ExchangeRatesController), "GetInternalExchangeRate"),
            (typeof(global::FileService.Controllers.FilesController), "GetInternalFileMetadata"),
            (typeof(global::TransactionService.Controllers.InternalTransactionsController), "GetAllTransactions")
        };

        foreach (var (ctrl, methodName) in controllersAndActions)
        {
            var attrs = ctrl.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Concat(ctrl.GetMethod(methodName)!.GetCustomAttributes(typeof(AuthorizeAttribute), true))
                .Cast<AuthorizeAttribute>()
                .ToList();
            Assert.Contains(attrs, a => a.Policy == "SystemOnly");
        }
    }

    [Fact]
    public async Task Test_RB08_PermissionsMatrixAndPolicyChecks()
    {
        await DbInitializer.InitializeAsync(_context, NullLogger.Instance);
        var supportRole = await _context.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstAsync(r => r.Name == "Support");
        var supportPerms = supportRole.RolePermissions.Select(rp => rp.Permission.Code).ToHashSet();

        Assert.Equal(new HashSet<string> { AppPermissions.UsersRead, AppPermissions.AuditRead }, supportPerms);

        // Verify policy evaluation per D65
        var supportPrincipal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("role", "Support"),
            new Claim("perm", AppPermissions.UsersRead),
            new Claim("perm", AppPermissions.AuditRead)
        ], "jwt"));
        Assert.True(JwtAuthExtensions.HasPermission(supportPrincipal, AppPermissions.UsersRead));
        Assert.False(JwtAuthExtensions.HasPermission(supportPrincipal, AppPermissions.UsersLock));

        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Admin")], "jwt"));
        Assert.All(AppPermissions.All, p => Assert.True(JwtAuthExtensions.HasPermission(adminPrincipal, p)));

        // Verify exact policy attributes on admin controllers
        var usersAttr = typeof(AdminUsersController).GetMethod("GetUsers")!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single();
        Assert.Equal(AppPermissions.UsersRead, usersAttr.Policy);

        var lockAttr = typeof(AdminUsersController).GetMethod("LockUser")!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single();
        Assert.Equal(AppPermissions.UsersLock, lockAttr.Policy);

        var rolesWriteAttr = typeof(AdminUsersController).GetMethod("UpdateUserRoles")!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single();
        Assert.Equal(AppPermissions.UsersRolesWrite, rolesWriteAttr.Policy);

        var ratesAttr = typeof(global::UserProfileService.Controllers.ExchangeRatesController).GetMethod("UpdateExchangeRate")!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single();
        Assert.Equal(AppPermissions.RatesWrite, ratesAttr.Policy);

        var catDefAttr = typeof(global::CategoryService.Controllers.CategoryDefaultsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single();
        Assert.Equal(AppPermissions.CategoriesDefaultsWrite, catDefAttr.Policy);
    }

    [Fact]
    public void Test_EM02_IdentityService_HasNoSmtpClientOrChannel()
    {
        var assembly = typeof(AuthController).Assembly;
        var loggingSenderType = assembly.GetType("IdentityService.Services.LoggingEmailSender");
        var emailBgType = assembly.GetType("IdentityService.Services.EmailDispatchBackgroundService");
        Assert.Null(loggingSenderType);
        Assert.Null(emailBgType);

        var ctorParams = typeof(AuthController).GetConstructors()[0].GetParameters();
        Assert.DoesNotContain(ctorParams, p => p.ParameterType.Name.Contains("EmailSender", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class FakeRevocationStore : ISessionRevocationStore
    {
        public Task SetLockedAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearLockedAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetValidAfterNowAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

