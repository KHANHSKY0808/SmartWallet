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

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}

