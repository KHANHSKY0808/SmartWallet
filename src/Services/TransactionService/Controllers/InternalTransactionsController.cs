using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransactionService.Data;

namespace TransactionService.Controllers;

/// <summary>
/// Endpoint nội bộ phục vụ ReportService backfill bản chiếu TransactionProjections (D41 & D58).
/// Chỉ chấp nhận JWT có role = "System" (RP-10).
/// </summary>
[ApiController]
[Route("internal/transactions")]
[Authorize(Policy = "SystemOnly")]
public class InternalTransactionsController : ControllerBase
{
    private readonly TransactionDbContext _context;

    public InternalTransactionsController(TransactionDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllTransactions([FromQuery] int page = 1, [FromQuery] int pageSize = 500, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 1000);

        var query = _context.Transactions.AsNoTracking().OrderBy(t => t.CreatedAt);
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.UserId,
                t.WalletId,
                t.CategoryId,
                Type = (int)t.Type,
                t.Amount,
                t.TransactionDate,
                t.Note,
                SourceType = (int)t.SourceType,
                t.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            items,
            totalCount,
            page,
            pageSize
        });
    }
}

