using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserProfileService.DTOs;
using UserProfileService.Services;

namespace UserProfileService.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        IProfileService profileService,
        IWebHostEnvironment environment,
        ILogger<ProfileController> logger)
    {
        _profileService = profileService;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized(new { message = "Không thể xác thực danh tính người dùng" });
        }

        var profile = await _profileService.GetProfileAsync(userId);
        if (profile == null)
        {
            // Tự động khởi tạo hồ sơ mặc định nếu chưa có
            var defaultRequest = new UpdateProfileRequest
            {
                FullName = User.FindFirstValue(ClaimTypes.Email)?.Split('@')[0] ?? "Người dùng",
                DefaultCurrency = "VND",
                Language = "vi"
            };
            profile = await _profileService.UpdateProfileAsync(userId, defaultRequest);
        }

        return Ok(profile);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized(new { message = "Không thể xác thực danh tính người dùng" });
        }

        try
        {
            var updated = await _profileService.UpdateProfileAsync(userId, request);
            return Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Vui lòng chọn tệp ảnh để tải lên" });
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized(new { message = "Không thể xác thực danh tính người dùng" });
        }

        try
        {
            var contentRoot = _environment.ContentRootPath;
            var uploadsPath = Path.Combine(contentRoot, "uploads", "avatars");

            var avatarUrl = await _profileService.UploadAvatarAsync(userId, file, uploadsPath);
            return Ok(new AvatarUploadResponse { AvatarUrl = avatarUrl });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải ảnh đại diện");
            return StatusCode(500, new { message = "Đã xảy ra lỗi khi lưu tệp ảnh" });
        }
    }

    [AllowAnonymous]
    [HttpGet("currencies")]
    public async Task<IActionResult> GetCurrencies()
    {
        var currencies = await _profileService.GetActiveCurrenciesAsync();
        return Ok(currencies);
    }
}

