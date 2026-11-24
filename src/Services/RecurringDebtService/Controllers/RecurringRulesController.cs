using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecurringDebtService.Data;
using RecurringDebtService.DTOs;
using RecurringDebtService.Models;
using RecurringDebtService.Services;

namespace RecurringDebtService.Controllers;

[ApiController]
[Route("api/recurring")]
[Authorize]
public class RecurringRulesController : ControllerBase
{
    private readonly RecurringDebtDbContext _context;
    private readonly ISchedulerEngine _schedulerEngine;
    private readonly IWalletServiceClient _walletClient;
    private readonly ICategoryServiceClient _categoryClient;
    private readonly ILogger<RecurringRulesController> _logger;

    public RecurringRulesController(
        RecurringDebtDbContext context,
        ISchedulerEngine schedulerEngine,
        IWalletServiceClient walletClient,
        ICategoryServiceClient categoryClient,
        ILogger<RecurringRulesController> logger)
    {
        _context = context;
        _schedulerEngine = schedulerEngine;
        _walletClient = walletClient;
        _categoryClient = categoryClient;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateRule([FromBody] CreateRecurringRuleRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // RR-02: Kiểm tra tính hợp lệ
        if (request.Frequency < 1 || request.Frequency > 4)
        {
            return BadRequest(new { message = "Tần suất phải từ 1 (Ngày) đến 4 (Năm)" });
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { message = "Số tiền phải lớn hơn 0" });
        }

        if (request.Type != 1 && request.Type != 2)
        {
            return BadRequest(new { message = "Loại giao dịch phải là 1 (Chi) hoặc 2 (Thu)" });
        }

        if (request.EndDate.HasValue && request.EndDate.Value < request.StartDate)
        {
            return BadRequest(new { message = "Ngày kết thúc phải sau hoặc bằng ngày bắt đầu" });
        }

        var userId = GetCurrentUserId();
        var token = GetBearerToken();

        // Kiểm tra danh mục
        var catResult = await _categoryClient.ValidateCategoryAsync(request.CategoryId, token, cancellationToken);
        if (!catResult.Exists || (catResult.UserId != null && catResult.UserId != userId))
        {
            return BadRequest(new { message = "Danh mục không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        // RR-02: Danh mục phải cùng loại với Type
        if (catResult.Type.HasValue && catResult.Type.Value != request.Type)
        {
            return BadRequest(new { message = "Loại danh mục không khớp với loại giao dịch định kỳ" });
        }

        // Kiểm tra ví
        var walletResult = await _walletClient.ValidateWalletAsync(request.WalletId, token, cancellationToken);
        if (!walletResult.Exists || (walletResult.UserId != null && walletResult.UserId != userId) || walletResult.IsArchived)
        {
            return BadRequest(new { message = "Ví không tồn tại hoặc đã bị lưu trữ" });
        }

        var anchorDay = request.StartDate.Day;
        var todayVn = _schedulerEngine.GetTodayVietnam();

        // D55 / RR-04: Không sinh bù quá khứ. NextExecutionDate = kỳ đầu tiên >= hôm nay (giờ VN)
        var nextExecutionDate = _schedulerEngine.FindInitialNextExecutionDate(
            request.StartDate, (FrequencyType)request.Frequency, anchorDay, todayVn);

        var rule = new RecurringRule
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name.Trim(),
            WalletId = request.WalletId,
            CategoryId = request.CategoryId,
            Type = request.Type,
            Amount = request.Amount,
            Currency = "VND",
            Frequency = (FrequencyType)request.Frequency,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            NextExecutionDate = nextExecutionDate,
            AnchorDay = anchorDay,
            IsActive = true,
            Note = request.Note,
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.RecurringRules.Add(rule);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Tạo quy tắc định kỳ {RuleId} thành công cho User {UserId}", rule.Id, userId);
        return StatusCode(201, ToDto(rule));
    }

    [HttpGet]
    public async Task<IActionResult> GetRules(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var rules = await _context.RecurringRules
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(rules.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRule(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var rule = await _context.RecurringRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken);
        if (rule == null)
        {
            return NotFound(new { message = "Quy tắc định kỳ không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        return Ok(ToDto(rule));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateRule(Guid id, [FromBody] UpdateRecurringRuleRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var rule = await _context.RecurringRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken);
        if (rule == null)
        {
            return NotFound(new { message = "Quy tắc định kỳ không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        // Tạm dừng / Tiếp tục
        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value && !rule.IsActive)
            {
                // D55 / SC-08: Tiếp tục rule sau khi dừng: NextExecutionDate = kỳ kế tiếp >= hôm nay, không chạy bù
                var todayVn = _schedulerEngine.GetTodayVietnam();
                if (rule.NextExecutionDate < todayVn)
                {
                    rule.NextExecutionDate = _schedulerEngine.FindInitialNextExecutionDate(
                        rule.NextExecutionDate, rule.Frequency, rule.AnchorDay, todayVn);
                }
            }
            rule.IsActive = request.IsActive.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Name)) rule.Name = request.Name.Trim();
        if (request.Amount.HasValue && request.Amount.Value > 0) rule.Amount = request.Amount.Value;
        if (request.EndDate.HasValue) rule.EndDate = request.EndDate.Value;
        if (request.Note != null) rule.Note = request.Note;

        rule.UpdatedAt = DateTime.UtcNow;
        rule.RowVersion = Guid.NewGuid();
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(rule));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var rule = await _context.RecurringRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken);
        if (rule == null)
        {
            return NotFound(new { message = "Quy tắc định kỳ không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        _context.RecurringRules.Remove(rule);
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Đã xóa quy tắc định kỳ thành công" });
    }

    private static RecurringRuleDto ToDto(RecurringRule r) => new()
    {
        Id = r.Id,
        UserId = r.UserId,
        Name = r.Name,
        WalletId = r.WalletId,
        CategoryId = r.CategoryId,
        Type = r.Type,
        Amount = r.Amount,
        Currency = r.Currency,
        Frequency = (int)r.Frequency,
        StartDate = r.StartDate.ToString("yyyy-MM-dd"),
        EndDate = r.EndDate?.ToString("yyyy-MM-dd"),
        NextExecutionDate = r.NextExecutionDate.ToString("yyyy-MM-dd"),
        AnchorDay = r.AnchorDay,
        IsActive = r.IsActive,
        LastExecutedDate = r.LastExecutedDate,
        LastError = r.LastError,
        Note = r.Note,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string? GetBearerToken()
    {
        return Request.Headers.Authorization.FirstOrDefault();
    }
}

