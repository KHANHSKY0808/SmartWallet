using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace SharedKernel.Extensions;

/// <summary>7 quyền RBAC chuẩn theo D65.</summary>
public static class AppPermissions
{
    public const string UsersRead = "users.read";
    public const string UsersLock = "users.lock";
    public const string UsersRolesWrite = "users.roles.write";
    public const string AuditRead = "audit.read";
    public const string RatesWrite = "rates.write";
    public const string CategoriesDefaultsWrite = "categories.defaults.write";
    public const string HealthRead = "health.read";
    public const string BackupsWrite = "backups.write";

    public static readonly IReadOnlyList<string> All =
    [
        UsersRead,
        UsersLock,
        UsersRolesWrite,
        AuditRead,
        RatesWrite,
        CategoriesDefaultsWrite,
        HealthRead,
        BackupsWrite
    ];
}

public static class JwtAuthExtensions
{
    public static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        return user.IsInRole("Admin")
            || user.HasClaim("role", "Admin")
            || user.HasClaim(ClaimTypes.Role, "Admin")
            || user.HasClaim("perm", permission);
    }

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Ưu tiên đọc từ Configuration (appsettings / user-secrets), sau đó fallback sang biến môi trường
        var jwtKey = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            jwtKey = configuration["JWT_SIGNING_KEY"]
                ?? Environment.GetEnvironmentVariable("JWT_SIGNING_KEY");
        }

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
            options.AddPolicy("SystemOnly", policy => policy.RequireRole("System"));
            options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());

            foreach (var perm in AppPermissions.All)
            {
                var p = perm;
                options.AddPolicy(p, policy => policy.RequireAssertion(ctx => HasPermission(ctx.User, p)));
            }
        });

        return services;
    }
}
