namespace WalletService.Models;

public enum WalletType
{
    Cash = 1,
    Bank = 2,
    EWallet = 3,
    CreditCard = 4
}

public class Wallet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public WalletType Type { get; set; }
    public decimal Balance { get; set; }
    public decimal InitialBalance { get; set; }
    public string Currency { get; set; } = "VND";
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public byte[] RowVersion { get; set; } = Guid.NewGuid().ToByteArray();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

