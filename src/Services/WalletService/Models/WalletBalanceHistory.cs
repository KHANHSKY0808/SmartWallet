namespace WalletService.Models;

public enum BalanceChangeReason
{
    Initial = 1,
    TxCreated = 2,
    TxUpdated = 3,
    TxDeleted = 4,
    TransferOut = 5,
    TransferIn = 6,
    TransferCompensation = 7,
    GoalDeposit = 8,
    GoalWithdraw = 9,
    Adjustment = 10
}

public class WalletBalanceHistory
{
    public long Id { get; set; }
    public Guid WalletId { get; set; }
    public Guid UserId { get; set; }
    public BalanceChangeReason Reason { get; set; }
    public decimal ChangeAmount { get; set; }
    public decimal BalanceAfter { get; set; }
    public Guid? EntityId { get; set; }
    public Guid? SourceEventId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

