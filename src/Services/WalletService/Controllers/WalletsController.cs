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
    private readonly ILogger<WalletsController> _logger;

    public WalletsController(
        WalletDbContext context,
        IWalletLedger walletLedger,
        IUserProfileClient userProfileClient,
        ILogger<WalletsController> logger,
        IEventPublisher? eventPublisher = null)
    {
        _context = context;
        _walletLedger = walletLedger;
        _userProfileClient = userProfileClient;
        _logger = logger;
        _eventPublisher = eventPublisher;
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
        if (!AllowedCurrencies.Contains(currency))
        {
            return BadRequest(new { message = $"Đơn vị tiền tệ '{currency}' không được hỗ trợ" });
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

        // Đổi tiền tệ: W-13 không cho đổi nếu Balance khác InitialBalance
        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            var newCurrency = request.Currency.Trim().ToUpperInvariant();
            if (!AllowedCurrencies.Contains(newCurrency))
            {
                return BadRequest(new { message = $"Đơn vị tiền tệ '{newCurrency}' không được hỗ trợ" });
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
