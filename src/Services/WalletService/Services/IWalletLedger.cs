using WalletService.Data;

namespace WalletService.Services;

public interface IWalletLedger
{
    Task DebitAsync(Guid walletId, decimal amount, CancellationToken cancellationToken = default);
    Task CreditAsync(Guid walletId, decimal amount, CancellationToken cancellationToken = default);
}

public class WalletLedger : IWalletLedger
{
    private readonly WalletDbContext _context;

    public WalletLedger(WalletDbContext context)
    {
        _context = context;
    }

    public async Task DebitAsync(Guid walletId, decimal amount, CancellationToken cancellationToken = default)
    {
        var wallet = await _context.Wallets.FindAsync(new object[] { walletId }, cancellationToken);
        if (wallet == null) throw new InvalidOperationException("Ví không tồn tại");
        wallet.Balance -= amount;
    }

    public async Task CreditAsync(Guid walletId, decimal amount, CancellationToken cancellationToken = default)
    {
        var wallet = await _context.Wallets.FindAsync(new object[] { walletId }, cancellationToken);
        if (wallet == null) throw new InvalidOperationException("Ví không tồn tại");
        wallet.Balance += amount;
    }
}

