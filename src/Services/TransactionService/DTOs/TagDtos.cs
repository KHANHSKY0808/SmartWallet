using System.ComponentModel.DataAnnotations;

namespace TransactionService.DTOs;

public class CreateTagRequest
{
    [Required(ErrorMessage = "Tên thẻ không được để trống")]
    [MaxLength(50, ErrorMessage = "Tên thẻ tối đa 50 ký tự")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Color { get; set; }
}

public class TagDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public DateTime CreatedAt { get; set; }
}
