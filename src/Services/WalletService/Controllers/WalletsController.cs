using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using WalletService.Data;
using WalletService.DTOs;
using WalletService.Models;
using WalletService.Services;

namespace WalletService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WalletsController : ControllerBase
{
    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "VND", "USD", "EUR"
    };

    private readonly WalletDbContext _context;
    private readonly IWalletLedger _walletLedger;
    private readonly IUserProfileClient _userProfileClient;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ISavingGoalClient? _savingGoalClient;
    private readonly ILogger<WalletsController> _logger;

    public WalletsController(
        WalletDbContext context,
        IWalletLedger walletLedger,
        IUserProfileClient userProfileClient,
        ILogger<WalletsController> logger,
        IEventPublisher? eventPublisher = null,
        ISavingGoalClient? savingGoalClient = null)
    {
        _context = context;
        _walletLedger = walletLedger;
        _userProfileClient = userProfileClient;
        _logger = logger;
        _eventPublisher = eventPublisher;
        _savingGoalClient = savingGoalClient;
    }

    [HttpGet]
    public async Task<IActionResult> GetWallets([FromQuery] bool includeArchived = false)
    {
        var userId = GetCurrentUserId();
        var query = _context.Wallets.AsNoTracking().Where(w => w.UserId == userId);

        if (!includeArchived)
        {
            query = query.Where(w => !w.IsArchived);
        }

        var wallets = await query
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.CreatedAt)
            .Select(w => ToDto(w))
            .ToListAsync();

        return Ok(wallets);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetWallet(Guid id)
    {
        var userId = GetCurrentUserId();
        var wallet = await _context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);

        if (wallet == null)
        {
            return NotFound(new { message = "Ví không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        return Ok(ToDto(wallet));
    }

    [Authorize(Policy = "SystemOnly")]
    [HttpGet("/internal/wallets/{id:guid}")]
    public async Task<IActionResult> GetInternalWallet(Guid id)
    {
        var wallet = await _context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id);

        if (wallet == null)
        {
            return NotFound(new { message = "Ví không tồn tại." });
        }

        return Ok(new
        {
            id = wallet.Id,
            userId = wallet.UserId,
            currency = wallet.Currency,
            isArchived = wallet.IsArchived
        });
    }

    [Authorize(Policy = "SystemOnly")]
    [HttpGet("/internal/wallets")]
    public async Task<IActionResult> GetInternalWalletsByUser([FromQuery] Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return BadRequest(new { message = "userId là bắt buộc." });
        }

        var wallets = await _context.Wallets.AsNoTracking()
            .Where(w => w.UserId == userId && !w.IsArchived)
            .Select(w => new
            {
                id = w.Id,
                userId = w.UserId,
                name = w.Name,
                currency = w.Currency,
                isArchived = w.IsArchived
            })
            .ToListAsync();

        return Ok(wallets);
    }

    [HttpPost]
    public async Task<IActionResult> CreateWallet([FromBody] CreateWalletRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();

        if (request.Type < 1 || request.Type > 4)
        {
            return BadRequest(new { message = "Loại ví không hợp lệ" });
        }

        var trimmedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 100)
        {
            return BadRequest(new { message = "Tên ví không được rỗng và tối đa 100 ký tự" });
        }

        if (request.InitialBalance < 0)
        {
            return BadRequest(new { message = "Số dư ban đầu không được âm" });
        }

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency.Trim().ToUpperInvariant();
        // FIN-130 / D29 gỡ bỏ: Hỗ trợ VND, USD, EUR. Các tiền tệ khác trả 400 UNSUPPORTED_CURRENCY
        if (!AllowedCurrencies.Contains(currency))
        {
            return BadRequest(new { code = "UNSUPPORTED_CURRENCY", message = $"Tiền tệ '{currency}' không được hỗ trợ. Chỉ hỗ trợ: {string.Join(", ", AllowedCurrencies)}." });
        }

        // D19: Unique tên trong các ví chưa archive của user, Trim và không phân biệt hoa thường
        var nameExists = await _context.Wallets.AnyAsync(w =>
            w.UserId == userId &&
            !w.IsArchived &&
            w.Name.ToLower() == trimmedName.ToLower());

        if (nameExists)
        {
            return StatusCode(409, new { code = "DUPLICATE_WALLET_NAME", message = "Tên ví đã tồn tại trong các ví đang hoạt động" });
        }

        var wallet = new Wallet
        {
            UserId = userId,
            Name = trimmedName,
            Type = (WalletType)request.Type,
            Balance = request.InitialBalance,
            InitialBalance = request.InitialBalance,
            Currency = currency,
            IsArchived = false
        };

        _context.Wallets.Add(wallet);

        // Ghi nhận lịch sử số dư ban đầu
        _context.WalletBalanceHistory.Add(new WalletBalanceHistory
        {
            WalletId = wallet.Id,
            UserId = userId,
            Reason = BalanceChangeReason.Initial,
            ChangeAmount = wallet.InitialBalance,
            BalanceAfter = wallet.InitialBalance,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return StatusCode(201, ToDto(wallet));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateWallet(Guid id, [FromBody] UpdateWalletRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);

        if (wallet == null)
        {
            return NotFound(new { message = "Ví không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        var trimmedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 100)
        {
            return BadRequest(new { message = "Tên ví không được rỗng và tối đa 100 ký tự" });
        }

        // Kiểm tra trùng tên với ví khác chưa archive
        var nameConflict = await _context.Wallets.AnyAsync(w =>
            w.Id != id &&
            w.UserId == userId &&
            !w.IsArchived &&
            w.Name.ToLower() == trimmedName.ToLower());

        if (nameConflict)
        {
            return StatusCode(409, new { code = "DUPLICATE_WALLET_NAME", message = "Tên ví đã tồn tại trong các ví đang hoạt động" });
        }

        // Đổi tiền tệ: FIN-130 hỗ trợ VND, USD, EUR, W-13 không cho đổi nếu Balance khác InitialBalance
        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            var newCurrency = request.Currency.Trim().ToUpperInvariant();
            if (!AllowedCurrencies.Contains(newCurrency))
            {
                return BadRequest(new { code = "UNSUPPORTED_CURRENCY", message = $"Tiền tệ '{newCurrency}' không được hỗ trợ. Chỉ hỗ trợ: {string.Join(", ", AllowedCurrencies)}." });
            }

            if (!string.Equals(wallet.Currency, newCurrency, StringComparison.OrdinalIgnoreCase))
            {
                if (wallet.Balance != wallet.InitialBalance)
                {
                    return BadRequest(new { message = "Không được đổi tiền tệ của ví sau khi đã phát sinh biến động số dư" });
                }
                wallet.Currency = newCurrency;
            }
        }

        // D1: PUT không được sửa Balance
        wallet.Name = trimmedName;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi cập nhật ví" });
        }

        return Ok(ToDto(wallet));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteWallet(Guid id)
    {
        var userId = GetCurrentUserId();
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);

        if (wallet == null)
        {
            return NotFound(new { message = "Ví không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        // D20: DELETE luôn là archive (soft delete), không xóa vật lý
        if (!wallet.IsArchived)
        {
            wallet.IsArchived = true;
            wallet.ArchivedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Ví đã được lưu trữ thành công" });
    }

    [HttpGet("total-assets")]
    public async Task<IActionResult> GetTotalAssets()
    {
        var userId = GetCurrentUserId();
        var activeWallets = await _context.Wallets.AsNoTracking()
            .Where(w => w.UserId == userId && !w.IsArchived)
            .ToListAsync();

        decimal totalVnd = 0m;
        bool ratesUnavailable = false;
        var excluded = new List<ExcludedWalletDto>();

        foreach (var w in activeWallets)
        {
            if (string.Equals(w.Currency, "VND", StringComparison.OrdinalIgnoreCase))
            {
                totalVnd += w.Balance;
            }
            else
            {
                var rate = await _userProfileClient.GetExchangeRateAsync(w.Currency, "VND");
                if (rate.HasValue)
                {
                    // D24: Làm tròn 0 chữ số thập phân cho VND
                    totalVnd += Math.Round(w.Balance * rate.Value, 0, MidpointRounding.AwayFromZero);
                }
                else
                {
                    ratesUnavailable = true;
                    excluded.Add(new ExcludedWalletDto
                    {
                        Id = w.Id,
                        Name = w.Name,
                        Currency = w.Currency,
                        Balance = w.Balance
                    });
                }
            }
        }

        return Ok(new TotalAssetsResponse
        {
            TotalAssetsVnd = totalVnd,
            RatesUnavailable = ratesUnavailable,
            ExcludedWallets = excluded
        });
    }

    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] TransferRequest request)
    {
        // D23 / T-05: Thiếu header hoặc không phải GUID -> 400
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !Guid.TryParse(idempotencyKey.Trim(), out var parsedKey))
        {
            return BadRequest(new { message = "Header 'Idempotency-Key' là bắt buộc và phải có định dạng GUID hợp lệ." });
        }

        var keyStr = parsedKey.ToString();
        var userId = GetCurrentUserId();

        // Validate basic request fields
        if (request.FromWalletId == request.ToWalletId)
        {
            return BadRequest(new { message = "Ví nguồn và ví đích không được trùng nhau." });
        }

        if (request.Amount <= 0 || request.Fee < 0)
        {
            return BadRequest(new { message = "Số tiền chuyển phải lớn hơn 0 và phí không được âm." });
        }

        // T-11: Số tiền không được có quá 2 chữ số thập phân
        if (decimal.Round(request.Amount, 2) != request.Amount)
        {
            return BadRequest(new { message = "Số tiền chỉ được tối đa 2 chữ số thập phân." });
        }

        if (request.Note != null && request.Note.Length > 500)
        {
            return BadRequest(new { message = "Ghi chú tối đa 500 ký tự." });
        }

        // D23: Hash body để kiểm tra idempotency
        var bodyJson = JsonSerializer.Serialize(new
        {
            request.FromWalletId,
            request.ToWalletId,
            request.Amount,
            request.Fee
        });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bodyJson)));

        // Kiểm tra Idempotency Record (D23: scope theo cặp (UserId, IdempotencyKey))
        var existingKey = await _context.IdempotencyKeys.FindAsync(userId, keyStr);
        if (existingKey != null)
        {
            if (string.Equals(existingKey.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
            {
                // Cùng key + cùng body: phát lại response cũ 200 OK, không trừ thêm (T-03)
                var cachedResponse = JsonSerializer.Deserialize<TransferResponse>(existingKey.ResponseBody);
                return existingKey.StatusCode == 200 ? Ok(cachedResponse) : StatusCode(existingKey.StatusCode, cachedResponse);
            }
            else
            {
                // Cùng key + body khác: 422 IDEMPOTENCY_KEY_REUSED (T-04)
                return StatusCode(422, new { code = "IDEMPOTENCY_KEY_REUSED", message = "Idempotency key đã được sử dụng với nội dung yêu cầu khác." });
            }
        }

        // Lấy 2 ví
        var fromWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == request.FromWalletId && w.UserId == userId);
        var toWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == request.ToWalletId && w.UserId == userId);

        // D22 / T-13 / T-14: Ví không tồn tại hoặc thuộc user khác -> 404 (chống dò ID)
        if (fromWallet == null || toWallet == null)
        {
            return NotFound(new { message = "Ví nguồn hoặc ví đích không tồn tại hoặc không thuộc quyền sở hữu." });
        }

        // T-15: Ví đã archive không thể giao dịch
        if (fromWallet.IsArchived || toWallet.IsArchived)
        {
            return BadRequest(new { message = "Không thể chuyển tiền liên quan đến ví đã lưu trữ." });
        }

        // T-16: MVP chỉ cho chuyển cùng tiền tệ
        if (!string.Equals(fromWallet.Currency, toWallet.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "MVP hiện tại chỉ hỗ trợ chuyển tiền giữa 2 ví cùng loại tiền tệ." });
        }

        // D21 / T-07 / T-09: fromWallet.Balance >= amount + fee
        var totalDeduction = request.Amount + request.Fee;
        if (fromWallet.Balance < totalDeduction)
        {
            return BadRequest(new { code = "INSUFFICIENT_BALANCE", message = "Số dư ví nguồn không đủ để thực hiện chuyển tiền (bao gồm phí)." });
        }

        // SAGA BƯỚC 1: Trừ tiền ví nguồn (Optimistic Locking)
        fromWallet.Balance -= totalDeduction;

        // SAGA BƯỚC 2: Cộng tiền ví đích qua Ledger interface (T-17 có thể ép lỗi)
        try
        {
            await _walletLedger.CreditAsync(toWallet.Id, request.Amount);
        }
        catch (Exception ex)
        {
            // COMPENSATE: Hoàn tiền lại ví nguồn
            fromWallet.Balance += totalDeduction;

            var compTransfer = new WalletTransfer
            {
                UserId = userId,
                SourceWalletId = fromWallet.Id,
                TargetWalletId = toWallet.Id,
                Amount = request.Amount,
                Fee = request.Fee,
                Note = request.Note,
                Status = TransferStatus.FailedCompensated,
                IdempotencyKey = keyStr,
                FailureReason = ex.Message
            };
            _context.WalletTransfers.Add(compTransfer);
            await _context.SaveChangesAsync();

            return StatusCode(500, new { message = "Giao dịch chuyển tiền thất bại, đã hoàn tiền lại ví nguồn." });
        }

        // SAGA BƯỚC 3: Lưu bản ghi WalletTransfers và IdempotencyRecord
        var transfer = new WalletTransfer
        {
            UserId = userId,
            SourceWalletId = fromWallet.Id,
            TargetWalletId = toWallet.Id,
            Amount = request.Amount,
            Fee = request.Fee,
            Note = request.Note,
            Status = TransferStatus.Success,
            IdempotencyKey = keyStr
        };
        _context.WalletTransfers.Add(transfer);

        var responseDto = new TransferResponse
        {
            TransferId = transfer.Id,
            NewSourceBalance = fromWallet.Balance,
            NewTargetBalance = toWallet.Balance
        };

        _context.IdempotencyKeys.Add(new IdempotencyRecord
        {
            UserId = userId,
            IdempotencyKey = keyStr,
            Operation = "WalletTransfer",
            RequestHash = requestHash,
            StatusCode = 200,
            ResponseBody = JsonSerializer.Serialize(responseDto),
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        });

        // Ghi nhận biến động sổ cái
        _context.WalletBalanceHistory.Add(new WalletBalanceHistory
        {
            WalletId = fromWallet.Id,
            UserId = userId,
            Reason = BalanceChangeReason.TransferOut,
            ChangeAmount = -totalDeduction,
            BalanceAfter = fromWallet.Balance,
            EntityId = transfer.Id
        });
        _context.WalletBalanceHistory.Add(new WalletBalanceHistory
        {
            WalletId = toWallet.Id,
            UserId = userId,
            Reason = BalanceChangeReason.TransferIn,
            ChangeAmount = request.Amount,
            BalanceAfter = toWallet.Balance,
            EntityId = transfer.Id
        });

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // D22 / T-19: Xung đột RowVersion trả 409 CONCURRENCY_CONFLICT
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi cập nhật số dư ví, vui lòng thử lại." });
        }
        catch (DbUpdateException)
        {
            // N4 / T-03b: Xung đột đồng thời do trùng IdempotencyKey (Double submit)
            _context.ChangeTracker.Clear();
            for (var retry = 0; retry < 5; retry++)
            {
                var keyRecord = await _context.IdempotencyKeys.AsNoTracking()
                    .FirstOrDefaultAsync(k => k.UserId == userId && k.IdempotencyKey == keyStr);
                if (keyRecord != null)
                {
                    if (string.Equals(keyRecord.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
                    {
                        var cachedResponse = JsonSerializer.Deserialize<TransferResponse>(keyRecord.ResponseBody);
                        return keyRecord.StatusCode == 200 ? Ok(cachedResponse) : StatusCode(keyRecord.StatusCode, cachedResponse);
                    }
                    return StatusCode(422, new { code = "IDEMPOTENCY_KEY_REUSED", message = "Idempotency key đã được sử dụng với nội dung yêu cầu khác." });
                }
                await Task.Delay(50);
            }

            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi xử lý giao dịch, vui lòng thử lại." });
        }

        // SAGA BƯỚC 4: Publish sự kiện MoneyTransferred lên RabbitMQ
        if (_eventPublisher != null)
        {
            try
            {
                var moneyEvent = new MoneyTransferredEvent
                {
                    TransferId = transfer.Id,
                    UserId = userId,
                    SourceWalletId = fromWallet.Id,
                    TargetWalletId = toWallet.Id,
                    FromWalletId = fromWallet.Id,
                    ToWalletId = toWallet.Id,
                    Amount = request.Amount,
                    Fee = request.Fee,
                    Note = request.Note
                };
                await _eventPublisher.PublishAsync(moneyEvent, "wallet.transferred");
            }
            catch (Exception ex)
            {
                // T-18: Chọn cách (a): log warning/error và giữ transfer thành công
                _logger.LogError(ex, "Lỗi khi publish MoneyTransferredEvent cho TransferId {TransferId}", transfer.Id);
            }
        }

        return Ok(responseDto);
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(idStr, out var id))
        {
            return id;
        }

        return Guid.Empty;
    }

    [HttpPost("{walletId:guid}/goal-transfer")]
    public async Task<IActionResult> GoalTransfer(
        [FromRoute] Guid walletId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] GoalTransferRequest request)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new { message = "Header 'Idempotency-Key' là bắt buộc." });
        }

        var keyStr = idempotencyKey.Trim();
        var userId = GetCurrentUserId();

        // Idempotency check
        var existingKey = await _context.IdempotencyKeys.FindAsync(userId, keyStr);
        if (existingKey != null)
        {
            var cached = JsonSerializer.Deserialize<object>(existingKey.ResponseBody);
            return StatusCode(existingKey.StatusCode, cached);
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { message = "Số tiền phải lớn hơn 0." });
        }

        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == walletId && w.UserId == userId && !w.IsArchived);
        if (wallet == null)
        {
            return NotFound(new { message = "Không tìm thấy ví hoặc ví đã bị lưu trữ." });
        }

        string goalName = "Mục tiêu tiết kiệm";
        if (_savingGoalClient != null)
        {
            var token = Request.Headers["Authorization"].ToString();
            var goal = await _savingGoalClient.GetGoalAsync(request.GoalId, token);
            if (goal == null || goal.UserId != userId)
            {
                return NotFound(new { message = "Không tìm thấy mục tiêu tiết kiệm." });
            }

            if (goal.StatusCode == 2 || string.Equals(goal.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { code = "GOAL_COMPLETED", message = "Mục tiêu đã hoàn thành, không thể nạp thêm." });
            }

            if (goal.StatusCode == 3 || string.Equals(goal.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { code = "GOAL_CANCELLED", message = "Mục tiêu đã bị hủy." });
            }

            if (!string.Equals(goal.Currency, wallet.Currency, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { code = "CURRENCY_MISMATCH", message = "Tiền tệ ví và mục tiêu không khớp." });
            }

            goalName = goal.Name;
        }

        if (wallet.Balance < request.Amount)
        {
            return BadRequest(new { code = "INSUFFICIENT_BALANCE", message = "Số dư ví không đủ." });
        }

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            wallet.Balance -= request.Amount;
            wallet.RowVersion = Guid.NewGuid().ToByteArray();

            var history = new WalletBalanceHistory
            {
                WalletId = wallet.Id,
                UserId = userId,
                Reason = BalanceChangeReason.GoalDeposit,
                ChangeAmount = -request.Amount,
                BalanceAfter = wallet.Balance,
                EntityId = request.GoalId,
                CreatedAt = DateTime.UtcNow
            };
            await _context.WalletBalanceHistory.AddAsync(history);

            var responseObj = new
            {
                newBalance = wallet.Balance,
                balance = wallet.Balance,
                walletId = wallet.Id,
                amount = request.Amount
            };
            var responseJson = JsonSerializer.Serialize(responseObj);

            _context.IdempotencyKeys.Add(new IdempotencyRecord
            {
                UserId = userId,
                IdempotencyKey = keyStr,
                Operation = "GoalTransfer",
                RequestHash = keyStr,
                StatusCode = 200,
                ResponseBody = responseJson,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            });

            await _context.SaveChangesAsync();

            if (_eventPublisher != null)
            {
                var eventId = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"goal-deposit:{userId}:{keyStr}")));
                await _eventPublisher.PublishAsync(new GoalFundsDepositedEvent
                {
                    Id = eventId,
                    GoalId = request.GoalId,
                    WalletId = wallet.Id,
                    UserId = userId,
                    Amount = request.Amount,
                    GoalName = goalName
                }, "goal.funds_deposited");
            }

            await tx.CommitAsync();

            return Ok(responseObj);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi cập nhật ví, vui lòng thử lại." });
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            for (var retry = 0; retry < 10; retry++)
            {
                var keyRecord = await _context.IdempotencyKeys.AsNoTracking()
                    .FirstOrDefaultAsync(k => k.UserId == userId && k.IdempotencyKey == keyStr);
                if (keyRecord != null)
                {
                    var cached = JsonSerializer.Deserialize<object>(keyRecord.ResponseBody);
                    return StatusCode(keyRecord.StatusCode, cached);
                }
                await Task.Delay(50);
            }

            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi nạp tiền mục tiêu." });
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5)
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            for (var retry = 0; retry < 10; retry++)
            {
                var keyRecord = await _context.IdempotencyKeys.AsNoTracking()
                    .FirstOrDefaultAsync(k => k.UserId == userId && k.IdempotencyKey == keyStr);
                if (keyRecord != null)
                {
                    var cached = JsonSerializer.Deserialize<object>(keyRecord.ResponseBody);
                    return StatusCode(keyRecord.StatusCode, cached);
                }
                await Task.Delay(50);
            }

            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Cơ sở dữ liệu đang bận, vui lòng thử lại." });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi nạp tiền mục tiêu {GoalId} từ ví {WalletId}", request.GoalId, walletId);
            return StatusCode(503, new { code = "EVENT_PUBLISH_FAILED", message = "Không thể hoàn tất giao dịch nạp tiền. Vui lòng thử lại." });
        }
    }

    private static WalletDto ToDto(Wallet w) => new()
    {
        Id = w.Id,
        UserId = w.UserId,
        Name = w.Name,
        Type = w.Type,
        Balance = w.Balance,
        InitialBalance = w.InitialBalance,
        Currency = w.Currency,
        IsDefault = w.IsDefault,
        IsArchived = w.IsArchived,
        ArchivedAt = w.ArchivedAt,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt
    };
}

public class GoalTransferRequest
{
    public Guid GoalId { get; set; }
    public decimal Amount { get; set; }
    public string Direction { get; set; } = "deposit";
}
