using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UserProfileService.Controllers;
using UserProfileService.Data;
using UserProfileService.Models;

namespace UserProfileService.Tests;

public class ExchangeRatesControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ProfileDbContext _context;
    private readonly ExchangeRatesController _controller;
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _normalUserId = Guid.NewGuid();

    public ExchangeRatesControllerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ProfileDbContext(options);
        _context.Database.EnsureCreated();

        // Seed currencies and exchange rates
        _context.Currencies.AddRange(
            new Currency { Code = "VND", Name = "Việt Nam Đồng", Symbol = "₫", IsActive = true },
            new Currency { Code = "USD", Name = "Đô la Mỹ", Symbol = "$", IsActive = true },
            new Currency { Code = "EUR", Name = "Euro", Symbol = "€", IsActive = true }
        );
        _context.ExchangeRates.AddRange(
            new ExchangeRate { BaseCurrency = "USD", TargetCurrency = "VND", Rate = 25450m, Source = "Manual", UpdatedAt = DateTime.UtcNow },
            new ExchangeRate { BaseCurrency = "EUR", TargetCurrency = "VND", Rate = 27500m, Source = "Manual", UpdatedAt = DateTime.UtcNow }
        );
        _context.SaveChanges();

        _controller = new ExchangeRatesController(_context, NullLogger<ExchangeRatesController>.Instance);
    }

    private void SetUser(Guid userId, string role = "User")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString()),
            new(ClaimTypes.Role, role),
            new("role", role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Fact]
    public async Task FX01_UserReadsSeededRates_Returns200WithUsdAndEur()
    {
        SetUser(_normalUserId);

        var result = await _controller.GetExchangeRates(null, null) as OkObjectResult;
        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);

        var items = result.Value as System.Collections.IEnumerable;
        Assert.NotNull(items);

        var list = items.Cast<object>().ToList();
        Assert.True(list.Count >= 2);
    }

    [Fact]
    public async Task FX02_NonAdminCannotUpdateRate_Returns403()
    {
        SetUser(_normalUserId, role: "User");

        var result = await _controller.UpdateExchangeRate("USD", "VND", new UpdateRateRequest { Rate = 26000m }) as ObjectResult;
        Assert.NotNull(result);
        Assert.Equal(403, result.StatusCode);
    }

    [Theory]
    [InlineData("USD", "VND", 0)]
    [InlineData("USD", "VND", -5)]
    [InlineData("USD", "USD", 1)]
    [InlineData("XYZ", "VND", 2)]
    public async Task FX03_InvalidRates_Rejected_Returns400(string b, string t, decimal rate)
    {
        SetUser(_adminUserId, role: "Admin");

        var result = await _controller.UpdateExchangeRate(b, t, new UpdateRateRequest { Rate = rate }) as BadRequestObjectResult;
        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task FX05_InvertedRate_CalculatesCorrectly()
    {
        SetUser(_normalUserId);

        // USD -> VND is 25450, ask VND -> USD
        var result = await _controller.GetSingleExchangeRate("VND", "USD") as OkObjectResult;
        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);

        var dto = Assert.IsType<ExchangeRateDto>(result.Value);
        decimal rate = dto.Rate;
        decimal expected = Math.Round(1m / 25450m, 6, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, rate);
    }

    [Fact]
    public async Task FX06_ExchangeRateHistory_RecordsChanges()
    {
        SetUser(_adminUserId, role: "Admin");

        // First update
        var res1 = await _controller.UpdateExchangeRate("USD", "VND", new UpdateRateRequest { Rate = 25000m });
        Assert.IsType<OkObjectResult>(res1);

        // Second update
        var res2 = await _controller.UpdateExchangeRate("USD", "VND", new UpdateRateRequest { Rate = 25500m });
        Assert.IsType<OkObjectResult>(res2);

        var histories = await _context.ExchangeRateHistories
            .Where(h => h.BaseCurrency == "USD" && h.TargetCurrency == "VND")
            .ToListAsync();

        Assert.Equal(2, histories.Count);
        Assert.Equal(25450m, histories[0].OldRate);
        Assert.Equal(25000m, histories[0].NewRate);
        Assert.Equal(25000m, histories[1].OldRate);
        Assert.Equal(25500m, histories[1].NewRate);
    }

    [Fact]
    public async Task FX08_InternalRoute_ReturnsRate()
    {
        var result = await _controller.GetInternalExchangeRate("USD", "VND") as OkObjectResult;
        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);

        var dto = Assert.IsType<ExchangeRateDto>(result.Value);
        Assert.Equal("USD", dto.BaseCurrency);
        Assert.Equal("VND", dto.TargetCurrency);
        Assert.Equal(25450m, dto.Rate);
    }

    [Fact]
    public async Task ExchangeRate_Cache_FX09()
    {
        SetUser(_normalUserId);

        // Fetch rate twice to verify consistent cached/resolved result
        var result1 = await _controller.GetSingleExchangeRate("USD", "VND") as OkObjectResult;
        Assert.NotNull(result1);
        Assert.Equal(200, result1.StatusCode);
        var dto1 = Assert.IsType<ExchangeRateDto>(result1.Value);

        var result2 = await _controller.GetSingleExchangeRate("USD", "VND") as OkObjectResult;
        Assert.NotNull(result2);
        Assert.Equal(200, result2.StatusCode);
        var dto2 = Assert.IsType<ExchangeRateDto>(result2.Value);

        Assert.Equal(dto1.Rate, dto2.Rate);
        Assert.Equal(25450m, dto2.Rate);
    }

    [Fact]
    public async Task ExchangeRate_Fallback_FX10()
    {
        SetUser(_normalUserId);

        // Same currency returns 1.0
        var sameResult = await _controller.GetSingleExchangeRate("VND", "VND") as OkObjectResult;
        Assert.NotNull(sameResult);
        var sameDto = Assert.IsType<ExchangeRateDto>(sameResult.Value);
        Assert.Equal(1.0m, sameDto.Rate);

        // Inverted rate fallback
        var invertResult = await _controller.GetSingleExchangeRate("VND", "USD") as OkObjectResult;
        Assert.NotNull(invertResult);
        var invertDto = Assert.IsType<ExchangeRateDto>(invertResult.Value);
        Assert.True(invertDto.Rate > 0);

        // Unsupported currency pair fallback -> 404
        var missingResult = await _controller.GetSingleExchangeRate("JPY", "EUR") as NotFoundObjectResult;
        Assert.NotNull(missingResult);
        Assert.Equal(404, missingResult.StatusCode);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
