using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CategoryService.Data;
using CategoryService.DTOs;
using CategoryService.Models;

namespace CategoryService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private static readonly Regex ColorRegex = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private readonly CategoryDbContext _context;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(CategoryDbContext context, ILogger<CategoriesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories([FromQuery] int? type = null)
    {
        var userId = GetCurrentUserId();

        var query = _context.Categories.AsNoTracking()
            .Where(c => c.IsActive && (c.UserId == null || c.UserId == userId));

        if (type.HasValue)
        {
            query = query.Where(c => c.Type == (CategoryType)type.Value);
        }

        var allCategories = await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync();

        // Xây dựng cây danh mục 2 cấp
        var rootCategories = allCategories
            .Where(c => c.ParentId == null)
            .Select(c => ToDto(c))
            .ToList();

        var childCategories = allCategories
            .Where(c => c.ParentId != null)
            .Select(c => ToDto(c))
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var root in rootCategories)
        {
            if (childCategories.TryGetValue(root.Id, out var children))
            {
                root.Children = children;
            }
        }

        return Ok(rootCategories);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCategory(Guid id)
    {
        var userId = GetCurrentUserId();
        var category = await _context.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive && (c.UserId == null || c.UserId == userId));

        if (category == null)
        {
            return NotFound(new { message = "Danh mục không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        return Ok(ToDto(category));
    }

    [HttpPost]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();
        var trimmedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 100)
        {
            return BadRequest(new { message = "Tên danh mục không được để trống và tối đa 100 ký tự" });
        }

        if (request.Type < 1 || request.Type > 2)
        {
            return BadRequest(new { message = "Loại danh mục không hợp lệ" });
        }

        if (!string.IsNullOrWhiteSpace(request.Icon) && request.Icon.Length > 50)
        {
            return BadRequest(new { message = "Icon tối đa 50 ký tự" });
        }

        if (!string.IsNullOrWhiteSpace(request.Color) && !ColorRegex.IsMatch(request.Color))
        {
            return BadRequest(new { message = "Màu sắc phải đúng định dạng #RRGGBB" });
        }

        // D25: Cây danh mục tối đa 2 cấp, con cùng Type với cha
        if (request.ParentId.HasValue)
        {
            var parent = await _context.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ParentId.Value && c.IsActive && (c.UserId == null || c.UserId == userId));

            if (parent == null)
            {
                return BadRequest(new { message = "Danh mục cha không tồn tại hoặc không hợp lệ" });
            }

            // C-05: Tối đa 2 cấp (cha phải có ParentId = null)
            if (parent.ParentId != null)
            {
                return BadRequest(new { message = "Hệ thống chỉ hỗ trợ tối đa 2 cấp danh mục (không thể tạo danh mục cấp 3)" });
            }

            // C-06: Con phải cùng Type với cha
            if (parent.Type != (CategoryType)request.Type)
            {
                return BadRequest(new { message = "Danh mục con phải cùng loại (Thu/Chi) với danh mục cha" });
            }
        }
        else
        {
            // C-11 / D26: Danh mục cá nhân ở cấp gốc không được trùng tên với danh mục mặc định cùng Type ở cấp gốc
            var defaultRoots = await _context.Categories.AsNoTracking()
                .Where(c => c.IsActive && c.UserId == null && c.ParentId == null && c.Type == (CategoryType)request.Type)
                .Select(c => c.Name)
                .ToListAsync();

            if (defaultRoots.Any(n => string.Equals(n.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                return StatusCode(409, new { code = "DUPLICATE_CATEGORY_NAME", message = "Tên danh mục trùng với danh mục mặc định ở cấp gốc" });
            }
        }

        // D26 / C-10: Unique trong phạm vi (UserId, ParentId, Type, Name) không phân biệt hoa thường (hỗ trợ Unicode tiếng Việt)
        var siblingNames = await _context.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.UserId == userId && c.ParentId == request.ParentId && c.Type == (CategoryType)request.Type)
            .Select(c => c.Name)
            .ToListAsync();

        if (siblingNames.Any(n => string.Equals(n.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(409, new { code = "DUPLICATE_CATEGORY_NAME", message = "Tên danh mục đã tồn tại trong cùng nhóm" });
        }

        var category = new Category
        {
            UserId = userId,
            Name = trimmedName,
            Type = (CategoryType)request.Type,
            ParentId = request.ParentId,
            Icon = request.Icon,
            Color = request.Color,
            IsDefault = false,
            IsActive = true
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return StatusCode(201, ToDto(category));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = GetCurrentUserId();
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.IsActive);

        if (category == null || (category.UserId != null && category.UserId != userId))
        {
            return NotFound(new { message = "Danh mục không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        // C-09 / D27: User không được sửa danh mục mặc định
        if (category.IsDefault || category.UserId == null)
        {
            return StatusCode(403, new { message = "Không được phép chỉnh sửa danh mục mặc định của hệ thống" });
        }

        var trimmedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName) || trimmedName.Length > 100)
        {
            return BadRequest(new { message = "Tên danh mục không được để trống và tối đa 100 ký tự" });
        }

        if (!string.IsNullOrWhiteSpace(request.Icon) && request.Icon.Length > 50)
        {
            return BadRequest(new { message = "Icon tối đa 50 ký tự" });
        }

        if (!string.IsNullOrWhiteSpace(request.Color) && !ColorRegex.IsMatch(request.Color))
        {
            return BadRequest(new { message = "Màu sắc phải đúng định dạng #RRGGBB" });
        }

        // C-07: Không được tự làm cha của chính mình hoặc tạo vòng lặp
        if (request.ParentId.HasValue)
        {
            if (request.ParentId.Value == id)
            {
                return BadRequest(new { message = "Danh mục không thể tự làm cha của chính mình" });
            }

            var hasChildren = await _context.Categories.AnyAsync(c => c.ParentId == id && c.IsActive);
            if (hasChildren)
            {
                return BadRequest(new { message = "Danh mục đang có con không thể chuyển thành danh mục con (tối đa 2 cấp)" });
            }

            var parent = await _context.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ParentId.Value && c.IsActive && (c.UserId == null || c.UserId == userId));

            if (parent == null || parent.ParentId != null || parent.Type != category.Type)
            {
                return BadRequest(new { message = "Danh mục cha không hợp lệ hoặc khác loại thu/chi" });
            }
        }

        // Kiểm tra trùng tên cùng cấp (hỗ trợ Unicode tiếng Việt)
        var siblingNames = await _context.Categories.AsNoTracking()
            .Where(c => c.Id != id && c.IsActive && c.UserId == userId && c.ParentId == request.ParentId && c.Type == category.Type)
            .Select(c => c.Name)
            .ToListAsync();

        if (siblingNames.Any(n => string.Equals(n.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(409, new { code = "DUPLICATE_CATEGORY_NAME", message = "Tên danh mục đã tồn tại trong cùng nhóm" });
        }

        category.Name = trimmedName;
        category.ParentId = request.ParentId;
        category.Icon = request.Icon;
        category.Color = request.Color;
        category.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(category));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, [FromQuery] Guid? replacementId = null)
    {
        var userId = GetCurrentUserId();
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.IsActive);

        if (category == null || (category.UserId != null && category.UserId != userId))
        {
            return NotFound(new { message = "Danh mục không tồn tại hoặc không thuộc quyền sở hữu" });
        }

        // C-09 / D27: User xóa danh mục mặc định -> 403
        if (category.IsDefault || category.UserId == null)
        {
            return StatusCode(403, new { message = "Không được phép xóa danh mục mặc định của hệ thống" });
        }

        // C-12 / D27: Danh mục cha còn con đang hoạt động -> 409 HAS_CHILDREN
        var hasActiveChildren = await _context.Categories.AnyAsync(c => c.ParentId == id && c.IsActive);
        if (hasActiveChildren)
        {
            return StatusCode(409, new { code = "HAS_CHILDREN", message = "Không thể xóa danh mục khi còn các danh mục con đang hoạt động" });
        }

        // C-14 / D27: Validate replacementId nếu có
        if (replacementId.HasValue)
        {
            if (replacementId.Value == id)
            {
                return BadRequest(new { message = "Danh mục thay thế không được trùng với chính nó" });
            }

            var replacement = await _context.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == replacementId.Value && c.IsActive && (c.UserId == null || c.UserId == userId));

            if (replacement == null || replacement.Type != category.Type)
            {
                return BadRequest(new { message = "Danh mục thay thế không tồn tại, khác loại hoặc không thuộc quyền truy cập" });
            }
        }

        // D27: Sprint 2 xóa mềm IsActive = false
        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Xóa danh mục thành công" });
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

    private static CategoryDto ToDto(Category c) => new()
    {
        Id = c.Id,
        UserId = c.UserId,
        Name = c.Name,
        Type = c.Type,
        ParentId = c.ParentId,
        Icon = c.Icon,
        Color = c.Color,
        IsDefault = c.IsDefault,
        IsActive = c.IsActive,
        SortOrder = c.SortOrder
    };
}
