namespace NotificationService.DTOs;

public class NotificationDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NotificationQueryParameters
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

