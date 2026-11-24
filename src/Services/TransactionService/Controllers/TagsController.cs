using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransactionService.Data;
using TransactionService.DTOs;
using TransactionService.Models;

namespace TransactionService.Controllers;

[ApiController]
[Route("api/transactions/tags")]
[Authorize]
public class TagsController : ControllerBase
{
    private readonly TransactionDbContext _context;
    private readonly ILogger<TagsController> _logger;

    public TagsController(TransactionDbContext context, ILogger<TagsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetTags()
    {
        var userId = GetCurrentUserId();
        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Name)
            .Select(t => new TagDto
            {
                Id = t.Id,
                UserId = t.UserId,
                Name = t.Name,
                Color = t.Color,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        return Ok(tags);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTag([FromBody] CreateTagRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Tên thẻ không được để trống." });
        }

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > 50)
        {
            return BadRequest(new { message = "Tên thẻ tối đa 50 ký tự." });
        }

        var userId = GetCurrentUserId();
        var exists = await _context.Tags.AnyAsync(t => t.UserId == userId && t.Name.ToLower() == trimmedName.ToLower());
        if (exists)
        {
            return Conflict(new { message = "Thẻ đã tồn tại." });
        }

        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = trimmedName,
            Color = request.Color?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _context.Tags.AddAsync(tag);
        await _context.SaveChangesAsync();

        return StatusCode(201, new TagDto
        {
            Id = tag.Id,
            UserId = tag.UserId,
            Name = tag.Name,
            Color = tag.Color,
            CreatedAt = tag.CreatedAt
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTag([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        var tag = await _context.Tags.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
        if (tag == null)
        {
            return NotFound(new { message = "Thẻ không tồn tại hoặc không thuộc quyền sở hữu." });
        }

        _context.Tags.Remove(tag);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã xóa thẻ thành công." });
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }
}
