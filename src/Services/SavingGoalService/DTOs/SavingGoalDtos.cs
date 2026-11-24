namespace SavingGoalService.DTOs;

public class CreateSavingGoalRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public DateTime TargetDate { get; set; }
    public string? Currency { get; set; } = "VND";
}

public class UpdateSavingGoalRequest
{
    public string? Name { get; set; }
    public decimal? TargetAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public int? Status { get; set; }
}

public class WithdrawRequest
{
    public Guid WalletId { get; set; }
    public decimal Amount { get; set; }
}

public class SavingGoalDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal Percent { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTime TargetDate { get; set; }
    public string Status { get; set; } = "InProgress";
    public int StatusCode { get; set; } = 1;
    public int DaysLeft { get; set; }
    public decimal MonthlyRequired { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
