using Microsoft.EntityFrameworkCore;
using UserProfileService.Data;
using UserProfileService.DTOs;
using UserProfileService.Models;

namespace UserProfileService.Services;

public interface IProfileService
{
    Task<UserProfileDto?> GetProfileAsync(Guid userId);
    Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
    Task<string> UploadAvatarAsync(Guid userId, IFormFile file, string uploadsPath);
    Task<List<Currency>> GetActiveCurrenciesAsync();
    Task CreateOrUpdateDefaultProfileAsync(Guid userId, string fullName, string defaultCurrency);
}

public class ProfileService : IProfileService
{
    private readonly ProfileDbContext _context;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(ProfileDbContext context, ILogger<ProfileService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<UserProfileDto?> GetProfileAsync(Guid userId)
    {
        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile == null)
        {
            return null;
        }

        return new UserProfileDto
        {
            UserId = profile.UserId,
            FullName = profile.FullName,
            AvatarUrl = profile.AvatarUrl,
            DefaultCurrency = profile.DefaultCurrency,
            Language = profile.Language,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt
        };
    }

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile == null)
        {
            // Tự động khởi tạo nếu chưa có
            profile = new UserProfile
            {
                UserId = userId,
                FullName = request.FullName.Trim(),
                DefaultCurrency = string.IsNullOrWhiteSpace(request.DefaultCurrency) ? "VND" : request.DefaultCurrency.Trim().ToUpperInvariant(),
                Language = string.IsNullOrWhiteSpace(request.Language) ? "vi" : request.Language.Trim().ToLowerInvariant(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.UserProfiles.Add(profile);
        }
        else
        {
            profile.FullName = request.FullName.Trim();
            if (!string.IsNullOrWhiteSpace(request.DefaultCurrency))
            {
                profile.DefaultCurrency = request.DefaultCurrency.Trim().ToUpperInvariant();
            }
            if (!string.IsNullOrWhiteSpace(request.Language))
            {
                profile.Language = request.Language.Trim().ToLowerInvariant();
            }
            profile.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return new UserProfileDto
        {
            UserId = profile.UserId,
            FullName = profile.FullName,
            AvatarUrl = profile.AvatarUrl,
            DefaultCurrency = profile.DefaultCurrency,
            Language = profile.Language,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt
        };
    }

    public async Task<string> UploadAvatarAsync(Guid userId, IFormFile file, string uploadsPath)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("Tệp tải lên không được rỗng");
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            throw new ArgumentException("Kích thước tệp không được vượt quá 5MB");
        }

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
        {
            throw new ArgumentException("Chỉ chấp nhận các tệp ảnh định dạng .jpg, .jpeg, .png");
        }

        Directory.CreateDirectory(uploadsPath);

        var fileName = $"{userId}_{DateTime.UtcNow.Ticks}{extension}";
        var filePath = Path.Combine(uploadsPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var avatarUrl = $"/uploads/avatars/{fileName}";

        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile != null)
        {
            profile.AvatarUrl = avatarUrl;
            profile.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return avatarUrl;
    }

    public async Task<List<Currency>> GetActiveCurrenciesAsync()
    {
        return await _context.Currencies.Where(c => c.IsActive).ToListAsync();
    }

    public async Task CreateOrUpdateDefaultProfileAsync(Guid userId, string fullName, string defaultCurrency)
    {
        var existing = await _context.UserProfiles.FindAsync(userId);
        if (existing == null)
        {
            var profile = new UserProfile
            {
                UserId = userId,
                FullName = string.IsNullOrWhiteSpace(fullName) ? "Người dùng" : fullName.Trim(),
                DefaultCurrency = string.IsNullOrWhiteSpace(defaultCurrency) ? "VND" : defaultCurrency.Trim().ToUpperInvariant(),
                Language = "vi",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.UserProfiles.Add(profile);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created default profile for UserId {UserId}", userId);
        }
    }
}
