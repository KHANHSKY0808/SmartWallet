using System.ComponentModel.DataAnnotations;
using CategoryService.Models;

namespace CategoryService.DTOs;

public class CreateCategoryRequest
{
    [Required(ErrorMessage = "Tên danh mục không được để trống")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên danh mục tối đa 100 ký tự")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 2, ErrorMessage = "Loại danh mục không hợp lệ (1=Chi tiêu, 2=Thu nhập)")]
    public int Type { get; set; }

    public Guid? ParentId { get; set; }

    [StringLength(50, ErrorMessage = "Icon tối đa 50 ký tự")]
    public string? Icon { get; set; }

    [RegularExpression(@"^#[0-9a-fA-F]{6}$", ErrorMessage = "Màu sắc phải có định dạng #RRGGBB")]
    [StringLength(20, ErrorMessage = "Màu sắc tối đa 20 ký tự")]
    public string? Color { get; set; }
}

public class UpdateCategoryRequest
{
    [Required(ErrorMessage = "Tên danh mục không được để trống")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên danh mục tối đa 100 ký tự")]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    [StringLength(50, ErrorMessage = "Icon tối đa 50 ký tự")]
    public string? Icon { get; set; }

    [RegularExpression(@"^#[0-9a-fA-F]{6}$", ErrorMessage = "Màu sắc phải có định dạng #RRGGBB")]
    [StringLength(20, ErrorMessage = "Màu sắc tối đa 20 ký tự")]
    public string? Color { get; set; }
}

public class CategoryDto
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CategoryType Type { get; set; }
    public Guid? ParentId { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public List<CategoryDto> Children { get; set; } = new();
}

