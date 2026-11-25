using System.Security.Claims;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;

namespace IdentityService.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly string DummyPasswordHash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

    private readonly IdentityDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<AuthController> _logger;
    private readonly string _frontendBaseUrl;
    private readonly TimeProvider _timeProvider;

    public AuthController(
        IdentityDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ILogger<AuthController> logger,
        IEventPublisher? eventPublisher = null,
        IConfiguration? configuration = null,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _logger = logger;
        _eventPublisher = eventPublisher;
        _frontendBaseUrl = (configuration?["App:FrontendBaseUrl"] ?? "http://localhost:4200").TrimEnd('/');
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail);
        if (existingUser)
        {
            return Conflict(new { message = "Email đã được sử dụng" });
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var fullName = string.IsNullOrWhiteSpace(request.FullName)
            ? normalizedEmail.Split('@')[0]
            : request.FullName.Trim();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            FullName = fullName,
            PasswordHash = passwordHash,
            Status = UserStatus.PendingConfirm,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Gán Role mặc định: User (Id = 2)
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = 2 });

        // Tạo token kích hoạt email 24h
        var rawToken = _tokenService.GenerateRawToken();
        var tokenHash = _tokenService.HashToken(rawToken);

        var userToken = new UserToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Type = UserTokenType.EmailConfirm,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.UserTokens.Add(userToken);
        await _context.SaveChangesAsync();

        // D62: IdentityService không gửi SMTP trực tiếp; chỉ phát EmailRequestedEvent sang NotificationService
        var confirmLink = $"{_frontendBaseUrl}/confirm-email?token={rawToken}";
        _logger.LogInformation("Prepared ConfirmEmail event for UserId {UserId} with link base {BaseUrl}/confirm-email?token=[REDACTED]",
            user.Id, _frontendBaseUrl);

        if (_eventPublisher != null)
        {
            try
            {
                await _eventPublisher.PublishAsync(new EmailRequestedEvent
                {
                    UserId = user.Id,
                    To = user.Email,
                    Template = "ConfirmEmail",
                    Data = new Dictionary<string, string>
                    {
                        ["confirmLink"] = confirmLink,
                        ["fullName"] = fullName
                    }
                }, "identity.email_requested");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish EmailRequestedEvent for UserId {UserId}", user.Id);
            }

            try
            {
                var userRegisteredEvent = new UserRegisteredEvent
                {
                    UserId = user.Id,
                    Email = user.Email,
                    FullName = fullName,
                    DefaultCurrency = "VND"
                };
                await _eventPublisher.PublishAsync(userRegisteredEvent, "user.registered");
                _logger.LogInformation("Published UserRegisteredEvent for UserId {UserId}", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish UserRegisteredEvent to RabbitMQ broker");
            }
        }

        return StatusCode(201, new
        {
            message = "Đăng ký thành công! Vui lòng kiểm tra email để kích hoạt tài khoản.",
            userId = user.Id
        });
    }

    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { message = "Mã xác nhận không được để trống" });
        }

        var tokenHash = _tokenService.HashToken(token.Trim());
        var userToken = await _context.UserTokens
            .Include(ut => ut.User)
            .FirstOrDefaultAsync(ut => ut.TokenHash == tokenHash && ut.Type == UserTokenType.EmailConfirm);

        if (userToken == null || userToken.UsedAt != null || userToken.ExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new { message = "Mã xác nhận không hợp lệ hoặc đã hết hạn" });
        }

        userToken.UsedAt = DateTime.UtcNow;
        userToken.User.Status = UserStatus.Active;
        userToken.User.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Kích hoạt tài khoản thành công! Bạn có thể đăng nhập ngay bây giờ." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .ThenInclude(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var passwordHashToVerify = user?.PasswordHash ?? DummyPasswordHash;
        var isPasswordValid = _passwordHasher.VerifyPassword(request.Password, passwordHashToVerify);
        var isValidPassword = user != null && isPasswordValid;

        if (!isValidPassword)
        {
            if (user != null)
            {
                var isCurrentlyLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > now;
                if (!isCurrentlyLocked)
                {
                    if (user.LockoutEnd.HasValue && user.LockoutEnd.Value <= now)
                    {
                        user.LockoutEnd = null;
                        user.FailedLoginAttempts = 0;
                    }

                    user.FailedLoginAttempts++;
                    if (user.FailedLoginAttempts >= 5)
                    {
                        user.LockoutEnd = now.AddMinutes(15);
                        user.FailedLoginAttempts = 0;
                    }

                    await _context.SaveChangesAsync();
                }
            }

            return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác" });
        }

        if (user!.LockoutEnd.HasValue)
        {
            if (user.LockoutEnd.Value > now)
            {
                var remainingMinutes = Math.Ceiling((user.LockoutEnd.Value - now).TotalMinutes);
                return StatusCode(423, new
                {
                    message = $"Tài khoản bị khóa 15 phút do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau {remainingMinutes} phút.",
                    lockoutEnd = user.LockoutEnd
                });
            }
            else
            {
                user.LockoutEnd = null;
                user.FailedLoginAttempts = 0;
            }
        }

        if (user.Status == UserStatus.PendingConfirm)
        {
            return StatusCode(403, new { message = "Tài khoản chưa được kích hoạt. Vui lòng kiểm tra email để kích hoạt tài khoản." });
        }

        // D64 / AD-04: Bị Admin khóa với đúng mật khẩu trả 403 ACCOUNT_DISABLED
        if (user.Status == UserStatus.Locked)
        {
            return StatusCode(403, new
            {
                code = "ACCOUNT_DISABLED",
                message = "Tài khoản đã bị vô hiệu hóa bởi quản trị viên."
            });
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        await _context.SaveChangesAsync();

        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        if (roles.Count == 0)
        {
            _logger.LogWarning("User {UserId} has no assigned roles. Falling back to default 'User' role.", user.Id);
            roles.Add("User");
        }

        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var accessToken = _tokenService.GenerateAccessToken(user, roles, permissions);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresIn = 1800,
            User = new UserInfoDto
            {
                Id = user.Id,
                Email = user.Email,
                Roles = roles
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var token = await _context.RefreshTokens
            .Include(r => r.User)
            .ThenInclude(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .ThenInclude(ro => ro.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Token == request.RefreshToken && !r.IsRevoked);

        if (token == null || token.ExpiresAt < DateTime.UtcNow || token.User.Status != UserStatus.Active)
        {
            return Unauthorized(new { message = "RefreshToken không hợp lệ hoặc đã hết hạn" });
        }

        token.IsRevoked = true;

        var roles = token.User.UserRoles.Select(ur => ur.Role.Name).ToList();
        if (roles.Count == 0)
        {
            _logger.LogWarning("User {UserId} has no assigned roles on refresh. Falling back to default 'User' role.", token.User.Id);
            roles.Add("User");
        }

        var permissions = token.User.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var newAccessToken = _tokenService.GenerateAccessToken(token.User, roles, permissions);
        var newRefreshToken = _tokenService.GenerateRefreshToken(token.User.Id);

        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync();

        return Ok(new AuthResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresIn = 1800,
            User = new UserInfoDto
            {
                Id = token.User.Id,
                Email = token.User.Email,
                Roles = roles
            }
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var token = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == request.RefreshToken);
        if (token != null)
        {
            token.IsRevoked = true;
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Đăng xuất thành công" });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        if (user != null && user.Status == UserStatus.Active)
        {
            var rawToken = _tokenService.GenerateRawToken();
            var tokenHash = _tokenService.HashToken(rawToken);

            var userToken = new UserToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Type = UserTokenType.PasswordReset,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                CreatedAt = DateTime.UtcNow
            };

            _context.UserTokens.Add(userToken);
            await _context.SaveChangesAsync();

            var resetLink = $"{_frontendBaseUrl}/reset-password?token={rawToken}";
            if (_eventPublisher != null)
            {
                try
                {
                    await _eventPublisher.PublishAsync(new EmailRequestedEvent
                    {
                        UserId = user.Id,
                        To = user.Email,
                        Template = "ResetPassword",
                        Data = new Dictionary<string, string>
                        {
                            ["resetLink"] = resetLink
                        }
                    }, "identity.email_requested");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish ResetPassword EmailRequestedEvent for UserId {UserId}", user.Id);
                }
            }
        }

        return Ok(new { message = "Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var tokenHash = _tokenService.HashToken(request.Token.Trim());
        var userToken = await _context.UserTokens
            .Include(ut => ut.User)
            .FirstOrDefaultAsync(ut => ut.TokenHash == tokenHash && ut.Type == UserTokenType.PasswordReset);

        if (userToken == null || userToken.UsedAt != null || userToken.ExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new { message = "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn" });
        }

        userToken.UsedAt = DateTime.UtcNow;
        userToken.User.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        userToken.User.UpdatedAt = DateTime.UtcNow;

        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == userToken.UserId && !r.IsRevoked)
            .ToListAsync();

        foreach (var t in activeTokens)
        {
            t.IsRevoked = true;
        }

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đặt lại mật khẩu thành công! Vui lòng đăng nhập bằng mật khẩu mới." });
    }

    [Authorize]
    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized(new { message = "Không xác định được danh tính người dùng" });
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng" });
        }

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            return BadRequest(new { message = "Mật khẩu hiện tại không chính xác" });
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && !r.IsRevoked)
            .ToListAsync();

        foreach (var t in activeTokens)
        {
            t.IsRevoked = true;
        }

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đổi mật khẩu thành công" });
    }
}
