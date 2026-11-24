using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserProfileService.Data;
using UserProfileService.Models;

namespace UserProfileService.Controllers;

[ApiController]
[Route("api/profile/exchange-rates")]
[Authorize]
public class ExchangeRatesController : ControllerBase
{
    private readonly ProfileDbContext _context;
    private readonly ILogger<ExchangeRatesController> _logger;

    public ExchangeRatesController(ProfileDbContext context, ILogger<ExchangeRatesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetExchangeRates([FromQuery(Name = "base")] string? baseCur, [FromQuery(Name = "target")] string? targetCur)
    {
        if (!string.IsNullOrWhiteSpace(baseCur) && !string.IsNullOrWhiteSpace(targetCur))
        {
            var singleRate = await ResolveRateAsync(baseCur.Trim().ToUpperInvariant(), targetCur.Trim().ToUpperInvariant());
            if (singleRate == null)
            {
                return NotFound(new { message = $"Không tìm thấy tỷ giá từ {baseCur} sang {targetCur}." });
            }
            return Ok(singleRate);
        }

        var rates = await _context.ExchangeRates.AsNoTracking().ToListAsync();
        var dtos = rates.Select(r => new ExchangeRateDto
        {
            BaseCurrency = r.BaseCurrency,
            TargetCurrency = r.TargetCurrency,
            Rate = r.Rate,
            UpdatedAt = r.UpdatedAt
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("/api/profile/exchange-rate")]
    public async Task<IActionResult> GetSingleExchangeRate([FromQuery(Name = "base")] string? baseCur, [FromQuery(Name = "target")] string? targetCur)
    {
        if (string.IsNullOrWhiteSpace(baseCur) || string.IsNullOrWhiteSpace(targetCur))
        {
            return BadRequest(new { message = "Thiếu tham số 'base' hoặc 'target'." });
        }

        var rate = await ResolveRateAsync(baseCur.Trim().ToUpperInvariant(), targetCur.Trim().ToUpperInvariant());
        if (rate == null)
        {
            return NotFound(new { message = $"Không tìm thấy tỷ giá từ {baseCur} sang {targetCur}." });
        }
        return Ok(rate);
    }

    [HttpPut("{baseCurrency}/{targetCurrency}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateExchangeRate(
        [FromRoute] string baseCurrency,
        [FromRoute] string targetCurrency,
        [FromBody] UpdateRateRequest request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
        if (!User.IsInRole("Admin") && !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, new { message = "Chỉ Admin mới có quyền cập nhật tỷ giá." });
        }

        if (request.Rate <= 0)
        {
            return BadRequest(new { message = "Tỷ giá phải lớn hơn 0." });
        }

        var b = baseCurrency.Trim().ToUpperInvariant();
        var t = targetCurrency.Trim().ToUpperInvariant();

        if (string.Equals(b, t, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Base và Target không được trùng nhau." });
        }

        var validCurrencies = await _context.Currencies
            .Where(c => c.IsActive && (c.Code == b || c.Code == t))
            .Select(c => c.Code)
            .ToListAsync();

        if (!validCurrencies.Contains(b) || !validCurrencies.Contains(t))
        {
            return BadRequest(new { message = "Mã tiền tệ không tồn tại hoặc chưa kích hoạt." });
        }

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid? userId = Guid.TryParse(userIdString, out var uid) ? uid : null;

        var existing = await _context.ExchangeRates.FirstOrDefaultAsync(r => r.BaseCurrency == b && r.TargetCurrency == t);
        var now = DateTime.UtcNow;

        if (existing != null)
        {
            var history = new ExchangeRateHistory
            {
                BaseCurrency = b,
                TargetCurrency = t,
                OldRate = existing.Rate,
                NewRate = request.Rate,
                UpdatedBy = userId,
                UpdatedAt = now
            };
            await _context.ExchangeRateHistories.AddAsync(history);

            existing.Rate = request.Rate;
            existing.UpdatedAt = now;
            existing.UpdatedBy = userId;
        }
        else
        {
            var history = new ExchangeRateHistory
            {
                BaseCurrency = b,
                TargetCurrency = t,
                OldRate = 0,
                NewRate = request.Rate,
                UpdatedBy = userId,
                UpdatedAt = now
            };
            await _context.ExchangeRateHistories.AddAsync(history);

            var newRate = new ExchangeRate
            {
                BaseCurrency = b,
                TargetCurrency = t,
                Rate = request.Rate,
                Source = "Manual",
                UpdatedAt = now,
                UpdatedBy = userId
            };
            await _context.ExchangeRates.AddAsync(newRate);
        }

        await _context.SaveChangesAsync();

        return Ok(new ExchangeRateDto
        {
            BaseCurrency = b,
            TargetCurrency = t,
            Rate = request.Rate,
            UpdatedAt = now
        });
    }

    [AllowAnonymous]
    [HttpGet("/internal/exchange-rates")]
    public async Task<IActionResult> GetInternalExchangeRate([FromQuery(Name = "base")] string? baseCur, [FromQuery(Name = "target")] string? targetCur)
    {
        if (string.IsNullOrWhiteSpace(baseCur) || string.IsNullOrWhiteSpace(targetCur))
        {
            return BadRequest(new { message = "Thiếu tham số 'base' hoặc 'target'." });
        }

        var rate = await ResolveRateAsync(baseCur.Trim().ToUpperInvariant(), targetCur.Trim().ToUpperInvariant());
        if (rate == null)
        {
            return NotFound(new { message = $"Không tìm thấy tỷ giá từ {baseCur} sang {targetCur}." });
        }
        return Ok(rate);
    }

    private async Task<ExchangeRateDto?> ResolveRateAsync(string b, string t)
    {
        if (string.Equals(b, t, StringComparison.OrdinalIgnoreCase))
        {
            return new ExchangeRateDto { BaseCurrency = b, TargetCurrency = t, Rate = 1m, UpdatedAt = DateTime.UtcNow };
        }

        var direct = await _context.ExchangeRates.AsNoTracking().FirstOrDefaultAsync(r => r.BaseCurrency == b && r.TargetCurrency == t);
        if (direct != null)
        {
            return new ExchangeRateDto { BaseCurrency = direct.BaseCurrency, TargetCurrency = direct.TargetCurrency, Rate = direct.Rate, UpdatedAt = direct.UpdatedAt };
        }

        var reverse = await _context.ExchangeRates.AsNoTracking().FirstOrDefaultAsync(r => r.BaseCurrency == t && r.TargetCurrency == b);
        if (reverse != null && reverse.Rate > 0)
        {
            var inverted = Math.Round(1m / reverse.Rate, 6, MidpointRounding.AwayFromZero);
            return new ExchangeRateDto { BaseCurrency = b, TargetCurrency = t, Rate = inverted, UpdatedAt = reverse.UpdatedAt };
        }

        return null;
    }
}

public class ExchangeRateDto
{
    public string BaseCurrency { get; set; } = string.Empty;
    public string TargetCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateRateRequest
{
    public decimal Rate { get; set; }
}
