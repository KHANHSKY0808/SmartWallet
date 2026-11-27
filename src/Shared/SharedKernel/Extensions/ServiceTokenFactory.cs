using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace SharedKernel.Extensions;

/// <summary>
/// Tạo Service Token (JWT role="System", thời hạn 5 phút) để gọi các endpoint /internal/* theo D41, D58 & D65.
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

/// <summary>
/// Tự động gắn Bearer Service Token (role="System") cho các HTTP client nội bộ nếu chưa có Authorization header.
/// </summary>
public sealed class SystemTokenDelegatingHandler : DelegatingHandler
{
    private readonly IConfiguration _configuration;
    private readonly string _serviceName;

    public SystemTokenDelegatingHandler(IConfiguration configuration, string serviceName)
    {
        _configuration = configuration;
        _serviceName = serviceName;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var token = ServiceTokenFactory.CreateSystemToken(_configuration, _serviceName);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (!request.Headers.Contains("X-Correlation-ID") && !string.IsNullOrWhiteSpace(CorrelationContext.CurrentCorrelationId))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", CorrelationContext.CurrentCorrelationId);
        }
        return base.SendAsync(request, cancellationToken);
    }
}

