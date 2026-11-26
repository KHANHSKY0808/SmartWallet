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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Extensions;
using SharedKernel.Messaging;

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

    [Fact]
    public async Task Test_D67_N44_RegisterPublishFailsLogsError_ThenResendConfirmationDeliversEmail()
    {
        await DbInitializer.InitializeAsync(_context, NullLogger.Instance);

        var publisher = new ToggleableEventPublisher { ShouldThrow = true };
        var captureLogger = new CapturingLogger<AuthController>();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        var limiter = new InMemoryResendConfirmationRateLimiter();
        var tokenService = new TokenService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "SuperSecretUnitTestKeyAtLeast32BytesLong!"
        }).Build());

        var ctrl = new AuthController(_context, _hasher, tokenService, captureLogger, publisher, null, clock, limiter);

        // 1. Register while broker is down -> 201 (X04), logs Error, PendingConfirm user created
        var regResult = Assert.IsType<ObjectResult>(await ctrl.Register(new DTOs.RegisterRequest
        {
            Email = "n44user@qa.local",
            Password = "Password@123",
            ConfirmPassword = "Password@123",
            FullName = "N44 User"
        }));
        Assert.Equal(201, regResult.StatusCode);
        Assert.Contains(captureLogger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("EmailRequestedEvent"));
        Assert.Empty(publisher.PublishedEvents);

        var firstToken = await _context.UserTokens.SingleAsync();
        Assert.Null(firstToken.UsedAt);

        // 2. Resend while broker still down -> 503 EVENT_PUBLISH_FAILED and rolls back
        var failResend = Assert.IsType<ObjectResult>(await ctrl.ResendConfirmation(new DTOs.ResendConfirmationRequest { Email = "n44user@qa.local" }));
        Assert.Equal(503, failResend.StatusCode);
        Assert.Contains("EVENT_PUBLISH_FAILED", JsonSerializer.Serialize(failResend.Value));

        // 3. Broker recovers -> Resend succeeds (200), invalidates old token, publishes EmailRequestedEvent
        publisher.ShouldThrow = false;
        var okResend1 = Assert.IsType<OkObjectResult>(await ctrl.ResendConfirmation(new DTOs.ResendConfirmationRequest { Email = "n44user@qa.local" }));
        Assert.Equal(200, okResend1.StatusCode);
        var emailEv = Assert.Single(publisher.PublishedEvents.OfType<EmailRequestedEvent>());
        Assert.Equal("n44user@qa.local", emailEv.To);
        Assert.Equal("ConfirmEmail", emailEv.Template);
        Assert.Contains("/confirm-email?token=", emailEv.Data["confirmLink"]);

        var oldTokenReloaded = await _context.UserTokens.FirstAsync(t => t.Id == firstToken.Id);
        Assert.NotNull(oldTokenReloaded.UsedAt);
        Assert.Equal(2, await _context.UserTokens.CountAsync());

        // 4. Rate limit: calling again within 60s returns same 200 message without publishing a duplicate event
        clock.Advance(TimeSpan.FromSeconds(30));
        var throttled = Assert.IsType<OkObjectResult>(await ctrl.ResendConfirmation(new DTOs.ResendConfirmationRequest { Email = "n44user@qa.local" }));
        Assert.Equal(JsonSerializer.Serialize(okResend1.Value), JsonSerializer.Serialize(throttled.Value));
        Assert.Single(publisher.PublishedEvents.OfType<EmailRequestedEvent>());

        // 5. After 60s -> allowed again
        clock.Advance(TimeSpan.FromSeconds(31));
        var okResend2 = Assert.IsType<OkObjectResult>(await ctrl.ResendConfirmation(new DTOs.ResendConfirmationRequest { Email = "n44user@qa.local" }));
        Assert.Equal(2, publisher.PublishedEvents.OfType<EmailRequestedEvent>().Count());

        // 6. Unknown email -> identical 200 body (no enumeration)
        var unknown = Assert.IsType<OkObjectResult>(await ctrl.ResendConfirmation(new DTOs.ResendConfirmationRequest { Email = "missing@qa.local" }));
        Assert.Equal(JsonSerializer.Serialize(okResend1.Value), JsonSerializer.Serialize(unknown.Value));
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

    private sealed class ToggleableEventPublisher : IEventPublisher
    {
        public bool ShouldThrow { get; set; }
        public List<IntegrationEvent> PublishedEvents { get; } = new();
        public Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default)
            where TEvent : IntegrationEvent
        {
            if (ShouldThrow) throw new InvalidOperationException("RabbitMQ down");
            PublishedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeClock(DateTimeOffset initial) => _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan ts) => _now = _now.Add(ts);
    }
}

