using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CategoryService.Data;
using CategoryService.DTOs;
using CategoryService.Models;

namespace CategoryService.Controllers;

[ApiController]
[Route("api/categories/defaults")]
[Authorize(Roles = "Admin")]
public class CategoryDefaultsController : ControllerBase
{
    private static readonly Regex ColorRegex = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private readonly CategoryDbContext _context;
    private readonly ILogger<CategoryDefaultsController> _logger;

    public CategoryDefaultsController(CategoryDbContext context, ILogger<CategoryDefaultsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateDefaultCategory([FromBody] CreateCategoryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

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

        if (request.ParentId.HasValue)
        {
            var parent = await _context.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.ParentId.Value && c.IsActive && c.UserId == null);

            if (parent == null || parent.ParentId != null || parent.Type != (CategoryType)request.Type)
            {
                return BadRequest(new { message = "Danh mục cha mặc định không hợp lệ hoặc khác loại" });
            }
        }

        // D-05: Tạo danh mục mặc định có UserId = null, IsDefault = true
        var category = new Category
        {
            UserId = null,
            Name = trimmedName,
            Type = (CategoryType)request.Type,
            ParentId = request.ParentId,
            Icon = request.Icon,
            Color = request.Color,
            IsDefault = true,
            IsActive = true
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return StatusCode(201, ToDto(category));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDefaultCategory(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == null && c.IsActive);
        if (category == null)
        {
            return NotFound(new { message = "Danh mục mặc định không tồn tại" });
        }

        var trimmedName = request.Name?.Trim() ?? string.Empty;
        category.Name = trimmedName;
        category.Icon = request.Icon;
        category.Color = request.Color;
        category.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ToDto(category));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDefaultCategory(Guid id)
    {
        // D-06: Admin xóa danh mục mặc định -> xóa mềm IsActive = false
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == null && c.IsActive);
        if (category == null)
        {
            return NotFound(new { message = "Danh mục mặc định không tồn tại" });
        }

        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Đã xóa danh mục mặc định" });
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

