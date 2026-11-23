using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Data;
using TransactionService.DTOs;
using TransactionService.Models;
using TransactionService.Services;

namespace TransactionService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly TransactionDbContext _context;
    private readonly IWalletClient _walletClient;
    private readonly ICategoryClient _categoryClient;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        TransactionDbContext context,
        IWalletClient walletClient,
        ICategoryClient categoryClient,
        ILogger<TransactionsController> logger,
        IEventPublisher? eventPublisher = null)
    {
        _context = context;
        _walletClient = walletClient;
        _categoryClient = categoryClient;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    [HttpGet]
    public async Task<IActionResult> GetTransactions([FromQuery] TransactionQueryParameters queryParams)
    {
        var userId = GetCurrentUserId();
        var query = _context.Transactions.AsNoTracking().Where(t => t.UserId == userId);

        if (queryParams.StartDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate >= queryParams.StartDate.Value);
        }

        if (queryParams.EndDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate <= queryParams.EndDate.Value);
        }

        if (queryParams.WalletId.HasValue)
        {
            query = query.Where(t => t.WalletId == queryParams.WalletId.Value);
        }

        if (queryParams.CategoryId.HasValue)
        {
            query = query.Where(t => t.CategoryId == queryParams.CategoryId.Value);
        }

        if (queryParams.Type.HasValue)
        {
            query = query.Where(t => (int)t.Type == queryParams.Type.Value);
        }

        if (queryParams.MinAmount.HasValue)
        {
            query = query.Where(t => t.Amount >= queryParams.MinAmount.Value);
        }

        if (queryParams.MaxAmount.HasValue)
        {
            query = query.Where(t => t.Amount <= queryParams.MaxAmount.Value);
        }

        if (!string.IsNullOrWhiteSpace(queryParams.Keyword))
        {
            var kw = queryParams.Keyword.Trim().ToLower();
            query = query.Where(t => t.Note != null && t.Note.ToLower().Contains(kw));
        }

        var totalItems = await query.CountAsync();

        // Sắp xếp
        var isAsc = string.Equals(queryParams.SortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (queryParams.SortBy?.ToLowerInvariant()) switch
        {
            "amount" => isAsc ? query.OrderBy(t => t.Amount) : query.OrderByDescending(t => t.Amount),
            "createdat" => isAsc ? query.OrderBy(t => t.CreatedAt) : query.OrderByDescending(t => t.CreatedAt),
            _ => isAsc ? query.OrderBy(t => t.TransactionDate) : query.OrderByDescending(t => t.TransactionDate)
        };

        var skip = (queryParams.Page - 1) * queryParams.PageSize;
        var items = await query.Skip(skip).Take(queryParams.PageSize).ToListAsync();

        var result = new PagedResult<TransactionDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalItems = totalItems,
            Page = queryParams.Page,
            PageSize = queryParams.PageSize
        };

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTransaction(Guid id)
    {
        var userId = GetCurrentUserId();
        var tx = await _context.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (tx == null)
        {
            return NotFound(new { message = "Giao dịch không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        return Ok(ToDto(tx));
    }

    [HttpPost]
    public async Task<IActionResult> CreateTransaction([FromBody] CreateTransactionRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();
        var token = GetBearerToken();

        // D1: REST GET sang WalletService để kiểm tra ví tồn tại và thuộc user
        var walletValid = await _walletClient.ValidateWalletExistsAsync(request.WalletId, token);
        if (!walletValid)
        {
            return BadRequest(new { message = "Ví không tồn tại hoặc không thuộc quyền sở hữu của người dùng" });
        }

        // Kiểm tra Category nếu được truyền
        if (request.CategoryId.HasValue)
        {
            var catResult = await _categoryClient.ValidateCategoryAsync(request.CategoryId.Value, token);
            if (!catResult.Exists)
            {
                return BadRequest(new { message = "Danh mục không tồn tại hoặc không thuộc quyền sở hữu" });
            }

            if (catResult.Type.HasValue && catResult.Type.Value != request.Type)
            {
                return BadRequest(new { message = "Loại danh mục không khớp với loại giao dịch (Thu/Chi)" });
            }
        }

        var tx = new Transaction
        {
            UserId = userId,
            WalletId = request.WalletId,
            CategoryId = request.CategoryId,
            Type = (TransactionType)request.Type,
            Amount = request.Amount,
            TransactionDate = request.TransactionDate == default ? DateTime.UtcNow : request.TransactionDate,
            Note = request.Note?.Trim(),
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Transactions.Add(tx);
        await _context.SaveChangesAsync();

        // Publish sự kiện TransactionCreatedEvent lên RabbitMQ để WalletService cập nhật số dư
        if (_eventPublisher != null)
        {
            try
            {
                await _eventPublisher.PublishAsync(new TransactionCreatedEvent
                {
                    TransactionId = tx.Id,
                    UserId = tx.UserId,
                    WalletId = tx.WalletId,
                    CategoryId = tx.CategoryId ?? Guid.Empty,
                    Type = (int)tx.Type,
                    Amount = tx.Amount,
                    TransactionDate = tx.TransactionDate,
                    Note = tx.Note
                }, "transaction.created");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi publish sự kiện TransactionCreatedEvent");
            }
        }

        return StatusCode(201, ToDto(tx));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTransaction(Guid id, [FromBody] UpdateTransactionRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();
        var tx = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (tx == null)
        {
            return NotFound(new { message = "Giao dịch không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        if (tx.Type == TransactionType.Transfer)
        {
            return BadRequest(new { message = "Không thể chỉnh sửa trực tiếp giao dịch chuyển tiền" });
        }

        // Optimistic Concurrency check
        if (request.RowVersion.HasValue && request.RowVersion.Value != tx.RowVersion)
        {
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Giao dịch đã bị thay đổi bởi phiên làm việc khác, vui lòng tải lại trang" });
        }

        var token = GetBearerToken();

        // Validate ví mới nếu đổi ví
        if (request.WalletId != tx.WalletId)
        {
            var walletValid = await _walletClient.ValidateWalletExistsAsync(request.WalletId, token);
            if (!walletValid)
            {
                return BadRequest(new { message = "Ví mới không tồn tại hoặc không thuộc quyền sở hữu của người dùng" });
            }
        }

        // Validate danh mục nếu có
        if (request.CategoryId.HasValue && request.CategoryId != tx.CategoryId)
        {
            var catResult = await _categoryClient.ValidateCategoryAsync(request.CategoryId.Value, token);
            if (!catResult.Exists)
            {
                return BadRequest(new { message = "Danh mục không tồn tại hoặc không thuộc quyền sở hữu" });
            }

            if (catResult.Type.HasValue && catResult.Type.Value != request.Type)
            {
                return BadRequest(new { message = "Loại danh mục không khớp với loại giao dịch (Thu/Chi)" });
            }
        }

        var oldWalletId = tx.WalletId;
        var oldCategoryId = tx.CategoryId ?? Guid.Empty;
        var oldType = (int)tx.Type;
        var oldAmount = tx.Amount;

        tx.WalletId = request.WalletId;
        tx.CategoryId = request.CategoryId;
        tx.Type = (TransactionType)request.Type;
        tx.Amount = request.Amount;
        tx.TransactionDate = request.TransactionDate == default ? tx.TransactionDate : request.TransactionDate;
        tx.Note = request.Note?.Trim();
        tx.RowVersion = Guid.NewGuid();
        tx.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi cập nhật giao dịch" });
        }

        // Publish sự kiện TransactionUpdatedEvent lên RabbitMQ để WalletService cập nhật số dư chênh lệch
        if (_eventPublisher != null)
        {
            try
            {
                await _eventPublisher.PublishAsync(new TransactionUpdatedEvent
                {
                    TransactionId = tx.Id,
                    UserId = tx.UserId,
                    OldWalletId = oldWalletId,
                    NewWalletId = tx.WalletId,
                    OldCategoryId = oldCategoryId,
                    NewCategoryId = tx.CategoryId ?? Guid.Empty,
                    OldType = oldType,
                    NewType = (int)tx.Type,
                    OldAmount = oldAmount,
                    NewAmount = tx.Amount,
                    TransactionDate = tx.TransactionDate
                }, "transaction.updated");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi publish sự kiện TransactionUpdatedEvent");
            }
        }

        return Ok(ToDto(tx));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTransaction(Guid id)
    {
        var userId = GetCurrentUserId();
        var tx = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (tx == null)
        {
            return NotFound(new { message = "Giao dịch không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        if (tx.Type == TransactionType.Transfer)
        {
            return BadRequest(new { message = "Không thể xóa trực tiếp giao dịch chuyển tiền" });
        }

        _context.Transactions.Remove(tx);
        await _context.SaveChangesAsync();

        // Publish sự kiện TransactionDeletedEvent để hoàn tiền lại ví
        if (_eventPublisher != null)
        {
            try
            {
                await _eventPublisher.PublishAsync(new TransactionDeletedEvent
                {
                    TransactionId = tx.Id,
                    UserId = tx.UserId,
                    WalletId = tx.WalletId,
                    CategoryId = tx.CategoryId ?? Guid.Empty,
                    Type = (int)tx.Type,
                    Amount = tx.Amount
                }, "transaction.deleted");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi publish sự kiện TransactionDeletedEvent");
            }
        }

        return Ok(new { message = "Xóa giao dịch thành công" });
    }

    /// <summary>
    /// AC9 (từ Sprint 2 / D27): Chuyển toàn bộ giao dịch của danh mục cũ sang danh mục thay thế khi xóa danh mục.
    /// </summary>
    [HttpPost("reassign-category")]
    public async Task<IActionResult> ReassignCategory([FromBody] ReassignCategoryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();

        var affectedTransactions = await _context.Transactions
            .Where(t => t.UserId == userId && t.CategoryId == request.OldCategoryId)
            .ToListAsync();

        foreach (var tx in affectedTransactions)
        {
            tx.CategoryId = request.ReplacementCategoryId;
            tx.UpdatedAt = DateTime.UtcNow;
            tx.RowVersion = Guid.NewGuid();
        }

        var count = await _context.SaveChangesAsync();
        return Ok(new { message = $"Đã chuyển {affectedTransactions.Count} giao dịch sang danh mục mới", count = affectedTransactions.Count });
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

    private static TransactionDto ToDto(Transaction tx)
    {
        return new TransactionDto
        {
            Id = tx.Id,
            UserId = tx.UserId,
            WalletId = tx.WalletId,
            CategoryId = tx.CategoryId,
            Type = (int)tx.Type,
            TypeName = tx.Type switch
            {
                TransactionType.Expense => "Expense",
                TransactionType.Income => "Income",
                TransactionType.Transfer => "Transfer",
                _ => "Unknown"
            },
            Amount = tx.Amount,
            TransactionDate = tx.TransactionDate,
            Note = tx.Note,
            ReceiptImageUrl = tx.ReceiptImageUrl,
            RowVersion = tx.RowVersion,
            CreatedAt = tx.CreatedAt,
            UpdatedAt = tx.UpdatedAt
        };
    }
}

