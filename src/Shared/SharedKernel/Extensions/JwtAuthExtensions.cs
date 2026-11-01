using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace SharedKernel.Extensions;

public static class JwtAuthExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Ưu tiên đọc từ Configuration (appsettings / user-secrets), sau đó fallback sang biến môi trường
        var jwtKey = configuration["Jwt:SigningKey"]
            ?? Environment.GetEnvironmentVariable("JWT_SIGNING_KEY");

        // Fail-fast: Báo lỗi nghiêm ngặt ngay khi khởi động nếu thiếu signing key
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException(
                "CRITICAL SECURITY ERROR: JWT SigningKey is missing. " +
                "Please configure 'Jwt:SigningKey' via dotnet user-secrets or environment variable 'JWT_SIGNING_KEY'. " +
                "Never commit hardcoded secrets to source control.");
        }

        if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
        {
            throw new InvalidOperationException(
                "CRITICAL SECURITY ERROR: JWT SigningKey must be at least 256 bits (32 bytes) long.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
            options.AddPolicy("UserOnly", policy => policy.RequireRole("User"));
            options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
        });

        return services;
    }
}
