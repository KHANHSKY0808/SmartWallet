using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UserProfileService.Data;
using UserProfileService.DTOs;
using UserProfileService.Models;
using UserProfileService.Services;

namespace UserProfileService.Tests;

public class ProfileServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ProfileDbContext _context;
    private readonly ProfileService _service;

    public ProfileServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ProfileDbContext(options);
        _context.Database.EnsureCreated();

        // Seed currencies
        _context.Currencies.AddRange(
            new Currency { Code = "VND", Name = "Việt Nam Đồng", Symbol = "₫", IsActive = true },
            new Currency { Code = "USD", Name = "Đô la Mỹ", Symbol = "$", IsActive = true }
        );
        _context.SaveChanges();

        _service = new ProfileService(_context, NullLogger<ProfileService>.Instance);
    }

    [Fact]
    public async Task GetProfileAsync_WhenProfileExists_ShouldReturnProfileDto()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _context.UserProfiles.Add(new UserProfile
        {
            UserId = userId,
            FullName = "Nguyễn Văn Test",
            DefaultCurrency = "VND",
            Language = "vi"
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetProfileAsync(userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("Nguyễn Văn Test", result.FullName);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenProfileExists_ShouldUpdateFields()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _context.UserProfiles.Add(new UserProfile
        {
            UserId = userId,
            FullName = "Tên Ban Đầu",
            DefaultCurrency = "VND",
            Language = "vi"
        });
        await _context.SaveChangesAsync();

        var updateRequest = new UpdateProfileRequest
        {
            FullName = "Tên Đã Cập Nhật",
            DefaultCurrency = "USD",
            Language = "en"
        };

        // Act
        var updated = await _service.UpdateProfileAsync(userId, updateRequest);

        // Assert
        Assert.Equal("Tên Đã Cập Nhật", updated.FullName);
        Assert.Equal("USD", updated.DefaultCurrency);
        Assert.Equal("en", updated.Language);

        var inDb = await _context.UserProfiles.FindAsync(userId);
        Assert.NotNull(inDb);
        Assert.Equal("Tên Đã Cập Nhật", inDb.FullName);
    }

    [Fact]
    public async Task P03_UpdateProfileAsync_WhenCurrencyIsUnknown_ShouldThrowArgumentException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var updateRequest = new UpdateProfileRequest
        {
            FullName = "QA Tester",
            DefaultCurrency = "XYZ"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateProfileAsync(userId, updateRequest));
        Assert.Contains("XYZ", ex.Message);
    }

    [Fact]
    public async Task CreateOrUpdateDefaultProfileAsync_WhenMissing_ShouldCreateProfile()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _service.CreateOrUpdateDefaultProfileAsync(userId, "Auto Created User", "VND");

        // Assert
        var created = await _context.UserProfiles.FindAsync(userId);
        Assert.NotNull(created);
        Assert.Equal("Auto Created User", created.FullName);
        Assert.Equal("VND", created.DefaultCurrency);
    }

    [Fact]
    public async Task GetActiveCurrenciesAsync_ShouldReturnActiveCurrencies()
    {
        // Act
        var currencies = await _service.GetActiveCurrenciesAsync();

        // Assert
        Assert.NotEmpty(currencies);
        Assert.Contains(currencies, c => c.Code == "VND");
        Assert.Contains(currencies, c => c.Code == "USD");
    }

    [Fact]
    public async Task H06_UploadAvatar_FilenameIsGuid_DoesNotContainUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tempDir = Path.Combine(Path.GetTempPath(), "qlct_test_avatars_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
            using var ms = new MemoryStream(pngBytes);
            var formFile = new Microsoft.AspNetCore.Http.FormFile(ms, 0, ms.Length, "file", "avatar.png");

            // Act
            var avatarUrl = await _service.UploadAvatarAsync(userId, formFile, tempDir);

            // Assert
            Assert.NotNull(avatarUrl);
            var fileName = Path.GetFileName(avatarUrl);
            Assert.DoesNotContain(userId.ToString(), fileName);
            Assert.Matches(@"^[0-9a-f]{32}\.(png|jpg|jpeg|webp)$", fileName);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task H07_UploadAvatar_Exceeds2MB_ThrowsArgumentException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tempDir = Path.Combine(Path.GetTempPath(), "qlct_test_avatars_" + Guid.NewGuid());

        var oversizedBytes = new byte[2 * 1024 * 1024 + 1];
        oversizedBytes[0] = 0x89; oversizedBytes[1] = 0x50; oversizedBytes[2] = 0x4E; oversizedBytes[3] = 0x47;
        using var ms = new MemoryStream(oversizedBytes);
        var formFile = new Microsoft.AspNetCore.Http.FormFile(ms, 0, ms.Length, "file", "big.png");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UploadAvatarAsync(userId, formFile, tempDir));
        Assert.Contains("2MB", ex.Message);
    }

    [Fact]
    public async Task H08_UploadAvatar_WebpValid_Succeeds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tempDir = Path.Combine(Path.GetTempPath(), "qlct_test_avatars_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            // RIFF....WEBP
            var webpBytes = new byte[] {
                0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50
            };
            using var ms = new MemoryStream(webpBytes);
            var formFile = new Microsoft.AspNetCore.Http.FormFile(ms, 0, ms.Length, "file", "photo.webp");

            // Act
            var avatarUrl = await _service.UploadAvatarAsync(userId, formFile, tempDir);

            // Assert
            Assert.NotNull(avatarUrl);
            Assert.EndsWith(".webp", avatarUrl);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task H09_UploadAvatar_InvalidMagicBytes_ThrowsArgumentException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tempDir = Path.Combine(Path.GetTempPath(), "qlct_test_avatars_" + Guid.NewGuid());

        var fakeBytes = System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        using var ms = new MemoryStream(fakeBytes);
        var formFile = new Microsoft.AspNetCore.Http.FormFile(ms, 0, ms.Length, "file", "evil.png");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UploadAvatarAsync(userId, formFile, tempDir));
        Assert.Contains("không hợp lệ", ex.Message);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}

