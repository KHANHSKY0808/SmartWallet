using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events.EventContracts;
using TransactionService.Consumers;
using TransactionService.Data;
using TransactionService.Models;

namespace TransactionService.Tests;

public class MoneyTransferredConsumerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TransactionDbContext _context;
    private readonly ServiceProvider _serviceProvider;
    private readonly MoneyTransferredConsumer _consumer;

    public MoneyTransferredConsumerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TransactionDbContext>(options =>
            options.UseSqlite(_connection));

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<TransactionDbContext>();
        _context.Database.EnsureCreated();

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "RabbitMQ:Host", "localhost" },
            { "RabbitMQ:Port", "5672" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        _consumer = new MoneyTransferredConsumer(
            config,
            NullLogger<MoneyTransferredConsumer>.Instance,
            _serviceProvider);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _context.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task MC01_ProcessEvent_CreatesType3Transaction()
    {
        var transferId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sourceWalletId = Guid.NewGuid();
        var targetWalletId = Guid.NewGuid();

        var evt = new MoneyTransferredEvent
        {
            TransferId = transferId,
            UserId = userId,
            SourceWalletId = sourceWalletId,
            TargetWalletId = targetWalletId,
            Amount = 150000m,
            Fee = 0m,
            Note = "Chuyển tiền ví A sang B"
        };

        await _consumer.HandleEventAsync(evt);

        var tx = await _context.Transactions.FirstOrDefaultAsync(t => t.TransferId == transferId);
        Assert.NotNull(tx);
        Assert.Equal(TransactionType.Transfer, tx.Type);
        Assert.Equal(150000m, tx.Amount);
        Assert.Equal(sourceWalletId, tx.WalletId);
        Assert.Equal("Chuyển tiền ví A sang B", tx.Note);
    }

    [Fact]
    public async Task MC02_ProcessEvent_Idempotent_DoesNotDuplicateOnSameTransferId()
    {
        var transferId = Guid.NewGuid();
        var evt = new MoneyTransferredEvent
        {
            TransferId = transferId,
            UserId = Guid.NewGuid(),
            SourceWalletId = Guid.NewGuid(),
            TargetWalletId = Guid.NewGuid(),
            Amount = 200000m
        };

        await _consumer.HandleEventAsync(evt);
        // Dispatch lần 2
        await _consumer.HandleEventAsync(evt);

        var count = await _context.Transactions.CountAsync(t => t.TransferId == transferId);
        Assert.Equal(1, count);
    }
}

