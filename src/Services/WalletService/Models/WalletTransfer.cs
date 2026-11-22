namespace WalletService.Models;

public enum TransferStatus
{
    Pending = 0,
    Success = 1,
    FailedCompensated = 2
}

public class WalletTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SourceWalletId { get; set; }
    public Wallet? SourceWallet { get; set; }
    public Guid TargetWalletId { get; set; }
    public Wallet? TargetWallet { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public DateTime TransferDate { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

