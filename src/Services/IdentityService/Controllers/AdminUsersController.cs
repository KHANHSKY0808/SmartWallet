using System.Security.Claims;
using IdentityService.Data;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Extensions;
using SharedKernel.Messaging;

namespace IdentityService.Controllers;

public record LockUserRequest(string? Reason = null);
public record UpdateUserRolesRequest(List<string> Roles);

[ApiController]
[Route("api/admin")]
public class AdminUsersController : ControllerBase
{
    private readonly IdentityDbContext _context;
    private readonly ISessionRevocationStore _revocationStore;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        IdentityDbContext context,
        ISessionRevocationStore revocationStore,
        ILogger<AdminUsersController> logger,
        IEventPublisher? eventPublisher = null)
    {
        _context = context;
        _revocationStore = revocationStore;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    [HttpGet("users")]
    [Authorize(Policy = AppPermissions.UsersRead)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new { message = "pageSize phải nằm trong khoảng 1 đến 100" });
        }

        var query = _context.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(term)
                || (u.FullName != null && u.FullName.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<UserStatus>(status.Trim(), ignoreCase: true, out var parsedStatus))
            {
                query = query.Where(u => u.Status == parsedStatus);
            }
            else
            {
                return BadRequest(new { message = "Trạng thái không hợp lệ" });
            }
        }

        var totalItems = await query.CountAsync();
        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = users.Select(u => new
        {
            id = u.Id,
            email = u.Email,
            fullName = u.FullName ?? u.Email.Split('@')[0],
            status = u.Status.ToString(),
            roles = u.UserRoles.Select(ur => ur.Role.Name).ToList(),
            lockReason = u.LockReason,
            lockedAt = u.LockedAt,
            createdAt = u.CreatedAt
        }).ToList();

        return Ok(new
        {
            items,
            totalItems,
            totalCount = totalItems,
            page,
            pageSize
        });
    }

    [HttpPut("users/{id:guid}/lock")]
    [Authorize(Policy = AppPermissions.UsersLock)]
    public async Task<IActionResult> LockUser(Guid id, [FromBody] LockUserRequest? request = null)
    {
        var actorId = GetCurrentUserId();
        if (actorId != Guid.Empty && actorId == id)
        {
            return Conflict(new { code = "CANNOT_LOCK_SELF", message = "Không thể tự khóa tài khoản của chính mình." });
        }

        var user = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound(new { message = "Người dùng không tồn tại." });
        }

        var isTargetAdmin = user.UserRoles.Any(ur => string.Equals(ur.Role.Name, "Admin", StringComparison.OrdinalIgnoreCase));
        if (isTargetAdmin && user.Status != UserStatus.Locked)
        {
            var activeAdminCount = await _context.Users
                .CountAsync(u => u.Status != UserStatus.Locked
                    && u.UserRoles.Any(ur => ur.Role.Name == "Admin"));

            if (activeAdminCount <= 1)
            {
                return Conflict(new { code = "LAST_ADMIN", message = "Không thể khóa quản trị viên cuối cùng đang hoạt động." });
            }
        }

        var now = DateTime.UtcNow;
        user.Status = UserStatus.Locked;
        user.LockReason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request!.Reason!.Trim();
        user.LockedAt = now;
        user.LockedByUserId = actorId == Guid.Empty ? null : actorId;
        user.UpdatedAt = now;

        // D64: Thu hồi toàn bộ RefreshToken của user
        var activeRefreshTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && !r.IsRevoked)
            .ToListAsync();
        foreach (var rt in activeRefreshTokens)
        {
            rt.IsRevoked = true;
        }

        await _context.SaveChangesAsync();

        // D64: Ghi auth:locked và auth:valid-after vào Redis
        await _revocationStore.SetLockedAsync(user.Id);

        // D63: Phát UserLockedEvent để AuditService ghi nhận
        await TryPublishAuditAsync(new UserLockedEvent
        {
            UserId = user.Id,
            ActorUserId = actorId,
            Reason = user.LockReason
        }, "identity.user_locked");

        return Ok(new { message = "Đã khóa tài khoản thành công.", userId = user.Id, status = user.Status.ToString() });
    }

    [HttpPut("users/{id:guid}/unlock")]
    [Authorize(Policy = AppPermissions.UsersLock)]
    public async Task<IActionResult> UnlockUser(Guid id)
    {
        var actorId = GetCurrentUserId();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
        {
            return NotFound(new { message = "Người dùng không tồn tại." });
        }

        user.Status = UserStatus.Active;
        user.LockReason = null;
        user.LockedAt = null;
        user.LockedByUserId = null;
        user.LockoutEnd = null;
        user.FailedLoginAttempts = 0;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // D64: Xóa auth:locked, GIỮ auth:valid-after để token cũ trước khi khóa vẫn bị từ chối
        await _revocationStore.ClearLockedAsync(user.Id);

        // D63: Phát UserUnlockedEvent để AuditService ghi nhận
        await TryPublishAuditAsync(new UserUnlockedEvent
        {
            UserId = user.Id,
            ActorUserId = actorId
        }, "identity.user_unlocked");

        return Ok(new { message = "Đã mở khóa tài khoản thành công.", userId = user.Id, status = user.Status.ToString() });
    }

    [HttpGet("roles")]
    [Authorize(Policy = AppPermissions.UsersRead)]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _context.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.Id)
            .ToListAsync();

        var result = roles.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            description = r.Description,
            permissions = r.RolePermissions.Select(rp => rp.Permission.Code).OrderBy(c => c).ToList()
        }).ToList();

        return Ok(result);
    }

    [HttpPut("users/{id:guid}/roles")]
    [Authorize(Policy = AppPermissions.UsersRolesWrite)]
    public async Task<IActionResult> UpdateUserRoles(Guid id, [FromBody] UpdateUserRolesRequest request)
    {
        if (request?.Roles == null || request.Roles.Count == 0)
        {
            return BadRequest(new { message = "Danh sách vai trò không được rỗng." });
        }

        var distinctInput = request.Roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctInput.Count == 0)
        {
            return BadRequest(new { message = "Danh sách vai trò không hợp lệ." });
        }

        var allRoles = await _context.Roles.ToListAsync();
        var matchedRoles = new List<Role>();
        foreach (var reqRole in distinctInput)
        {
            var found = allRoles.FirstOrDefault(r => string.Equals(r.Name, reqRole, StringComparison.OrdinalIgnoreCase));
            if (found == null)
            {
                return BadRequest(new { message = $"Vai trò '{reqRole}' không tồn tại." });
            }
            matchedRoles.Add(found);
        }

        var user = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound(new { message = "Người dùng không tồn tại." });
        }

        var currentlyAdmin = user.UserRoles.Any(ur => string.Equals(ur.Role.Name, "Admin", StringComparison.OrdinalIgnoreCase));
        var willKeepAdmin = matchedRoles.Any(r => string.Equals(r.Name, "Admin", StringComparison.OrdinalIgnoreCase));
        if (currentlyAdmin && !willKeepAdmin)
        {
            var activeAdminCount = await _context.Users
                .CountAsync(u => u.Status != UserStatus.Locked
                    && u.UserRoles.Any(ur => ur.Role.Name == "Admin"));
            if (activeAdminCount <= 1)
            {
                return Conflict(new { code = "LAST_ADMIN", message = "Không thể gỡ vai trò Admin của quản trị viên cuối cùng." });
            }
        }

        _context.UserRoles.RemoveRange(user.UserRoles);
        foreach (var role in matchedRoles)
        {
            _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        // Thu hồi refresh tokens & cập nhật valid-after để quyền mới có hiệu lực ngay (D65)
        var activeRefreshTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && !r.IsRevoked)
            .ToListAsync();
        foreach (var rt in activeRefreshTokens)
        {
            rt.IsRevoked = true;
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _revocationStore.SetValidAfterNowAsync(user.Id);

        var roleNames = matchedRoles.Select(r => r.Name).ToList();
        await TryPublishAuditAsync(new UserRolesChangedEvent
        {
            UserId = user.Id,
            ActorUserId = GetCurrentUserId(),
            Roles = roleNames
        }, "identity.user_roles_changed");

        return Ok(new { userId = user.Id, roles = roleNames });
    }

    private Guid GetCurrentUserId()
    {
        var str = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(str, out var g) ? g : Guid.Empty;
    }

    private async Task TryPublishAuditAsync<TEvent>(TEvent @event, string routingKey) where TEvent : IntegrationEvent
    {
        if (_eventPublisher == null) return;
        try
        {
            await _eventPublisher.PublishAsync(@event, routingKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể phát audit event {EventType}", @event.EventType);
        }
    }
}
