using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IFileServiceClient? _fileServiceClient;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        TransactionDbContext context,
        IWalletClient walletClient,
        ICategoryClient categoryClient,
        ILogger<TransactionsController> logger,
        IEventPublisher? eventPublisher)
        : this(context, walletClient, categoryClient, logger, null, eventPublisher)
    {
    }

    [ActivatorUtilitiesConstructor]
    public TransactionsController(
        TransactionDbContext context,
        IWalletClient walletClient,
        ICategoryClient categoryClient,
        ILogger<TransactionsController> logger,
        IFileServiceClient? fileServiceClient,
        IEventPublisher? eventPublisher = null)
    {
        _context = context;
        _walletClient = walletClient;
        _categoryClient = categoryClient;
        _logger = logger;
        _fileServiceClient = fileServiceClient;
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

        if (!string.IsNullOrWhiteSpace(queryParams.SourceType))
        {
            var rawSrc = queryParams.SourceType.Trim();
            if (Enum.TryParse<SourceType>(rawSrc, true, out var parsedSource))
            {
                query = query.Where(t => t.SourceType == parsedSource);
            }
            else if (int.TryParse(rawSrc, out var intSource) && Enum.IsDefined(typeof(SourceType), intSource))
            {
                var enumSrc = (SourceType)intSource;
                query = query.Where(t => t.SourceType == enumSrc);
            }
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

        if (!string.IsNullOrWhiteSpace(queryParams.TagIds))
        {
            var tagIdStrings = queryParams.TagIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var parsedTagIds = new List<Guid>();
            foreach (var s in tagIdStrings)
            {
                if (Guid.TryParse(s, out var tagGuid))
                {
                    parsedTagIds.Add(tagGuid);
                }
            }
            if (parsedTagIds.Count > 0)
            {
                var matchingTxIds = _context.TransactionTags
                    .Where(tt => parsedTagIds.Contains(tt.TagId))
                    .Select(tt => tt.TransactionId);
                query = query.Where(t => matchingTxIds.Contains(t.Id));
            }
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

        var txIds = items.Select(t => t.Id).ToList();
        var allTxTags = await _context.TransactionTags
            .AsNoTracking()
            .Where(tt => txIds.Contains(tt.TransactionId))
            .Include(tt => tt.Tag)
            .ToListAsync();
        var tagsByTxId = allTxTags.GroupBy(tt => tt.TransactionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new PagedResult<TransactionDto>
        {
            Items = items.Select(t => ToDto(t, tagsByTxId.GetValueOrDefault(t.Id))).ToList(),
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

        var tags = await _context.TransactionTags
            .AsNoTracking()
            .Where(tt => tt.TransactionId == tx.Id)
            .Include(tt => tt.Tag)
            .ToListAsync();

        return Ok(ToDto(tx, tags));
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

        // D1 & N13: REST GET sang WalletService để kiểm tra ví tồn tại, thuộc user và chưa bị archive
        var walletResult = await _walletClient.ValidateWalletAsync(request.WalletId, token);
        if (!walletResult.Exists)
        {
            return BadRequest(new { message = "Ví không tồn tại hoặc không thuộc quyền sở hữu của người dùng" });
        }
        if (walletResult.IsArchived)
        {
            return BadRequest(new { code = "WALLET_ARCHIVED", message = "Không thể tạo giao dịch cho ví đã lưu trữ/đóng" });
        }

        // Kiểm tra Category nếu được truyền
        Guid? categoryParentId = null;
        string? categoryName = null;
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

            categoryParentId = catResult.ParentId;
            categoryName = catResult.Name;
        }

        if (request.TagIds != null && request.TagIds.Count > 0)
        {
            if (request.TagIds.Count > 10)
            {
                return BadRequest(new { message = "Mỗi giao dịch tối đa 10 thẻ." });
            }
            var distinctTagIds = request.TagIds.Distinct().ToList();
            var userTagCount = await _context.Tags.CountAsync(t => t.UserId == userId && distinctTagIds.Contains(t.Id));
            if (userTagCount != distinctTagIds.Count)
            {
                return BadRequest(new { message = "Thẻ không tồn tại hoặc không thuộc quyền sở hữu của người dùng." });
            }
        }

        if (request.ReceiptFileId.HasValue)
        {
            var isAttached = await _context.Transactions.AnyAsync(t => t.ReceiptFileId == request.ReceiptFileId.Value);
            if (isAttached)
            {
                return StatusCode(409, new { code = "RECEIPT_ALREADY_ATTACHED", message = "Hóa đơn đã được gắn cho giao dịch khác." });
            }

            if (_fileServiceClient != null)
            {
                var fileResult = await _fileServiceClient.ValidateFileAsync(request.ReceiptFileId.Value, token);
                if (!fileResult.Exists || fileResult.OwnerUserId != userId || fileResult.Purpose != 1)
                {
                    return BadRequest(new { message = "Hóa đơn không tồn tại, không thuộc quyền sở hữu hoặc không phải là hóa đơn." });
                }
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
            ReceiptFileId = request.ReceiptFileId,
            RowVersion = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // D30: Mở DB transaction, lưu, publish có publisher confirm, rồi commit. Publish lỗi thì rollback và trả 503 EVENT_PUBLISH_FAILED
        using var dbTx = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.Transactions.Add(tx);
            if (request.TagIds != null && request.TagIds.Count > 0)
            {
                foreach (var tid in request.TagIds.Distinct())
                {
                    _context.TransactionTags.Add(new TransactionTag { TransactionId = tx.Id, TagId = tid });
                }
            }
            await _context.SaveChangesAsync();

            // Publish sự kiện TransactionCreatedEvent lên RabbitMQ để WalletService cập nhật số dư
            if (_eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(new TransactionCreatedEvent
                {
                    TransactionId = tx.Id,
                    UserId = tx.UserId,
                    WalletId = tx.WalletId,
                    CategoryId = tx.CategoryId ?? Guid.Empty,
                    CategoryParentId = categoryParentId,
                    CategoryName = categoryName,
                    Type = (int)tx.Type,
                    Amount = tx.Amount,
                    Currency = walletResult.Currency,
                    TransactionDate = tx.TransactionDate,
                    Note = tx.Note
                }, "transaction.created");
            }

            await dbTx.CommitAsync();

            var createdTags = new List<TransactionTag>();
            if (request.TagIds != null && request.TagIds.Count > 0)
            {
                createdTags = await _context.TransactionTags
                    .AsNoTracking()
                    .Where(tt => tt.TransactionId == tx.Id)
                    .Include(tt => tt.Tag)
                    .ToListAsync();
            }

            return StatusCode(201, ToDto(tx, createdTags));
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi lưu giao dịch hoặc publish sự kiện TransactionCreatedEvent, đã rollback DB transaction");
            return StatusCode(503, new { code = "EVENT_PUBLISH_FAILED", message = "Không thể publish sự kiện lên message broker, giao dịch đã được rollback." });
        }
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

        // D57 / DB-07: Giao dịch Debt hoặc Goal không sửa trực tiếp được
        if (tx.SourceType == SourceType.Debt || tx.SourceType == SourceType.Goal)
        {
            return BadRequest(new { code = "MANAGED_BY_SOURCE", message = "Giao dịch được quản lý bởi nguồn khác, không thể sửa trực tiếp" });
        }

        // N12: RowVersion là bắt buộc khi cập nhật giao dịch
        if (!request.RowVersion.HasValue || request.RowVersion.Value == Guid.Empty)
        {
            return BadRequest(new { code = "ROW_VERSION_REQUIRED", message = "Trường rowVersion là bắt buộc khi cập nhật giao dịch" });
        }

        // Optimistic Concurrency check
        if (request.RowVersion.Value != tx.RowVersion)
        {
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Giao dịch đã bị thay đổi bởi phiên làm việc khác, vui lòng tải lại trang" });
        }

        var token = GetBearerToken();

        // Validate ví mới nếu đổi ví (N13: kiểm tra cả isArchived) & lấy Currency (D58)
        string newWalletCurrency = "VND";
        if (request.WalletId != tx.WalletId)
        {
            var walletResult = await _walletClient.ValidateWalletAsync(request.WalletId, token);
            if (!walletResult.Exists)
            {
                return BadRequest(new { message = "Ví mới không tồn tại hoặc không thuộc quyền sở hữu của người dùng" });
            }
            if (walletResult.IsArchived)
            {
                return BadRequest(new { code = "WALLET_ARCHIVED", message = "Không thể chuyển giao dịch sang ví đã lưu trữ/đóng" });
            }
            newWalletCurrency = walletResult.Currency;
        }
        else
        {
            var walletResult = await _walletClient.ValidateWalletAsync(request.WalletId, token);
            if (walletResult.Exists)
            {
                newWalletCurrency = walletResult.Currency;
            }
        }

        if (request.TagIds != null)
        {
            if (request.TagIds.Count > 10)
            {
                return BadRequest(new { message = "Mỗi giao dịch tối đa 10 thẻ." });
            }
            var distinctTagIds = request.TagIds.Distinct().ToList();
            if (distinctTagIds.Count > 0)
            {
                var userTagCount = await _context.Tags.CountAsync(t => t.UserId == userId && distinctTagIds.Contains(t.Id));
                if (userTagCount != distinctTagIds.Count)
                {
                    return BadRequest(new { message = "Thẻ không tồn tại hoặc không thuộc quyền sở hữu của người dùng." });
                }
            }
        }

        if (request.ReceiptFileId.HasValue)
        {
            var isAttached = await _context.Transactions.AnyAsync(t => t.ReceiptFileId == request.ReceiptFileId.Value && t.Id != id);
            if (isAttached)
            {
                return StatusCode(409, new { code = "RECEIPT_ALREADY_ATTACHED", message = "Một file chỉ gắn 1 giao dịch." });
            }

            if (_fileServiceClient != null)
            {
                var fileResult = await _fileServiceClient.ValidateFileAsync(request.ReceiptFileId.Value, token);
                if (!fileResult.Exists || fileResult.OwnerUserId != userId || fileResult.Purpose != 1)
                {
                    return BadRequest(new { message = "File của user khác hoặc không hợp lệ." });
                }
            }
        }

        Guid? oldCategoryParentId = null;
        string? oldCategoryName = null;
        if (tx.CategoryId.HasValue)
        {
            var oldCatResult = await _categoryClient.ValidateCategoryAsync(tx.CategoryId.Value, token);
            if (oldCatResult.Exists)
            {
                oldCategoryParentId = oldCatResult.ParentId;
                oldCategoryName = oldCatResult.Name;
            }
        }

        Guid? newCategoryParentId = null;
        string? newCategoryName = null;
        // Validate danh mục nếu có
        if (request.CategoryId.HasValue)
        {
            if (request.CategoryId == tx.CategoryId)
            {
                newCategoryParentId = oldCategoryParentId;
                newCategoryName = oldCategoryName;
            }
            else
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

                newCategoryParentId = catResult.ParentId;
                newCategoryName = catResult.Name;
            }
        }

        var oldWalletId = tx.WalletId;
        var oldCategoryId = tx.CategoryId ?? Guid.Empty;
        var oldType = (int)tx.Type;
        var oldAmount = tx.Amount;

        // D30: Mở DB transaction, cập nhật, publish có publisher confirm, rồi commit. Publish lỗi thì rollback và trả 503 EVENT_PUBLISH_FAILED
        using var dbTx = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.Entry(tx).Property(t => t.RowVersion).OriginalValue = request.RowVersion.Value;

            tx.WalletId = request.WalletId;
            tx.CategoryId = request.CategoryId;
            tx.Type = (TransactionType)request.Type;
            tx.Amount = request.Amount;
            tx.TransactionDate = request.TransactionDate == default ? tx.TransactionDate : request.TransactionDate;
            tx.Note = request.Note?.Trim();
            if (request.ReceiptFileId.HasValue)
            {
                tx.ReceiptFileId = request.ReceiptFileId.Value;
            }
            tx.RowVersion = Guid.NewGuid();
            tx.UpdatedAt = DateTime.UtcNow;

            if (request.TagIds != null)
            {
                var existingTags = await _context.TransactionTags.Where(tt => tt.TransactionId == tx.Id).ToListAsync();
                _context.TransactionTags.RemoveRange(existingTags);
                foreach (var tid in request.TagIds.Distinct())
                {
                    _context.TransactionTags.Add(new TransactionTag { TransactionId = tx.Id, TagId = tid });
                }
            }

            await _context.SaveChangesAsync();

            // Publish sự kiện TransactionUpdatedEvent lên RabbitMQ để WalletService cập nhật số dư chênh lệch
            if (_eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(new TransactionUpdatedEvent
                {
                    TransactionId = tx.Id,
                    UserId = tx.UserId,
                    OldWalletId = oldWalletId,
                    NewWalletId = tx.WalletId,
                    OldCategoryId = oldCategoryId,
                    NewCategoryId = tx.CategoryId ?? Guid.Empty,
                    OldCategoryParentId = oldCategoryParentId,
                    NewCategoryParentId = newCategoryParentId,
                    CategoryName = newCategoryName,
                    OldType = oldType,
                    NewType = (int)tx.Type,
                    OldAmount = oldAmount,
                    NewAmount = tx.Amount,
                    Currency = newWalletCurrency,
                    TransactionDate = tx.TransactionDate
                }, "transaction.updated");
            }

            await dbTx.CommitAsync();

            var updatedTags = await _context.TransactionTags
                .AsNoTracking()
                .Where(tt => tt.TransactionId == tx.Id)
                .Include(tt => tt.Tag)
                .ToListAsync();

            return Ok(ToDto(tx, updatedTags));
        }
        catch (DbUpdateConcurrencyException)
        {
            await dbTx.RollbackAsync();
            return StatusCode(409, new { code = "CONCURRENCY_CONFLICT", message = "Xung đột đồng thời khi cập nhật giao dịch" });
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi cập nhật giao dịch hoặc publish sự kiện TransactionUpdatedEvent, đã rollback DB transaction");
            return StatusCode(503, new { code = "EVENT_PUBLISH_FAILED", message = "Không thể publish sự kiện lên message broker, giao dịch đã được rollback." });
        }
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

        // D57 / DB-07: Giao dịch Debt hoặc Goal không xóa trực tiếp được
        if (tx.SourceType == SourceType.Debt || tx.SourceType == SourceType.Goal)
        {
            return BadRequest(new { code = "MANAGED_BY_SOURCE", message = "Giao dịch được quản lý bởi nguồn khác, không thể xóa trực tiếp" });
        }

        // D30: Mở DB transaction, xóa, publish có publisher confirm, rồi commit. Publish lỗi thì rollback và trả 503 EVENT_PUBLISH_FAILED
        var token = GetBearerToken();
        Guid? categoryParentId = null;
        string? categoryName = null;
        if (tx.CategoryId.HasValue)
        {
            var catResult = await _categoryClient.ValidateCategoryAsync(tx.CategoryId.Value, token);
            if (catResult.Exists)
            {
                categoryParentId = catResult.ParentId;
                categoryName = catResult.Name;
            }
        }

        string walletCurrency = "VND";
        var walletRes = await _walletClient.ValidateWalletAsync(tx.WalletId, token);
        if (walletRes.Exists)
        {
            walletCurrency = walletRes.Currency;
        }

        using var dbTx = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.Transactions.Remove(tx);
            await _context.SaveChangesAsync();

            // Publish sự kiện TransactionDeletedEvent để hoàn tiền lại ví
            if (_eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(new TransactionDeletedEvent
                {
                    TransactionId = tx.Id,
                    UserId = tx.UserId,
                    WalletId = tx.WalletId,
                    CategoryId = tx.CategoryId ?? Guid.Empty,
                    CategoryParentId = categoryParentId,
                    CategoryName = categoryName,
                    Type = (int)tx.Type,
                    Amount = tx.Amount,
                    Currency = walletCurrency,
                    ReceiptFileId = tx.ReceiptFileId
                }, "transaction.deleted");
            }

            await dbTx.CommitAsync();
            return Ok(new { message = "Xóa giao dịch thành công" });
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi xóa giao dịch hoặc publish sự kiện TransactionDeletedEvent, đã rollback DB transaction");
            return StatusCode(503, new { code = "EVENT_PUBLISH_FAILED", message = "Không thể publish sự kiện lên message broker, giao dịch đã được rollback." });
        }
    }

    /// <summary>
    /// AC9 (từ Sprint 2 / D27): Chuyển toàn bộ giao dịch của danh mục cũ sang danh mục thay thế khi xóa danh mục.
    /// N14: Kiểm tra danh mục thay thế tồn tại, thuộc user, và cùng loại (Thu/Chi) trước khi chuyển.
    /// Ghi nhận S4: Reassign cần phát sự kiện để BudgetService tính toán lại hạn mức chi tiêu.
    /// </summary>
    [HttpPost("reassign-category")]
    public async Task<IActionResult> ReassignCategory([FromBody] ReassignCategoryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();
        var token = GetBearerToken();

        // N14: Kiểm tra danh mục thay thế qua CategoryClient
        var replacementResult = await _categoryClient.ValidateCategoryAsync(request.ReplacementCategoryId, token);
        if (!replacementResult.Exists)
        {
            return BadRequest(new { message = "Danh mục thay thế không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        var affectedTransactions = await _context.Transactions
            .Where(t => t.UserId == userId && t.CategoryId == request.OldCategoryId)
            .ToListAsync();

        if (affectedTransactions.Count > 0 && replacementResult.Type.HasValue)
        {
            if (affectedTransactions.Any(t => (int)t.Type != replacementResult.Type.Value))
            {
                return BadRequest(new { message = "Danh mục thay thế không cùng loại (Thu/Chi) với các giao dịch được chuyển" });
            }
        }

        var oldCatResult = await _categoryClient.ValidateCategoryAsync(request.OldCategoryId, token);

        // D30/D38: Mở DB transaction, cập nhật, publish TransactionUpdatedEvent cho từng giao dịch, commit
        using var dbTx = await _context.Database.BeginTransactionAsync();
        try
        {
            foreach (var tx in affectedTransactions)
            {
                tx.CategoryId = request.ReplacementCategoryId;
                tx.UpdatedAt = DateTime.UtcNow;
                tx.RowVersion = Guid.NewGuid();

                if (_eventPublisher != null)
                {
                    var walletRes = await _walletClient.ValidateWalletAsync(tx.WalletId, token);
                    await _eventPublisher.PublishAsync(new TransactionUpdatedEvent
                    {
                        TransactionId = tx.Id,
                        UserId = tx.UserId,
                        OldWalletId = tx.WalletId,
                        NewWalletId = tx.WalletId,
                        OldCategoryId = request.OldCategoryId,
                        NewCategoryId = request.ReplacementCategoryId,
                        OldCategoryParentId = oldCatResult.Exists ? oldCatResult.ParentId : null,
                        NewCategoryParentId = replacementResult.ParentId,
                        CategoryName = replacementResult.Name,
                        OldType = (int)tx.Type,
                        NewType = (int)tx.Type,
                        OldAmount = tx.Amount,
                        NewAmount = tx.Amount,
                        Currency = walletRes.Exists ? walletRes.Currency : "VND",
                        TransactionDate = tx.TransactionDate
                    }, "transaction.updated");
                }
            }

            var count = await _context.SaveChangesAsync();
            await dbTx.CommitAsync();
            return Ok(new { message = $"Đã chuyển {affectedTransactions.Count} giao dịch sang danh mục mới", count = affectedTransactions.Count });
        }
        catch (Exception ex)
        {
            await dbTx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi chuyển giao dịch hoặc publish TransactionUpdatedEvent cho danh mục thay thế, đã rollback DB transaction");
            return StatusCode(503, new { code = "EVENT_PUBLISH_FAILED", message = "Không thể publish sự kiện lên message broker, thao tác đã được rollback." });
        }
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

    private static TransactionDto ToDto(Transaction tx, List<TransactionTag>? tags = null)
    {
        var dto = new TransactionDto
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
            ReceiptFileId = tx.ReceiptFileId,
            SourceType = tx.SourceType.ToString(),
            SourceId = tx.SourceId,
            OccurrenceDate = tx.OccurrenceDate,
            RowVersion = tx.RowVersion,
            CreatedAt = tx.CreatedAt,
            UpdatedAt = tx.UpdatedAt
        };

        if (tags != null)
        {
            dto.TagIds = tags.Select(t => t.TagId).ToList();
            dto.Tags = tags.Where(t => t.Tag != null).Select(t => new TagDto
            {
                Id = t.Tag!.Id,
                UserId = t.Tag.UserId,
                Name = t.Tag.Name,
                Color = t.Tag.Color,
                CreatedAt = t.Tag.CreatedAt
            }).ToList();
        }

        return dto;
    }
}

