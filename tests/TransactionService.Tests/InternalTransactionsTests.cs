using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Extensions;

namespace TransactionService.Tests;

public class InternalTransactionsTests
{
    [Fact]
    public async Task Test_RP10_InternalTransactions_RequiresSystemRoleToken()
    {
        const string key = "QA-E2E-TEST-ONLY-signing-key-do-not-use-0123456789";
        Environment.SetEnvironmentVariable("JWT_SIGNING_KEY", key);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Jwt:SigningKey", key } })
            .Build();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("Jwt:SigningKey", key));
        using var client = factory.CreateClient();

        // 1. Không token -> 401
        var noAuth = await client.GetAsync("/internal/transactions");
        Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);

        // 2. Token User thường -> 403
        var userToken = CreateTestToken("User", key);
        using var reqUser = new HttpRequestMessage(HttpMethod.Get, "/internal/transactions");
        reqUser.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var userResp = await client.SendAsync(reqUser);
        Assert.Equal(HttpStatusCode.Forbidden, userResp.StatusCode);

        // 3. Service token role=System -> 200
        var sysToken = ServiceTokenFactory.CreateSystemToken(config, "ReportService");
        using var reqSys = new HttpRequestMessage(HttpMethod.Get, "/internal/transactions");
        reqSys.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sysToken);
        var sysResp = await client.SendAsync(reqSys);
        Assert.Equal(HttpStatusCode.OK, sysResp.StatusCode);
    }

    private static string CreateTestToken(string role, string key)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role)
        };
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

