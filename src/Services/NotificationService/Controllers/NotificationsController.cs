using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedKernel.DTOs;
using NotificationService.Data;
using NotificationService.DTOs;
using NotificationService.Models;

namespace NotificationService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly NotificationDbContext _context;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(NotificationDbContext context, ILogger<NotificationsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// D37, N-03, N-04: Lấy danh sách thông báo của người dùng đăng nhập, phân trang, mới nhất trước
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] NotificationQueryParameters queryParams)
    {
        var userId = GetCurrentUserId();
        var page = queryParams.Page < 1 ? 1 : queryParams.Page;
        var pageSize = queryParams.PageSize < 1 ? 20 : (queryParams.PageSize > 100 ? 100 : queryParams.PageSize);

        var query = _context.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt);

        var totalItems = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var dtos = items.Select(ToDto).ToList();
        var result = new PagedResult<NotificationDto>(dtos, totalItems, page, pageSize);

        return Ok(result);
    }

    /// <summary>FIN-114 / NT-01: Số lượng thông báo chưa đọc của người dùng đăng nhập.</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = GetCurrentUserId();
        var count = await _context.Notifications.AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead);
        return Ok(new { count });
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var userId = GetCurrentUserId();
        var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

        if (notification == null)
        {
            return NotFound(new { message = "Thông báo không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        notification.IsRead = true;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Đã đánh dấu đã đọc" });
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = GetCurrentUserId();
        var unreadNotifications = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var n in unreadNotifications)
        {
            n.IsRead = true;
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = $"Đã đánh dấu đã đọc {unreadNotifications.Count} thông báo", count = unreadNotifications.Count });
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }

    private static NotificationDto ToDto(Notification n)
    {
        return new NotificationDto
        {
            Id = n.Id,
            UserId = n.UserId,
            Type = n.Type,
            Title = n.Title,
            Message = n.Message,
            IsRead = n.IsRead,
            Metadata = n.Metadata,
            CreatedAt = n.CreatedAt
        };
    }
}

