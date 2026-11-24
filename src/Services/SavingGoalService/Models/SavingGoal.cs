namespace SavingGoalService.Models;

public class SavingGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; } = 0;
    public string Currency { get; set; } = "VND";
    public DateTime TargetDate { get; set; }
    public int Status { get; set; } = 1; // 1: InProgress, 2: Completed, 3: Cancelled
    public DateTime? CompletedAt { get; set; }
    public byte[] RowVersion { get; set; } = Guid.NewGuid().ToByteArray();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
