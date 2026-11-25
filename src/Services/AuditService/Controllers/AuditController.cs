using AuditService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;

namespace AuditService.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = AppPermissions.AuditRead)]
public class AuditController : ControllerBase
{
    private readonly AuditDbContext _context;

    public AuditController(AuditDbContext context)
    {
        _context = context;
    }

    /// <summary>D63 / FIN-119: Tra cứu nhật ký kiểm toán bất biến (mới nhất trước, phân trang tối đa 100).</summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? userId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? serviceName = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (pageSize > 100 || pageSize < 1)
        {
            return BadRequest(new { message = "pageSize phải nằm trong khoảng 1 đến 100" });
        }

        if (page < 1) page = 1;

        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(a => a.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var term = action.Trim().ToLower();
            query = query.Where(a => a.Action.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            var svc = serviceName.Trim().ToLower();
            query = query.Where(a => a.ServiceName.ToLower().Contains(svc));
        }

        if (from.HasValue)
        {
            var f = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
            query = query.Where(a => a.Timestamp >= f);
        }

        if (to.HasValue)
        {
            var t = to.Value.TimeOfDay == TimeSpan.Zero
                ? DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.SpecifyKind(to.Value, DateTimeKind.Utc);
            query = query.Where(a => a.Timestamp <= t);
        }

        var totalItems = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                id = a.Id,
                eventId = a.EventId,
                userId = a.UserId,
                serviceName = a.ServiceName,
                action = a.Action,
                entityType = a.EntityType,
                entityId = a.EntityId,
                oldValueJson = a.OldValueJson,
                newValueJson = a.NewValueJson,
                ipAddress = a.IpAddress,
                correlationId = a.CorrelationId,
                timestamp = a.Timestamp
            })
            .ToListAsync();

        return Ok(new
        {
            items,
            totalItems,
            totalCount = totalItems,
            page,
            pageSize
        });
    }
}

