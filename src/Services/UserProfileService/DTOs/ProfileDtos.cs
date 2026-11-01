using System.ComponentModel.DataAnnotations;

namespace UserProfileService.DTOs;

public record UserProfileDto
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public string DefaultCurrency { get; init; } = "VND";
    public string Language { get; init; } = "vi";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record UpdateProfileRequest
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc")]
    [MaxLength(100, ErrorMessage = "Họ và tên không được vượt quá 100 ký tự")]
    public string FullName { get; init; } = string.Empty;

    [MaxLength(10)]
    public string DefaultCurrency { get; init; } = "VND";

    [MaxLength(10)]
    public string Language { get; init; } = "vi";
}

public record AvatarUploadResponse
{
    public string AvatarUrl { get; init; } = string.Empty;
}

