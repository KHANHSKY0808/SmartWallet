using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events.EventContracts;
using WalletService.Consumers;
using WalletService.Data;
using WalletService.Models;

namespace WalletService.Tests;

public class TransactionConsumersTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WalletDbContext _context;
    private readonly ServiceProvider _serviceProvider;
    private readonly TransactionCreatedConsumer _createdConsumer;
    private readonly TransactionUpdatedConsumer _updatedConsumer;
    private readonly TransactionDeletedConsumer _deletedConsumer;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _walletAId = Guid.NewGuid();
    private readonly Guid _walletBId = Guid.NewGuid();

    public TransactionConsumersTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<WalletDbContext>(options =>
            options.UseSqlite(_connection));

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<WalletDbContext>();
        _context.Database.EnsureCreated();

        // Seed 2 ví
        _context.Wallets.AddRange(
            new Wallet { Id = _walletAId, UserId = _userId, Name = "Ví Tiền Mặt", Balance = 1_000_000m, InitialBalance = 1_000_000m },
            new Wallet { Id = _walletBId, UserId = _userId, Name = "Ví Ngân Hàng", Balance = 5_000_000m, InitialBalance = 5_000_000m }
        );
        _context.SaveChanges();

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "RabbitMQ:Host", "localhost" },
            { "RabbitMQ:Port", "5672" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        _createdConsumer = new TransactionCreatedConsumer(config, NullLogger<TransactionCreatedConsumer>.Instance, _serviceProvider);
        _updatedConsumer = new TransactionUpdatedConsumer(config, NullLogger<TransactionUpdatedConsumer>.Instance, _serviceProvider);
        _deletedConsumer = new TransactionDeletedConsumer(config, NullLogger<TransactionDeletedConsumer>.Instance, _serviceProvider);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _context.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task WC01_TransactionCreated_Expense_DebitsWalletBalanceAndRecordsHistory()
    {
        var evt = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = _walletAId,
            Type = 1, // Expense
            Amount = 50_000m
        };

        await _createdConsumer.HandleEventAsync(evt);

        var wallet = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == _walletAId);
        Assert.NotNull(wallet);
        Assert.Equal(950_000m, wallet.Balance);

        var history = await _context.WalletBalanceHistory.FirstOrDefaultAsync(h => h.EntityId == evt.TransactionId);
        Assert.NotNull(history);
        Assert.Equal(-50_000m, history.ChangeAmount);
        Assert.Equal(950_000m, history.BalanceAfter);
        Assert.Equal(BalanceChangeReason.TxCreated, history.Reason);
    }

    [Fact]
    public async Task WC02_TransactionCreated_Income_CreditsWalletBalanceAndRecordsHistory()
    {
        var evt = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = _walletAId,
            Type = 2, // Income
            Amount = 10_000_000m
        };

        await _createdConsumer.HandleEventAsync(evt);

        var wallet = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == _walletAId);
        Assert.NotNull(wallet);
        Assert.Equal(11_000_000m, wallet.Balance);

        var history = await _context.WalletBalanceHistory.FirstOrDefaultAsync(h => h.EntityId == evt.TransactionId);
        Assert.NotNull(history);
        Assert.Equal(10_000_000m, history.ChangeAmount);
        Assert.Equal(11_000_000m, history.BalanceAfter);
    }

    [Fact]
    public async Task WC03_TransactionCreated_TransferType3_Ignored()
    {
        var evt = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = _walletAId,
            Type = 3, // Transfer
            Amount = 200_000m
        };

        await _createdConsumer.HandleEventAsync(evt);

        var wallet = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == _walletAId);
        Assert.NotNull(wallet);
        Assert.Equal(1_000_000m, wallet.Balance); // Không thay đổi

        var history = await _context.WalletBalanceHistory.FirstOrDefaultAsync(h => h.EntityId == evt.TransactionId);
        Assert.Null(history);
    }

    [Fact]
    public async Task WC04_TransactionCreated_Idempotent_IgnoredIfAlreadyProcessed()
    {
        var evt = new TransactionCreatedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userId,
            WalletId = _walletAId,
            Type = 1,
            Amount = 50_000m
        };

        await _createdConsumer.HandleEventAsync(evt);
        await _createdConsumer.HandleEventAsync(evt);

        var wallet = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == _walletAId);
        Assert.NotNull(wallet);
        Assert.Equal(950_000m, wallet.Balance); // Chỉ trừ 1 lần duy nhất

        var count = await _context.WalletBalanceHistory.CountAsync(h => h.EntityId == evt.TransactionId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task WC05_TransactionUpdated_SameWallet_CalculatesDelta()
    {
        // Ban đầu chi 50K -> ví A còn 950K
        var txId = Guid.NewGuid();
        var createEvt = new TransactionCreatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            WalletId = _walletAId,
            Type = 1,
            Amount = 50_000m
        };
        await _createdConsumer.HandleEventAsync(createEvt);

        // Sửa giao dịch chi 50K -> 70K
        var updateEvt = new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            OldWalletId = _walletAId,
            NewWalletId = _walletAId,
            OldType = 1,
            NewType = 1,
            OldAmount = 50_000m,
            NewAmount = 70_000m
        };
        await _updatedConsumer.HandleEventAsync(updateEvt);

        var wallet = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == _walletAId);
        Assert.NotNull(wallet);
        // Ban đầu 1M, chi 50K -> 950K, sửa thành 70K -> giảm thêm 20K -> 930K
        Assert.Equal(930_000m, wallet.Balance);

        var history = await _context.WalletBalanceHistory
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync(h => h.EntityId == txId);
        Assert.NotNull(history);
        Assert.Equal(-20_000m, history.ChangeAmount);
        Assert.Equal(930_000m, history.BalanceAfter);
    }

    [Fact]
    public async Task WC06_TransactionUpdated_DifferentWallet_RefundsOldDebitsNew()
    {
        // Ban đầu chi 50K ở ví A (1M -> 950K)
        var txId = Guid.NewGuid();
        var createEvt = new TransactionCreatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            WalletId = _walletAId,
            Type = 1,
            Amount = 50_000m
        };
        await _createdConsumer.HandleEventAsync(createEvt);

        // Đổi sang ví B với số tiền 60K
        var updateEvt = new TransactionUpdatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            OldWalletId = _walletAId,
            NewWalletId = _walletBId,
            OldType = 1,
            NewType = 1,
            OldAmount = 50_000m,
            NewAmount = 60_000m
        };
        await _updatedConsumer.HandleEventAsync(updateEvt);

        var walletA = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == _walletAId);
        var walletB = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == _walletBId);
        Assert.NotNull(walletA);
        Assert.NotNull(walletB);

        // Ví A được hoàn lại 50K -> 1_000_000
        Assert.Equal(1_000_000m, walletA.Balance);
        // Ví B bị trừ 60K (5M -> 4_940_000)
        Assert.Equal(4_940_000m, walletB.Balance);
    }

    [Fact]
    public async Task WC07_TransactionDeleted_Expense_RefundsWallet()
    {
        // Tạo giao dịch chi 50K
        var txId = Guid.NewGuid();
        var createEvt = new TransactionCreatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            WalletId = _walletAId,
            Type = 1,
            Amount = 50_000m
        };
        await _createdConsumer.HandleEventAsync(createEvt);

        // Xóa giao dịch chi 50K
        var deleteEvt = new TransactionDeletedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            WalletId = _walletAId,
            Type = 1,
            Amount = 50_000m
        };
        await _deletedConsumer.HandleEventAsync(deleteEvt);

        var wallet = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == _walletAId);
        Assert.NotNull(wallet);
        Assert.Equal(1_000_000m, wallet.Balance); // Hoàn đủ 1M

        var history = await _context.WalletBalanceHistory
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync(h => h.EntityId == txId);
        Assert.NotNull(history);
        Assert.Equal(50_000m, history.ChangeAmount);
        Assert.Equal(BalanceChangeReason.TxDeleted, history.Reason);
    }

    [Fact]
    public async Task WC08_TransactionDeleted_Income_DeductsWallet()
    {
        // Tạo giao dịch thu 200K -> ví A 1.2M
        var txId = Guid.NewGuid();
        var createEvt = new TransactionCreatedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            WalletId = _walletAId,
            Type = 2,
            Amount = 200_000m
        };
        await _createdConsumer.HandleEventAsync(createEvt);

        // Xóa giao dịch thu 200K
        var deleteEvt = new TransactionDeletedEvent
        {
            TransactionId = txId,
            UserId = _userId,
            WalletId = _walletAId,
            Type = 2,
            Amount = 200_000m
        };
        await _deletedConsumer.HandleEventAsync(deleteEvt);

        var wallet = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == _walletAId);
        Assert.NotNull(wallet);
        Assert.Equal(1_000_000m, wallet.Balance); // Quay lại 1M
    }
}
