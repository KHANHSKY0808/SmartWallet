namespace SavingGoalService.Models;

public class GoalLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoalId { get; set; }
    public Guid UserId { get; set; }
    public Guid SourceWalletId { get; set; }
    public decimal Amount { get; set; }
    public int Type { get; set; } // 1: Deposit, 2: Withdraw
    public int Status { get; set; } = 1; // 0: Pending, 1: Success, 2: Failed
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
