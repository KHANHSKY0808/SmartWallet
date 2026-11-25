using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using SavingGoalService.Data;
using SavingGoalService.DTOs;
using SavingGoalService.Models;
using SavingGoalService.Services;

namespace SavingGoalService.Controllers;

[ApiController]
[Route("api/savings")]
[Authorize]
public class SavingsController : ControllerBase
{
    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "VND", "USD", "EUR"
    };

    private readonly SavingGoalDbContext _context;
    private readonly IEventPublisher _publisher;
    private readonly ILogger<SavingsController> _logger;
    private readonly IWalletServiceClient? _walletServiceClient;

    public SavingsController(
        SavingGoalDbContext context,
        IEventPublisher publisher,
        ILogger<SavingsController> logger,
        IWalletServiceClient? walletServiceClient = null)
    {
        _context = context;
        _publisher = publisher;
        _logger = logger;
        _walletServiceClient = walletServiceClient;
    }

    [HttpPost]
    public async Task<IActionResult> CreateGoal([FromBody] CreateSavingGoalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150)
        {
            return BadRequest(new { message = "Tên mục tiêu không được rỗng và tối đa 150 ký tự." });
        }

        if (request.TargetAmount <= 0)
        {
            return BadRequest(new { message = "Số tiền mục tiêu phải lớn hơn 0." });
        }

        var vnNow = DateTime.UtcNow.AddHours(7);
        if (request.TargetDate.Date <= vnNow.Date)
        {
            return BadRequest(new { message = "Hạn mục tiêu phải ở tương lai." });
        }

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency.Trim().ToUpperInvariant();
        if (!AllowedCurrencies.Contains(currency))
        {
            return BadRequest(new { code = "UNSUPPORTED_CURRENCY", message = $"Tiền tệ '{currency}' không được hỗ trợ." });
        }

        var userId = GetCurrentUserId();
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name.Trim(),
            TargetAmount = request.TargetAmount,
            CurrentAmount = 0m,
            Currency = currency,
            TargetDate = request.TargetDate.Date,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.SavingGoals.AddAsync(goal);
        await _context.SaveChangesAsync();

        var dto = MapToDto(goal);
        return StatusCode(201, dto);
    }

    [HttpGet]
    public async Task<IActionResult> GetGoals([FromQuery] string? status)
    {
        var userId = GetCurrentUserId();
        var query = _context.SavingGoals.AsNoTracking().Where(g => g.UserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (int.TryParse(status, out var statusCode))
            {
                query = query.Where(g => g.Status == statusCode);
            }
            else if (string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(g => g.Status == 1);
            }
            else if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(g => g.Status == 2);
            }
            else if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(g => g.Status == 3);
            }
        }

        var goals = await query.OrderByDescending(g => g.CreatedAt).ToListAsync();
        var dtos = goals.Select(MapToDto).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetGoalById([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        var goal = await _context.SavingGoals.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
        if (goal == null)
        {
            return NotFound(new { message = "Không tìm thấy mục tiêu tiết kiệm." });
        }

        return Ok(MapToDto(goal));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateGoal([FromRoute] Guid id, [FromBody] UpdateSavingGoalRequest request)
    {
        var userId = GetCurrentUserId();
        var goal = await _context.SavingGoals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
        if (goal == null)
        {
            return NotFound(new { message = "Không tìm thấy mục tiêu tiết kiệm." });
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            if (request.Name.Trim().Length > 150)
            {
                return BadRequest(new { message = "Tên mục tiêu tối đa 150 ký tự." });
            }
            goal.Name = request.Name.Trim();
        }

        if (request.TargetDate.HasValue)
        {
            var vnNow = DateTime.UtcNow.AddHours(7);
            if (request.TargetDate.Value.Date <= vnNow.Date)
            {
                return BadRequest(new { message = "Hạn mục tiêu phải ở tương lai." });
            }
            goal.TargetDate = request.TargetDate.Value.Date;
        }

        bool newlyCompleted = false;
        if (request.TargetAmount.HasValue)
        {
            if (request.TargetAmount.Value <= 0)
            {
                return BadRequest(new { message = "Số tiền mục tiêu phải lớn hơn 0." });
            }
            goal.TargetAmount = request.TargetAmount.Value;

            if (goal.CurrentAmount >= goal.TargetAmount && goal.Status != 2)
            {
                goal.Status = 2;
                goal.CompletedAt = DateTime.UtcNow;
                newlyCompleted = true;
            }
        }

        goal.UpdatedAt = DateTime.UtcNow;
        goal.RowVersion = Guid.NewGuid().ToByteArray();

        await using var txUpdate = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.SaveChangesAsync();

            if (newlyCompleted)
            {
                await _publisher.PublishAsync(new GoalCompletedEvent
                {
                    Id = Guid.NewGuid(),
                    GoalId = goal.Id,
                    UserId = goal.UserId,
                    GoalName = goal.Name,
                    TargetAmount = goal.TargetAmount,
                    CurrentAmount = goal.CurrentAmount
                }, "goal.completed");
            }

            await txUpdate.CommitAsync();
            return Ok(MapToDto(goal));
        }
        catch (DbUpdateConcurrencyException)
        {
            await txUpdate.RollbackAsync();
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi cập nhật mục tiêu." });
        }
        catch (Exception)
        {
            await txUpdate.RollbackAsync();
            throw;
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> CancelGoal([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        var goal = await _context.SavingGoals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
        if (goal == null)
        {
            return NotFound(new { message = "Không tìm thấy mục tiêu tiết kiệm." });
        }

        if (goal.CurrentAmount > 0)
        {
            return Conflict(new { code = "GOAL_HAS_FUNDS", message = "Không thể hủy mục tiêu khi vẫn còn tiền tích lũy." });
        }

        goal.Status = 3; // Cancelled
        goal.UpdatedAt = DateTime.UtcNow;
        goal.RowVersion = Guid.NewGuid().ToByteArray();

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi hủy mục tiêu." });
        }

        return Ok(new { message = "Đã hủy mục tiêu thành công." });
    }

    [HttpPost("{goalId:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(
        [FromRoute] Guid goalId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] WithdrawRequest request)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new { message = "Header 'Idempotency-Key' là bắt buộc." });
        }

        var key = idempotencyKey.Trim();
        var userId = GetCurrentUserId();

        var requestPayload = $"{request.WalletId}:{request.Amount:F2}";
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestPayload)));

        var existingRecord = await _context.IdempotencyRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key);
        if (existingRecord != null)
        {
            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(422, new { code = "IDEMPOTENCY_KEY_REUSED", message = "Idempotency key đã được sử dụng với payload khác." });
            }
            return StatusCode(existingRecord.StatusCode, JsonSerializer.Deserialize<object>(existingRecord.ResponseJson));
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { message = "Số tiền rút phải lớn hơn 0." });
        }

        var goal = await _context.SavingGoals.FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId);
        if (goal == null)
        {
            return NotFound(new { message = "Không tìm thấy mục tiêu tiết kiệm." });
        }

        if (goal.Status == 3)
        {
            return BadRequest(new { code = "GOAL_CANCELLED", message = "Mục tiêu đã bị hủy, không thể rút tiền." });
        }

        if (goal.CurrentAmount < request.Amount)
        {
            return BadRequest(new { code = "INSUFFICIENT_GOAL_FUNDS", message = "Số tiền tích lũy không đủ để rút." });
        }

        // N23, N24, N25: Kiểm tra ví đích đồng bộ qua WalletService
        if (_walletServiceClient != null)
        {
            var wallet = await _walletServiceClient.GetWalletAsync(request.WalletId);
            if (wallet == null || wallet.UserId != userId)
            {
                return NotFound(new { message = "Ví không tồn tại hoặc không thuộc quyền sở hữu của bạn." });
            }

            if (wallet.IsArchived)
            {
                return BadRequest(new { code = "WALLET_ARCHIVED", message = "Ví đã bị lưu trữ, không thể nhận tiền rút." });
            }

            if (!string.Equals(wallet.Currency, goal.Currency, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { code = "CURRENCY_MISMATCH", message = $"Loại tiền của ví ({wallet.Currency}) không khớp với loại tiền của mục tiêu ({goal.Currency})." });
            }
        }

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            goal.CurrentAmount -= request.Amount;
            goal.UpdatedAt = DateTime.UtcNow;
            goal.RowVersion = Guid.NewGuid().ToByteArray();

            var log = new GoalLog
            {
                Id = Guid.NewGuid(),
                GoalId = goal.Id,
                UserId = userId,
                SourceWalletId = request.WalletId,
                Amount = request.Amount,
                Type = 2, // Withdraw
                Status = 1,
                IdempotencyKey = key,
                CreatedDate = DateTime.UtcNow
            };
            await _context.GoalLogs.AddAsync(log);

            var responseObj = MapToDto(goal);
            var responseJson = JsonSerializer.Serialize(responseObj);

            await _context.IdempotencyRecords.AddAsync(new IdempotencyRecord
            {
                UserId = userId,
                Key = key,
                RequestHash = requestHash,
                ResponseJson = responseJson,
                StatusCode = 200,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });

            await _context.SaveChangesAsync();

            // Publish confirm event GoalFundsWithdrawn bên trong transaction
            var eventId = new Guid(MD5.HashData(Encoding.UTF8.GetBytes($"goal-withdraw:{userId}:{key}")));
            await _publisher.PublishAsync(new GoalFundsWithdrawnEvent
            {
                Id = eventId,
                GoalId = goal.Id,
                WalletId = request.WalletId,
                UserId = userId,
                Amount = request.Amount,
                Currency = goal.Currency,
                GoalName = goal.Name
            }, "goal.funds_withdrawn");

            await tx.CommitAsync();

            return Ok(responseObj);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi cập nhật mục tiêu." });
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            var record = await _context.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key);
            if (record != null)
            {
                return StatusCode(record.StatusCode, JsonSerializer.Deserialize<object>(record.ResponseJson));
            }
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi rút tiền." });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi rút tiền mục tiêu {GoalId}", goalId);
            return StatusCode(503, new { code = "EVENT_PUBLISH_FAILED", message = "Không thể hoàn tất giao dịch rút tiền. Vui lòng thử lại." });
        }
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(idStr, out var id))
        {
            return id;
        }
        throw new UnauthorizedAccessException("Người dùng chưa được xác thực.");
    }

    private static SavingGoalDto MapToDto(SavingGoal g)
    {
        var vnNow = DateTime.UtcNow.AddHours(7);
        var targetDateVn = g.TargetDate.Date;
        var daysLeft = Math.Max(0, (int)(targetDateVn - vnNow.Date).TotalDays);

        decimal percent = 0m;
        if (g.TargetAmount > 0)
        {
            percent = Math.Round((g.CurrentAmount / g.TargetAmount) * 100m, 1);
        }

        decimal monthlyRequired = 0m;
        if (g.CurrentAmount < g.TargetAmount && daysLeft > 0)
        {
            var remainingAmount = g.TargetAmount - g.CurrentAmount;
            double monthsLeft = Math.Max(1.0, daysLeft / 30.0);
            var unrounded = remainingAmount / (decimal)monthsLeft;
            monthlyRequired = Math.Ceiling(unrounded / 1000m) * 1000m;
        }

        string statusStr = g.Status switch
        {
            1 => "InProgress",
            2 => "Completed",
            3 => "Cancelled",
            _ => "InProgress"
        };

        return new SavingGoalDto
        {
            Id = g.Id,
            UserId = g.UserId,
            Name = g.Name,
            TargetAmount = g.TargetAmount,
            CurrentAmount = g.CurrentAmount,
            Percent = percent,
            Currency = g.Currency,
            TargetDate = g.TargetDate,
            Status = statusStr,
            StatusCode = g.Status,
            DaysLeft = daysLeft,
            MonthlyRequired = monthlyRequired,
            CreatedAt = g.CreatedAt,
            UpdatedAt = g.UpdatedAt
        };
    }
}
