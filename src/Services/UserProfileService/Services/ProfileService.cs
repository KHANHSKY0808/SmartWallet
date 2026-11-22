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

        if (file.Length > 2 * 1024 * 1024)
        {
            throw new ArgumentException("Kích thước tệp không được vượt quá 2MB");
        }

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
        {
            throw new ArgumentException("Chỉ chấp nhận các tệp ảnh định dạng .jpg, .jpeg, .png, .webp");
        }

        // Kiểm tra magic bytes thực tế của tệp (chống giả mạo đuôi tệp - H-09)
        using (var stream = file.OpenReadStream())
        {
            if (!IsValidImageMagicBytes(stream, extension))
            {
                throw new ArgumentException("Nội dung tệp không hợp lệ hoặc không đúng định dạng ảnh");
            }
        }

        Directory.CreateDirectory(uploadsPath);

        // D14/K6/H-06: Tên file là GUID ngẫu nhiên không chứa UserId, khớp regex ^[0-9a-f]{32}\.(png|jpg|jpeg|webp)$
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadsPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var avatarUrl = $"/uploads/avatars/{fileName}";

        var profile = await _context.UserProfiles.FindAsync(userId);
        if (profile != null)
        {
            // Xóa file avatar cũ nếu tồn tại (M9)
            if (!string.IsNullOrEmpty(profile.AvatarUrl))
            {
                var oldFileName = Path.GetFileName(profile.AvatarUrl);
                var oldFilePath = Path.Combine(uploadsPath, oldFileName);
                if (File.Exists(oldFilePath))
                {
                    try
                    {
                        File.Delete(oldFilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not delete old avatar file {OldFilePath}", oldFilePath);
                    }
                }
            }

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

    private static bool IsValidImageMagicBytes(Stream stream, string extension)
    {
        if (stream.Length < 4) return false;
        var header = new byte[12];
        var read = stream.Read(header, 0, header.Length);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        if (read < 4) return false;

        return extension switch
        {
            ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
            ".webp" => read >= 12 &&
                       header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && // RIFF
                       header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50, // WEBP
            _ => false
        };
    }
}

