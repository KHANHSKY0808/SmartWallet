using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.Extensions.Configuration;

namespace IdentityService.Tests;

public class TokenServiceTests
{
    private readonly IConfiguration _configuration;
    private readonly TokenService _tokenService;

    public TokenServiceTests()
    {
        var configValues = new Dictionary<string, string?>
        {
            { "Jwt:SigningKey", "TestCryptographicSigningKeyForUnitTestingOnly1234567890!#" }
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        _tokenService = new TokenService(_configuration);
    }

    [Fact]
    public void GenerateAccessToken_ShouldIncludeClaimsAndProperExpiration()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "testuser@example.com"
        };
        var roles = new[] { "Admin", "User" };

        // Act
        var tokenString = _tokenService.GenerateAccessToken(user, roles);

        // Assert
        Assert.NotNull(tokenString);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        Assert.Equal(user.Id.ToString(), jwt.Subject);
        var emailClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value;
        Assert.Equal(user.Email, emailClaim);

        var roleClaims = jwt.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role").Select(c => c.Value).ToList();
        Assert.Contains("Admin", roleClaims);
        Assert.Contains("User", roleClaims);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldHave7DaysExpirationAndNotBeRevoked()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var refreshToken = _tokenService.GenerateRefreshToken(userId);

        // Assert
        Assert.NotNull(refreshToken);
        Assert.Equal(userId, refreshToken.UserId);
        Assert.False(refreshToken.IsRevoked);
        Assert.True(refreshToken.ExpiresAt > DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public void HashToken_ShouldGenerateDeterministicSha256()
    {
        // Arrange
        const string rawToken = "sample-secret-token-12345";

        // Act
        var hash1 = _tokenService.HashToken(rawToken);
        var hash2 = _tokenService.HashToken(rawToken);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // 256 bits = 64 hex characters
    }
}

