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
    private readonly IdentityDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IEmailSender _emailSender;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<AuthController> _logger;
    private readonly string _frontendBaseUrl;
    private readonly TimeProvider _timeProvider;

    public AuthController(
        IdentityDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IEmailSender emailSender,
        ILogger<AuthController> logger,
        IEventPublisher? eventPublisher = null,
        IConfiguration? configuration = null,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _emailSender = emailSender;
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
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
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

        // Gửi email xác nhận
        var confirmLink = $"{_frontendBaseUrl}/confirm-email?token={rawToken}";
        var emailBody = $@"
            <h2>Chào mừng bạn đến với SmartWallet!</h2>
            <p>Vui lòng click vào liên kết bên dưới để kích hoạt tài khoản của bạn (liên kết có hiệu lực trong 24 giờ):</p>
            <p><a href='{confirmLink}' style='padding: 10px 20px; background-color: #2563eb; color: white; text-decoration: none; border-radius: 5px;'>Kích hoạt tài khoản</a></p>
            <p>Hoặc truy cập URL: {confirmLink}</p>";

        await _emailSender.SendEmailAsync(user.Email, "SmartWallet - Xác nhận đăng ký tài khoản", emailBody);

        // Publish UserRegisteredEvent lên RabbitMQ
        if (_eventPublisher != null)
        {
            try
            {
                var userRegisteredEvent = new UserRegisteredEvent
                {
                    UserId = user.Id,
                    Email = user.Email,
                    FullName = string.IsNullOrWhiteSpace(request.FullName) ? user.Email.Split('@')[0] : request.FullName.Trim(),
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
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // H3/D10: Xác thực mật khẩu để chống User Enumeration
        var isValidPassword = user != null && _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);

        if (!isValidPassword)
        {
            if (user != null)
            {
                // Nếu tài khoản đang trong thời gian bị khóa, không tăng FailedLoginAttempts, không đẩy lùi LockoutEnd (H-05)
                var isCurrentlyLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > now;
                if (!isCurrentlyLocked)
                {
                    // Nếu thời gian khóa cũ đã hết, reset
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

            // D10 / K1: Luôn trả 401 cùng body từng byte, không trả remainingAttempts, không tiết lộ user có tồn tại hay không
            return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác" });
        }

        // Mật khẩu đúng -> Kiểm tra các trạng thái tài khoản
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
                // Khóa đã hết hạn -> mở khóa
                user.LockoutEnd = null;
                user.FailedLoginAttempts = 0;
            }
        }

        if (user.Status == UserStatus.PendingConfirm)
        {
            return StatusCode(403, new { message = "Tài khoản chưa được kích hoạt. Vui lòng kiểm tra email để kích hoạt tài khoản." });
        }

        if (user.Status == UserStatus.Locked)
        {
            return StatusCode(423, new { message = "Tài khoản đã bị vô hiệu hóa bởi quản trị viên." });
        }

        // Đăng nhập thành công -> Reset số lần đăng nhập sai
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        await _context.SaveChangesAsync();

        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        if (roles.Count == 0)
        {
            _logger.LogWarning("User {UserId} has no assigned roles. Falling back to default 'User' role.", user.Id);
            roles.Add("User");
        }

        var accessToken = _tokenService.GenerateAccessToken(user, roles);
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
            .FirstOrDefaultAsync(r => r.Token == request.RefreshToken && !r.IsRevoked);

        if (token == null || token.ExpiresAt < DateTime.UtcNow || token.User.Status != UserStatus.Active)
        {
            return Unauthorized(new { message = "RefreshToken không hợp lệ hoặc đã hết hạn" });
        }

        // Thu hồi token cũ
        token.IsRevoked = true;

        var roles = token.User.UserRoles.Select(ur => ur.Role.Name).ToList();
        if (roles.Count == 0)
        {
            _logger.LogWarning("User {UserId} has no assigned roles on refresh. Falling back to default 'User' role.", token.User.Id);
            roles.Add("User");
        }

        var newAccessToken = _tokenService.GenerateAccessToken(token.User, roles);
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
                ExpiresAt = DateTime.UtcNow.AddMinutes(30), // Link token 30 phút theo D6
                CreatedAt = DateTime.UtcNow
            };

            _context.UserTokens.Add(userToken);
            await _context.SaveChangesAsync();

            var resetLink = $"{_frontendBaseUrl}/reset-password?token={rawToken}";
            var emailBody = $@"
                <h2>Yêu cầu đặt lại mật khẩu</h2>
                <p>Bạn vừa yêu cầu đặt lại mật khẩu cho tài khoản SmartWallet.</p>
                <p>Vui lòng click vào liên kết bên dưới để đặt mật khẩu mới (liên kết có hiệu lực trong 30 phút):</p>
                <p><a href='{resetLink}' style='padding: 10px 20px; background-color: #2563eb; color: white; text-decoration: none; border-radius: 5px;'>Đặt lại mật khẩu</a></p>
                <p>Hoặc truy cập URL: {resetLink}</p>
                <p>Nếu bạn không gửi yêu cầu này, vui lòng bỏ qua email.</p>";

            await _emailSender.SendEmailAsync(user.Email, "SmartWallet - Đặt lại mật khẩu", emailBody);
        }

        // Luôn trả về 200 để chống user enumeration
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

        // Thu hồi toàn bộ RefreshToken cũ của user
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

        // Thu hồi toàn bộ RefreshToken cũ của user khi đổi mật khẩu (M1)
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
