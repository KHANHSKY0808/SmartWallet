using IdentityService.Controllers;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;

namespace IdentityService.Tests;

public class AuthControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IdentityDbContext _context;
    private readonly PasswordHasher _passwordHasher;
    private readonly TokenService _tokenService;
    private readonly TestEventPublisher _eventPublisher;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new IdentityDbContext(options);
        _context.Database.EnsureCreated();

        // Seed Roles
        _context.Roles.AddRange(
            new Role { Id = 1, Name = "Admin", Description = "Quản trị viên" },
            new Role { Id = 2, Name = "User", Description = "Người dùng tiêu chuẩn" }
        );
        _context.SaveChanges();

        _passwordHasher = new PasswordHasher();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:SigningKey", "UnitTestingSuperSecretSigningKeyForIdentityServiceTests987654321!" }
            })
            .Build();

        _tokenService = new TokenService(config);
        _eventPublisher = new TestEventPublisher();

        _controller = new AuthController(
            _context,
            _passwordHasher,
            _tokenService,
            NullLogger<AuthController>.Instance,
            _eventPublisher
        );
    }

    [Fact]
    public async Task Register_ShouldCreatePendingUser_AndSendEmail()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "newuser@example.com",
            Password = "Password123@",
            ConfirmPassword = "Password123@",
            FullName = "New User"
        };

        // Act
        var result = await _controller.Register(request);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, objectResult.StatusCode);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == "newuser@example.com");
        Assert.NotNull(user);
        Assert.Equal(UserStatus.PendingConfirm, user.Status);
        var emailEvent = _eventPublisher.PublishedEvents.OfType<EmailRequestedEvent>().SingleOrDefault();
        Assert.NotNull(emailEvent);
        Assert.Equal("ConfirmEmail", emailEvent.Template);
        Assert.Equal("newuser@example.com", emailEvent.To);
        Assert.True(emailEvent.Data.ContainsKey("confirmLink"));

        var token = await _context.UserTokens.FirstOrDefaultAsync(t => t.UserId == user.Id);
        Assert.NotNull(token);
        Assert.Equal(UserTokenType.EmailConfirm, token.Type);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ShouldReturn409Conflict()
    {
        // Arrange
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "duplicate@example.com",
            PasswordHash = _passwordHasher.HashPassword("Pass123!"),
            Status = UserStatus.Active
        };
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var request = new RegisterRequest
        {
            Email = "duplicate@example.com",
            Password = "Password123@",
            ConfirmPassword = "Password123@"
        };

        // Act
        var result = await _controller.Register(request);

        // Assert
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task ConfirmEmail_ValidToken_ShouldActivateUser()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "confirm@example.com",
            PasswordHash = _passwordHasher.HashPassword("Pass123!"),
            Status = UserStatus.PendingConfirm
        };
        var rawToken = "my-secret-activation-token";
        var tokenHash = _tokenService.HashToken(rawToken);

        var userToken = new UserToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Type = UserTokenType.EmailConfirm,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _context.Users.Add(user);
        _context.UserTokens.Add(userToken);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.ConfirmEmail(rawToken);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.Equal(UserStatus.Active, updatedUser.Status);
    }

    [Fact]
    public async Task Login_ValidCredentials_ShouldReturnAuthResponse()
    {
        // Arrange
        var password = "ValidPassword123@";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "loginuser@example.com",
            PasswordHash = _passwordHasher.HashPassword(password),
            Status = UserStatus.Active
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = 2 });

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var request = new LoginRequest
        {
            Email = "loginuser@example.com",
            Password = password
        };

        // Act
        var result = await _controller.Login(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var authResponse = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.NotNull(authResponse.AccessToken);
        Assert.NotNull(authResponse.RefreshToken);
        Assert.Equal(user.Email, authResponse.User.Email);
    }

    [Fact]
    public async Task Login_5FailedAttempts_ShouldLockAccountFor15Minutes()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "lockout@example.com",
            PasswordHash = _passwordHasher.HashPassword("CorrectPass123"),
            Status = UserStatus.Active
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var badRequest = new LoginRequest
        {
            Email = "lockout@example.com",
            Password = "WrongPassword"
        };

        // Act - 4 failed attempts
        for (int i = 0; i < 4; i++)
        {
            var res = await _controller.Login(badRequest);
            Assert.IsType<UnauthorizedObjectResult>(res);
        }

        // 5th failed attempt: D10 specifies returning 401 Unauthorized (not 423), but account is locked in DB
        var fifthResult = await _controller.Login(badRequest);
        var unauthResult = Assert.IsType<UnauthorizedObjectResult>(fifthResult);
        Assert.Equal(401, unauthResult.StatusCode);

        var lockedUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(lockedUser);
        Assert.NotNull(lockedUser.LockoutEnd);
        Assert.True(lockedUser.LockoutEnd > DateTime.UtcNow);
    }

    [Fact]
    public async Task H01_Login_NonExistentAndExistentUsers_WrongPassword_ReturnIdentical401ByteForByte()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "y@a.com",
            PasswordHash = _passwordHasher.HashPassword("RealPassword123!"),
            Status = UserStatus.Active
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var nonExistentReq = new LoginRequest { Email = "x@a.com", Password = "AnyPassword123!" };
        var existentReq = new LoginRequest { Email = "y@a.com", Password = "WrongPassword123!" };

        // Act
        var res1 = await _controller.Login(nonExistentReq) as UnauthorizedObjectResult;
        var res2 = await _controller.Login(existentReq) as UnauthorizedObjectResult;

        // Assert
        Assert.NotNull(res1);
        Assert.NotNull(res2);
        Assert.Equal(401, res1.StatusCode);
        Assert.Equal(401, res2.StatusCode);

        var json1 = System.Text.Json.JsonSerializer.Serialize(res1.Value);
        var json2 = System.Text.Json.JsonSerializer.Serialize(res2.Value);
        Assert.Equal(json1, json2);
        Assert.DoesNotContain("remainingAttempts", json1);
    }

    [Fact]
    public async Task H02_Login_SixthFailedAttempt_Returns401WithSameBody()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "lockout_h02@example.com",
            PasswordHash = _passwordHasher.HashPassword("CorrectPass123!"),
            Status = UserStatus.Active
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var badRequest = new LoginRequest { Email = "lockout_h02@example.com", Password = "WrongPassword" };
        var nonExistentReq = new LoginRequest { Email = "random@example.com", Password = "WrongPassword" };

        var baselineRes = await _controller.Login(nonExistentReq) as UnauthorizedObjectResult;
        var baselineJson = System.Text.Json.JsonSerializer.Serialize(baselineRes!.Value);

        // 5 failed attempts
        for (int i = 0; i < 5; i++)
        {
            await _controller.Login(badRequest);
        }

        // 6th failed attempt
        var sixthRes = await _controller.Login(badRequest) as UnauthorizedObjectResult;
        Assert.NotNull(sixthRes);
        Assert.Equal(401, sixthRes.StatusCode);
        var sixthJson = System.Text.Json.JsonSerializer.Serialize(sixthRes.Value);
        Assert.Equal(baselineJson, sixthJson);
    }

    [Fact]
    public async Task H03_Login_SixthAttemptCorrectPasswordWhileLocked_Returns423()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "lockout_h03@example.com",
            PasswordHash = _passwordHasher.HashPassword("CorrectPass123!"),
            Status = UserStatus.Active
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var badRequest = new LoginRequest { Email = "lockout_h03@example.com", Password = "WrongPassword" };
        for (int i = 0; i < 5; i++)
        {
            await _controller.Login(badRequest);
        }

        // 6th attempt with correct password within 15 minutes
        var correctRequest = new LoginRequest { Email = "lockout_h03@example.com", Password = "CorrectPass123!" };
        var res = await _controller.Login(correctRequest) as ObjectResult;

        Assert.NotNull(res);
        Assert.Equal(423, res.StatusCode);
    }

    [Fact]
    public async Task H04_Login_CorrectPasswordAfter15Minutes_Returns200AndResetsCounter()
    {
        // Arrange with TestTimeProvider
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var controllerWithClock = new AuthController(
            _context,
            _passwordHasher,
            _tokenService,
            NullLogger<AuthController>.Instance,
            _eventPublisher,
            timeProvider: timeProvider
        );

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "lockout_h04@example.com",
            PasswordHash = _passwordHasher.HashPassword("CorrectPass123!"),
            Status = UserStatus.Active
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = 2 });
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var badRequest = new LoginRequest { Email = "lockout_h04@example.com", Password = "WrongPassword" };
        for (int i = 0; i < 5; i++)
        {
            await controllerWithClock.Login(badRequest);
        }

        // Advance clock by 16 minutes
        timeProvider.Advance(TimeSpan.FromMinutes(16));

        var correctRequest = new LoginRequest { Email = "lockout_h04@example.com", Password = "CorrectPass123!" };
        var res = await controllerWithClock.Login(correctRequest) as OkObjectResult;

        Assert.NotNull(res);
        Assert.Equal(200, res.StatusCode);

        var refreshedUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(refreshedUser);
        Assert.Equal(0, refreshedUser.FailedLoginAttempts);
        Assert.Null(refreshedUser.LockoutEnd);
    }

    [Fact]
    public async Task H05_Login_RepeatedFailedAttemptsWhileLocked_DoesNotIncreaseCounterOrExtendLockout()
    {
        // Arrange
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var controllerWithClock = new AuthController(
            _context,
            _passwordHasher,
            _tokenService,
            NullLogger<AuthController>.Instance,
            _eventPublisher,
            timeProvider: timeProvider
        );

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "lockout_h05@example.com",
            PasswordHash = _passwordHasher.HashPassword("CorrectPass123!"),
            Status = UserStatus.Active
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var badRequest = new LoginRequest { Email = "lockout_h05@example.com", Password = "WrongPassword" };
        for (int i = 0; i < 5; i++)
        {
            await controllerWithClock.Login(badRequest);
        }

        var lockedUser = await _context.Users.FindAsync(user.Id);
        var initialLockoutEnd = lockedUser!.LockoutEnd;

        // Advance 5 minutes, still locked
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Submit 3 more failed attempts while locked
        for (int i = 0; i < 3; i++)
        {
            var res = await controllerWithClock.Login(badRequest);
            Assert.IsType<UnauthorizedObjectResult>(res);
        }

        var recheckUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(recheckUser);
        Assert.Equal(0, recheckUser.FailedLoginAttempts);
        Assert.Equal(initialLockoutEnd, recheckUser.LockoutEnd);
    }

    [Fact]
    public async Task RefreshToken_ValidToken_ShouldIssueNewTokensAndRevokeOld()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "refresher@example.com",
            PasswordHash = "hashed",
            Status = UserStatus.Active
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = 2 });

        var oldRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "existing-valid-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        };

        _context.Users.Add(user);
        _context.RefreshTokens.Add(oldRefreshToken);
        await _context.SaveChangesAsync();

        var request = new RefreshTokenRequest { RefreshToken = "existing-valid-refresh-token" };

        // Act
        var result = await _controller.RefreshToken(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var authResponse = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.NotNull(authResponse.AccessToken);
        Assert.NotEqual("existing-valid-refresh-token", authResponse.RefreshToken);

        var revokedOld = await _context.RefreshTokens.FindAsync(oldRefreshToken.Id);
        Assert.NotNull(revokedOld);
        Assert.True(revokedOld.IsRevoked);
    }

    [Fact]
    public async Task Login_PendingConfirmUser_WithWrongPassword_ShouldReturn401Unauthorized_ToPreventEnumeration()
    {
        // Arrange - User chưa kích hoạt email
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "pending@example.com",
            PasswordHash = _passwordHasher.HashPassword("RealSecretPassword123"),
            Status = UserStatus.PendingConfirm
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var request = new LoginRequest
        {
            Email = "pending@example.com",
            Password = "WrongPasswordGuess"
        };

        // Act
        var result = await _controller.Login(request);

        // Assert - Phải trả về 401 chứ không được trả 403 để chống Enumeration (H3)
        var unauthResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthResult.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ShouldRevokeAllExistingRefreshTokens()
    {
        // Arrange
        var currentPassword = "CurrentPassword123@";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "changepass@example.com",
            PasswordHash = _passwordHasher.HashPassword(currentPassword),
            Status = UserStatus.Active
        };

        var token1 = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "token-1",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        };
        var token2 = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = "token-2",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        };

        _context.Users.Add(user);
        _context.RefreshTokens.AddRange(token1, token2);
        await _context.SaveChangesAsync();

        // Giả lập ClaimsPrincipal cho user đã đăng nhập
        var claims = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()) },
                "TestAuth"
            )
        );
        _controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = claims } };

        var request = new ChangePasswordRequest
        {
            CurrentPassword = currentPassword,
            NewPassword = "NewStrongPassword456@",
            ConfirmPassword = "NewStrongPassword456@"
        };

        // Act
        var result = await _controller.ChangePassword(request);

        // Assert (M1)
        Assert.IsType<OkObjectResult>(result);
        var updatedTokens = await _context.RefreshTokens.Where(r => r.UserId == user.Id).ToListAsync();
        Assert.All(updatedTokens, t => Assert.True(t.IsRevoked));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}

public class TestEventPublisher : IEventPublisher
{
    public List<IntegrationEvent> PublishedEvents { get; } = new();

    public Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        PublishedEvents.Add(@event);
        return Task.CompletedTask;
    }
}

public class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    public TestTimeProvider(DateTimeOffset initialUtcNow) => _utcNow = initialUtcNow;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void Advance(TimeSpan timeSpan) => _utcNow = _utcNow.Add(timeSpan);
}

