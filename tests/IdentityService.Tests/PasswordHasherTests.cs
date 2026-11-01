using IdentityService.Services;

namespace IdentityService.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ShouldGenerateValidBCryptHash()
    {
        // Arrange
        const string rawPassword = "TestPassword@123";

        // Act
        var hash = _hasher.HashPassword(rawPassword);

        // Assert
        Assert.NotNull(hash);
        Assert.NotEqual(rawPassword, hash);
        Assert.StartsWith("$2", hash); // BCrypt prefix
    }

    [Fact]
    public void VerifyPassword_ShouldReturnTrue_WhenPasswordMatches()
    {
        // Arrange
        const string rawPassword = "SecurePassword#999";
        var hash = _hasher.HashPassword(rawPassword);

        // Act
        var isValid = _hasher.VerifyPassword(rawPassword, hash);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void VerifyPassword_ShouldReturnFalse_WhenPasswordDoesNotMatch()
    {
        // Arrange
        var hash = _hasher.HashPassword("CorrectPassword123");

        // Act
        var isValid = _hasher.VerifyPassword("WrongPassword123", hash);

        // Assert
        Assert.False(isValid);
    }
}

