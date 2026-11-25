using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace SharedKernel.Extensions;

/// <summary>
/// Tạo Service Token (JWT role="System", thời hạn 5 phút) để gọi các endpoint /internal/* theo D41 & D58.
/// </summary>
public static class ServiceTokenFactory
{
    public static string CreateSystemToken(IConfiguration configuration, string serviceName)
    {
        var jwtKey = configuration["Jwt:SigningKey"]
            ?? configuration["JWT_SIGNING_KEY"]
            ?? Environment.GetEnvironmentVariable("JWT_SIGNING_KEY");

        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException("JWT SigningKey is missing for ServiceTokenFactory.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
        var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, serviceName),
            new Claim(ClaimTypes.NameIdentifier, serviceName),
            new Claim(ClaimTypes.Role, "System"),
            new Claim("role", "System")
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

