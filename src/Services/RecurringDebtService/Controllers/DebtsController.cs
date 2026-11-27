using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using RecurringDebtService.Data;
using RecurringDebtService.DTOs;
using RecurringDebtService.Models;
using RecurringDebtService.Services;

namespace RecurringDebtService.Controllers;

[ApiController]
[Route("api/debts")]
[Authorize]
public class DebtsController : ControllerBase
{
    // 4 GUIDs danh mục hệ thống sổ nợ (D44 / FIN-97b / DB-09)
    public static readonly Guid CatExpenseLend = Guid.Parse("10000000-0000-0000-0000-000000000021");
    public static readonly Guid CatIncomeDebtCollection = Guid.Parse("10000000-0000-0000-0000-000000000022");
    public static readonly Guid CatIncomeBorrow = Guid.Parse("10000000-0000-0000-0000-000000000023");
    public static readonly Guid CatExpenseDebtRepayment = Guid.Parse("10000000-0000-0000-0000-000000000024");

    private readonly RecurringDebtDbContext _context;
    private readonly ISchedulerEngine _schedulerEngine;
    private readonly IWalletServiceClient _walletClient;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<DebtsController> _logger;

    public DebtsController(
        RecurringDebtDbContext context,
        ISchedulerEngine schedulerEngine,
        IWalletServiceClient walletClient,
        ILogger<DebtsController> logger,
        IEventPublisher? eventPublisher = null)
    {
        _context = context;
        _schedulerEngine = schedulerEngine;
        _walletClient = walletClient;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    [HttpPost]
    public async Task<IActionResult> CreateDebt([FromBody] CreateDebtRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // DB-03: Validation
        if (request.Type != 1 && request.Type != 2)
        {
            return BadRequest(new { message = "Loại khoản nợ chỉ có thể là 1 (Cho vay) hoặc 2 (Đi vay)" });
        }

        if (string.IsNullOrWhiteSpace(request.PersonName))
        {
            return BadRequest(new { message = "Tên người liên quan không được để trống" });
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { message = "Số tiền phải lớn hơn 0" });
        }

        var todayVn = _schedulerEngine.GetTodayVietnam();
        if (request.DueDate < todayVn)
        {
            return BadRequest(new { message = "Hạn thanh toán không được ở quá khứ" });
        }

        if (request.InterestRate.HasValue && request.InterestRate.Value < 0)
        {
            return BadRequest(new { message = "Lãi suất không được là số âm" });
        }

        var userId = GetCurrentUserId();
        var token = GetBearerToken();

        var walletResult = await _walletClient.ValidateWalletAsync(request.WalletId, token, cancellationToken);
        if (!walletResult.Exists || walletResult.UserId != userId || walletResult.IsArchived)
        {
            return BadRequest(new { message = "Ví không tồn tại, không thuộc quyền sở hữu hoặc đã bị lưu trữ" });
        }

        var debt = new Debt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = (DebtType)request.Type,
            PersonName = request.PersonName.Trim(),
            Amount = request.Amount,
            PaidAmount = 0,
            InterestRate = request.InterestRate,
            Currency = walletResult.Currency,
            DueDate = request.DueDate,
            Status = DebtStatus.Active,
            WalletId = request.WalletId,
            Note = request.Note,
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.Debts.Add(debt);
            await _context.SaveChangesAsync(cancellationToken);

            // Phát sự kiện tạo giao dịch tương ứng (D44 / DB-01 / DB-02 / D30)
            // Cho vay -> Chi, "Cho vay" (ví giảm)
            // Đi vay -> Thu, "Đi vay" (ví tăng)
            if (_eventPublisher != null)
            {
                var txType = debt.Type == DebtType.Lend ? 1 : 2; // 1: Expense, 2: Income
                var catId = debt.Type == DebtType.Lend ? CatExpenseLend : CatIncomeBorrow;
                var note = debt.Type == DebtType.Lend
                    ? $"Cho vay {debt.PersonName}"
                    : $"Đi vay từ {debt.PersonName}";

                try
                {
                    await _eventPublisher.PublishAsync(new DebtTransactionRequestedEvent
                    {
                        DebtId = debt.Id,
                        PaymentId = null,
                        UserId = userId,
                        WalletId = debt.WalletId,
                        Type = txType,
                        Amount = debt.Amount,
                        CategoryId = catId,
                        Note = note,
                        TransactionDate = DateTime.UtcNow
                    }, "debt.transaction_requested", cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await tx.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Lỗi ghi Outbox sự kiện khi tạo khoản nợ {DebtId}", debt.Id);
                    throw;
                }
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Lỗi lưu khoản nợ {DebtId}", debt.Id);
            throw;
        }

        _logger.LogInformation("Tạo khoản nợ {DebtId} thành công cho User {UserId}", debt.Id, userId);
        return StatusCode(201, ToDto(debt));
    }

    [HttpGet]
    public async Task<IActionResult> GetDebts(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var debts = await _context.Debts
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(debts.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDebt(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var debt = await _context.Debts.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);
        if (debt == null)
        {
            return NotFound(new { message = "Khoản nợ không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        return Ok(ToDto(debt));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDebt(Guid id, [FromBody] UpdateDebtRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var debt = await _context.Debts.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);
        if (debt == null)
        {
            return NotFound(new { message = "Khoản nợ không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        // DB-08 / D57: Sửa Amount < PaidAmount -> 400
        if (request.Amount.HasValue)
        {
            if (request.Amount.Value < debt.PaidAmount)
            {
                return BadRequest(new { message = "Số tiền nợ không được nhỏ hơn số tiền đã thanh toán" });
            }
            debt.Amount = request.Amount.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.PersonName)) debt.PersonName = request.PersonName.Trim();
        if (request.DueDate.HasValue) debt.DueDate = request.DueDate.Value;
        if (request.InterestRate.HasValue) debt.InterestRate = request.InterestRate.Value;
        if (request.Note != null) debt.Note = request.Note;

        debt.UpdatedAt = DateTime.UtcNow;
        debt.RowVersion = Guid.NewGuid();
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(debt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDebt(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var debt = await _context.Debts.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);
        if (debt == null)
        {
            return NotFound(new { message = "Khoản nợ không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        // DB-06 / D57: Xóa khoản nợ đã có thanh toán -> 409 DEBT_HAS_PAYMENTS
        if (debt.PaidAmount > 0)
        {
            return StatusCode(409, new { code = "DEBT_HAS_PAYMENTS", message = "Không thể xóa khoản nợ đã có thanh toán" });
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        _context.Debts.Remove(debt);
        await _context.SaveChangesAsync(cancellationToken);

        // DB-05 / D57 / D30: Xóa khoản nợ chưa thanh toán thì phát event để giao dịch gốc bị xóa và hoàn tiền ví
        if (_eventPublisher != null)
        {
            try
            {
                await _eventPublisher.PublishAsync(new DebtDeletedEvent
                {
                    DebtId = debt.Id,
                    UserId = userId
                }, "debt.deleted", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await tx.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Lỗi ghi Outbox sự kiện khi xóa khoản nợ {DebtId}", debt.Id);
                throw;
            }
        }

        await tx.CommitAsync(cancellationToken);

        return Ok(new { message = "Đã xóa khoản nợ thành công" });
    }

    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> MakePayment(Guid id, [FromBody] CreateDebtPaymentRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { message = "Số tiền thanh toán phải lớn hơn 0" });
        }

        var userId = GetCurrentUserId();
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            return BadRequest(new { message = "Header Idempotency-Key là bắt buộc" });
        }

        // D23 / D57 / DP-01..04: Idempotency theo (UserId, Key) + body hash
        var rawBody = JsonSerializer.Serialize(request);
        var bodyHash = ComputeHash(rawBody);

        var existingRecord = await _context.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, cancellationToken);

        if (existingRecord != null)
        {
            if (existingRecord.RequestHash == bodyHash)
            {
                var cachedDto = JsonSerializer.Deserialize<DebtPaymentDto>(existingRecord.ResponseBody);
                return StatusCode(existingRecord.StatusCode, cachedDto);
            }
            return StatusCode(422, new
            {
                code = "IDEMPOTENCY_KEY_REUSED",
                message = "Idempotency key đã được sử dụng với payload khác"
            });
        }

        var debt = await _context.Debts.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);
        if (debt == null)
        {
            return NotFound(new { message = "Khoản nợ không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        // N34 & N35: Kiểm tra ví đồng bộ trước khi ghi thanh toán (tồn tại, thuộc user, chưa lưu trữ, cùng tiền tệ)
        var token = GetBearerToken();
        var walletResult = await _walletClient.ValidateWalletAsync(request.WalletId, token, cancellationToken);
        if (!walletResult.Exists || walletResult.UserId != userId)
        {
            return NotFound(new { message = "Ví không tồn tại hoặc không thuộc quyền sở hữu" });
        }
        if (walletResult.IsArchived)
        {
            return BadRequest(new { code = "WALLET_ARCHIVED", message = "Ví đã bị lưu trữ" });
        }
        if (!string.Equals(walletResult.Currency, debt.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { code = "CURRENCY_MISMATCH", message = $"Loại tiền của ví ({walletResult.Currency}) không khớp với khoản nợ ({debt.Currency})" });
        }

        // DP-06: Khoản nợ đã PaidFull không thể thanh toán thêm
        if (debt.Status == DebtStatus.PaidFull)
        {
            return BadRequest(new { message = "Khoản nợ đã được thanh toán đầy đủ" });
        }

        // DP-05 / DP-08: Thanh toán vượt dư nợ còn lại -> 400
        if (debt.PaidAmount + request.Amount > debt.Amount)
        {
            return BadRequest(new { message = "Số tiền thanh toán vượt dư nợ còn lại" });
        }

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        var payment = new DebtPayment
        {
            Id = Guid.NewGuid(),
            DebtId = debt.Id,
            UserId = userId,
            WalletId = request.WalletId,
            Amount = request.Amount,
            PaymentDate = DateTime.UtcNow,
            Note = request.Note,
            IdempotencyKey = key,
            CreatedAt = DateTime.UtcNow
        };

        debt.PaidAmount += request.Amount;
        if (debt.PaidAmount >= debt.Amount)
        {
            debt.Status = DebtStatus.PaidFull;
        }
        debt.UpdatedAt = DateTime.UtcNow;
        debt.RowVersion = Guid.NewGuid();

        _context.DebtPayments.Add(payment);

        var paymentDto = new DebtPaymentDto
        {
            Id = payment.Id,
            DebtId = payment.DebtId,
            UserId = payment.UserId,
            WalletId = payment.WalletId,
            Amount = payment.Amount,
            PaymentDate = payment.PaymentDate,
            Note = payment.Note,
            IdempotencyKey = payment.IdempotencyKey,
            CreatedAt = payment.CreatedAt
        };

        var idempRecord = new IdempotencyRecord
        {
            UserId = userId,
            Key = key,
            Operation = "DebtPayment",
            RequestHash = bodyHash,
            StatusCode = 201,
            ResponseBody = JsonSerializer.Serialize(paymentDto),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        _context.IdempotencyRecords.Add(idempRecord);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(cancellationToken);
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi cập nhật khoản nợ" });
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            var dupRecord = await _context.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, cancellationToken);
            if (dupRecord != null)
            {
                if (dupRecord.RequestHash == bodyHash)
                {
                    var cachedDto = JsonSerializer.Deserialize<DebtPaymentDto>(dupRecord.ResponseBody);
                    return StatusCode(dupRecord.StatusCode, cachedDto);
                }
                return StatusCode(422, new { code = "IDEMPOTENCY_KEY_REUSED", message = "Idempotency key đã được sử dụng với payload khác" });
            }
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi ghi thanh toán nợ" });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            await tx.RollbackAsync(cancellationToken);
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi ghi thanh toán nợ" });
        }

        // Phát sự kiện để TransactionService tạo giao dịch thanh toán (D30)
        // Cho vay -> thu nợ: Thu, "Thu nợ" (DP-01)
        // Đi vay -> trả nợ: Chi, "Trả nợ" (DP-07)
        if (_eventPublisher != null)
        {
            var txType = debt.Type == DebtType.Lend ? 2 : 1; // Lend -> Income (Thu nợ), Borrow -> Expense (Trả nợ)
            var catId = debt.Type == DebtType.Lend ? CatIncomeDebtCollection : CatExpenseDebtRepayment;
            var note = debt.Type == DebtType.Lend
                ? $"Thu nợ từ {debt.PersonName}"
                : $"Trả nợ cho {debt.PersonName}";

            try
            {
                await _eventPublisher.PublishAsync(new DebtTransactionRequestedEvent
                {
                    DebtId = debt.Id,
                    PaymentId = payment.Id,
                    UserId = userId,
                    WalletId = request.WalletId,
                    Type = txType,
                    Amount = request.Amount,
                    CategoryId = catId,
                    Note = note,
                    TransactionDate = DateTime.UtcNow
                }, "debt.transaction_requested", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await tx.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Lỗi ghi Outbox sự kiện khi thanh toán nợ {DebtId}", debt.Id);
                throw;
            }
        }

        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation("Ghi nhận thanh toán {PaymentId} cho Debt {DebtId}: Amount {Amount}, Status {Status}",
            payment.Id, debt.Id, payment.Amount, debt.Status);

        return StatusCode(201, paymentDto);
    }

    [HttpGet("{id:guid}/payments")]
    public async Task<IActionResult> GetPayments(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var debt = await _context.Debts.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);
        if (debt == null)
        {
            return NotFound(new { message = "Khoản nợ không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        var payments = await _context.DebtPayments
            .Where(p => p.DebtId == id && p.UserId == userId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync(cancellationToken);

        var dtos = payments.Select(p => new DebtPaymentDto
        {
            Id = p.Id,
            DebtId = p.DebtId,
            UserId = p.UserId,
            WalletId = p.WalletId,
            Amount = p.Amount,
            PaymentDate = p.PaymentDate,
            Note = p.Note,
            IdempotencyKey = p.IdempotencyKey,
            CreatedAt = p.CreatedAt
        }).ToList();

        return Ok(dtos);
    }

    private static DebtDto ToDto(Debt d) => new()
    {
        Id = d.Id,
        UserId = d.UserId,
        Type = (int)d.Type,
        TypeName = d.Type == DebtType.Lend ? "Lend" : "Borrow",
        PersonName = d.PersonName,
        Amount = d.Amount,
        PaidAmount = d.PaidAmount,
        InterestRate = d.InterestRate,
        Currency = d.Currency,
        DueDate = d.DueDate.ToString("yyyy-MM-dd"),
        Status = d.Status switch
        {
            DebtStatus.Active => "Active",
            DebtStatus.PaidFull => "PaidFull",
            DebtStatus.Overdue => "Overdue",
            _ => "Active"
        },
        StatusCode = (int)d.Status,
        WalletId = d.WalletId,
        Note = d.Note,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt
    };

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

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

