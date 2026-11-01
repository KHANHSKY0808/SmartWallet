using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Extensions;
using Xunit;

namespace SharedKernel.Tests;

public class JwtAuthExtensionsTests
{
    [Fact]
    public void AddJwtAuthentication_ShouldThrowException_WhenKeyIsMissing()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build(); // No keys

        // Đảm bảo không có biến môi trường rác
        Environment.SetEnvironmentVariable("JWT_SIGNING_KEY", null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddJwtAuthentication(config);
        });

        Assert.Contains("JWT SigningKey is missing", ex.Message);
    }

    [Fact]
    public void AddJwtAuthentication_ShouldThrowException_WhenKeyIsTooShort()
    {
        var services = new ServiceCollection();
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Jwt:SigningKey", "ShortKey123" } // < 32 bytes
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddJwtAuthentication(config);
        });

        Assert.Contains("must be at least 256 bits", ex.Message);
    }

    [Fact]
    public void AddJwtAuthentication_ShouldSucceed_WhenKeyIsValid()
    {
        var services = new ServiceCollection();
        // Sinh key ngẫu nhiên đạt chuẩn >= 32 bytes phục vụ test
        var testKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Jwt:SigningKey", testKey }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        services.AddJwtAuthentication(config);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }
}

