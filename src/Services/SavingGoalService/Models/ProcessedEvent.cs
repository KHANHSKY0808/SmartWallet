namespace SavingGoalService.Models;

public class ProcessedEvent
{
    public Guid EventId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 200;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
