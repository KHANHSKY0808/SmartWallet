using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using BudgetService.Data;
using BudgetService.DTOs;
using BudgetService.Models;
using BudgetService.Services;

namespace BudgetService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BudgetsController : ControllerBase
{
    private readonly BudgetDbContext _context;
    private readonly ICategoryClient _categoryClient;
    private readonly ITransactionClient _transactionClient;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<BudgetsController> _logger;

    public BudgetsController(
        BudgetDbContext context,
        ICategoryClient categoryClient,
        ITransactionClient transactionClient,
        ILogger<BudgetsController> logger,
        IEventPublisher? eventPublisher = null)
    {
        _context = context;
        _categoryClient = categoryClient;
        _transactionClient = transactionClient;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    [HttpGet]
    public async Task<IActionResult> GetBudgets([FromQuery] BudgetFilterParameters filter)
    {
        var userId = GetCurrentUserId();

        // D31: Nếu không truyền month/year thì lấy tháng hiện tại theo giờ Việt Nam
        var (defaultMonth, defaultYear) = VietnamTimeHelper.GetCurrentMonthYearInVietnam();
        var targetMonth = filter.Month ?? defaultMonth;
        var targetYear = filter.Year ?? defaultYear;

        var budgets = await _context.Budgets
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.Month == targetMonth && b.Year == targetYear)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();

        return Ok(budgets.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBudget(Guid id)
    {
        var userId = GetCurrentUserId();
        var budget = await _context.Budgets
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (budget == null)
        {
            return NotFound(new { message = "Ngân sách không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        return Ok(ToDto(budget));
    }

    [HttpPost]
    public async Task<IActionResult> CreateBudget([FromBody] CreateBudgetRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();
        var token = GetBearerToken();

        // B-02: Validate amount, month, year
        if (request.Amount <= 0 || request.Amount > 1000000000000m)
        {
            return BadRequest(new { message = "Số tiền ngân sách phải lớn hơn 0 và không quá 1.000 tỷ" });
        }

        if (request.Month < 1 || request.Month > 12)
        {
            return BadRequest(new { message = "Tháng không hợp lệ (1-12)" });
        }

        if (request.Year < 2000 || request.Year > 2100)
        {
            return BadRequest(new { message = "Năm không hợp lệ (2000-2100)" });
        }

        // B-03, B-04: Validate danh mục phải tồn tại, thuộc user hoặc mặc định, Type = 1 (Chi tiêu)
        var catResult = await _categoryClient.ValidateCategoryAsync(request.CategoryId, token);
        if (!catResult.Exists)
        {
            return BadRequest(new { message = "Danh mục không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        if (catResult.Type != 1)
        {
            return BadRequest(new { message = "Ngân sách chỉ được thiết lập cho danh mục Chi tiêu (Type = 1)" });
        }

        // B-05: Kiểm tra trùng ngân sách (UserId, CategoryId, Month, Year)
        var existingBudget = await _context.Budgets
            .AnyAsync(b => b.UserId == userId && b.CategoryId == request.CategoryId && b.Month == request.Month && b.Year == request.Year);

        if (existingBudget)
        {
            return StatusCode(409, new { code = "DUPLICATE_BUDGET", message = "Ngân sách cho danh mục này trong tháng đã tồn tại" });
        }

        // D34, B-11, B-12: Backfill giao dịch từ TransactionService
        var categoriesToQuery = new List<Guid> { request.CategoryId };
        if (catResult.ChildCategoryIds.Count > 0)
        {
            categoriesToQuery.AddRange(catResult.ChildCategoryIds);
        }

        var (startUtc, endUtc) = VietnamTimeHelper.GetMonthDateRangeInUtc(request.Month, request.Year);
        var backfillResult = await _transactionClient.GetExpenseTransactionsAsync(categoriesToQuery, startUtc, endUtc, token);

        if (!backfillResult.Success)
        {
            _logger.LogError("Không thể backfill giao dịch từ TransactionService: {Error}", backfillResult.ErrorMessage);
            return StatusCode(503, new { code = "TRANSACTION_SERVICE_UNAVAILABLE", message = "Không thể kết nối đến TransactionService để đồng bộ giao dịch ngân sách" });
        }

        var budget = new Budget
        {
            UserId = userId,
            CategoryId = request.CategoryId,
            CategoryName = catResult.Name,
            Amount = request.Amount,
            Month = request.Month,
            Year = request.Year,
            CurrentSpent = backfillResult.Transactions.Sum(t => t.Amount),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // D35, B-10: Kiểm tra ngưỡng cảnh báo ngay lúc tạo
        var percent = budget.Amount > 0 ? (budget.CurrentSpent / budget.Amount) * 100m : 0m;
        BudgetThresholdReachedEvent? alertEvent = null;

        if (percent >= 100m)
        {
            budget.IsAlertSent80 = true;
            budget.IsAlertSent100 = true;
            alertEvent = new BudgetThresholdReachedEvent
            {
                BudgetId = budget.Id,
                UserId = budget.UserId,
                CategoryId = budget.CategoryId,
                CategoryName = catResult.Name ?? "Danh mục",
                Percent = 100,
                CurrentSpent = budget.CurrentSpent,
                BudgetLimit = budget.Amount,
                Month = budget.Month,
                Year = budget.Year
            };
        }
        else if (percent >= 80m)
        {
            budget.IsAlertSent80 = true;
            budget.IsAlertSent100 = false;
            alertEvent = new BudgetThresholdReachedEvent
            {
                BudgetId = budget.Id,
                UserId = budget.UserId,
                CategoryId = budget.CategoryId,
                CategoryName = catResult.Name ?? "Danh mục",
                Percent = 80,
                CurrentSpent = budget.CurrentSpent,
                BudgetLimit = budget.Amount,
                Month = budget.Month,
                Year = budget.Year
            };
        }

        // D36: DB transaction + publish confirm
        using var dbTx = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.Budgets.Add(budget);

            // Ghi sổ cái BudgetTransactions
            foreach (var tx in backfillResult.Transactions)
            {
                _context.BudgetTransactions.Add(new BudgetTransaction
                {
                    BudgetId = budget.Id,
                    TransactionId = tx.Id,
                    UserId = userId,
                    Amount = tx.Amount,
                    TransactionDate = tx.TransactionDate,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            if (alertEvent != null && _eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(alertEvent, "budget.threshold_reached");
            }

            await dbTx.CommitAsync();
            return StatusCode(201, ToDto(budget));
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi tạo ngân sách hoặc ghi Outbox cảnh báo, đã rollback DB transaction");
            throw;
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateBudget(Guid id, [FromBody] UpdateBudgetRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (request.Amount <= 0 || request.Amount > 1000000000000m)
        {
            return BadRequest(new { message = "Số tiền ngân sách phải lớn hơn 0 và không quá 1.000 tỷ" });
        }

        var userId = GetCurrentUserId();
        var budget = await _context.Budgets.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (budget == null)
        {
            return NotFound(new { message = "Ngân sách không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        budget.Amount = request.Amount;
        budget.UpdatedAt = DateTime.UtcNow;

        // D35, B-13, B-14: Kiểm tra ngưỡng cảnh báo khi sửa hạn mức
        var percent = budget.Amount > 0 ? (budget.CurrentSpent / budget.Amount) * 100m : 0m;
        BudgetThresholdReachedEvent? alertEvent = null;

        if (percent >= 100m)
        {
            if (!budget.IsAlertSent100)
            {
                budget.IsAlertSent80 = true;
                budget.IsAlertSent100 = true;
                alertEvent = new BudgetThresholdReachedEvent
                {
                    BudgetId = budget.Id,
                    UserId = budget.UserId,
                    CategoryId = budget.CategoryId,
                    CategoryName = budget.CategoryName ?? "Danh mục",
                    Percent = 100,
                    CurrentSpent = budget.CurrentSpent,
                    BudgetLimit = budget.Amount,
                    Month = budget.Month,
                    Year = budget.Year
                };
            }
        }
        else if (percent >= 80m)
        {
            if (!budget.IsAlertSent80)
            {
                budget.IsAlertSent80 = true;
                alertEvent = new BudgetThresholdReachedEvent
                {
                    BudgetId = budget.Id,
                    UserId = budget.UserId,
                    CategoryId = budget.CategoryId,
                    CategoryName = budget.CategoryName ?? "Danh mục",
                    Percent = 80,
                    CurrentSpent = budget.CurrentSpent,
                    BudgetLimit = budget.Amount,
                    Month = budget.Month,
                    Year = budget.Year
                };
            }

            if (budget.IsAlertSent100)
            {
                budget.IsAlertSent100 = false;
            }
        }
        else
        {
            // B-14: Spent < 80% -> Gỡ cờ 80 và 100
            budget.IsAlertSent80 = false;
            budget.IsAlertSent100 = false;
        }

        // D36: DB transaction + publish confirm
        using var dbTx = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.SaveChangesAsync();

            if (alertEvent != null && _eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(alertEvent, "budget.threshold_reached");
            }

            await dbTx.CommitAsync();
            return Ok(ToDto(budget));
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi cập nhật hạn mức ngân sách hoặc ghi Outbox cảnh báo, đã rollback DB transaction");
            throw;
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBudget(Guid id)
    {
        var userId = GetCurrentUserId();
        var budget = await _context.Budgets.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
        if (budget == null)
        {
            return NotFound(new { message = "Ngân sách không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        _context.Budgets.Remove(budget);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Xóa ngân sách thành công" });
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }

    private string? GetBearerToken()
    {
        return Request.Headers.Authorization.FirstOrDefault();
    }

    private static BudgetDto ToDto(Budget b)
    {
        var percent = b.Amount > 0 ? Math.Round((b.CurrentSpent / b.Amount) * 100m, 1) : 0m;
        var status = percent >= 100m ? "exceeded" : (percent >= 80m ? "warning" : "normal");

        return new BudgetDto
        {
            Id = b.Id,
            UserId = b.UserId,
            CategoryId = b.CategoryId,
            CategoryName = b.CategoryName,
            Amount = b.Amount,
            Month = b.Month,
            Year = b.Year,
            CurrentSpent = b.CurrentSpent,
            Percent = percent,
            Status = status,
            IsAlertSent80 = b.IsAlertSent80,
            IsAlertSent100 = b.IsAlertSent100,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt
        };
    }
}

