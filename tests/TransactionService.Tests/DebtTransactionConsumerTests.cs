using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Consumers;
using TransactionService.Data;
using TransactionService.Models;

namespace TransactionService.Tests;

public class DebtTransactionConsumerTests : IDisposable
{
    private class SpyEventPublisher : IEventPublisher
    {
        public List<object> PublishedEvents { get; } = new();

        public Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default)
            where TEvent : IntegrationEvent
        {
            PublishedEvents.Add(@event!);
            return Task.CompletedTask;
        }
    }

    private readonly SqliteConnection _connection;
    private readonly TransactionDbContext _context;
    private readonly ServiceProvider _serviceProvider;
    private readonly SpyEventPublisher _spyPublisher;
    private readonly DebtTransactionConsumer _consumer;

    public DebtTransactionConsumerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _spyPublisher = new SpyEventPublisher();

        var services = new ServiceCollection();
        services.AddDbContext<TransactionDbContext>(options =>
            options.UseSqlite(_connection));
        services.AddSingleton<IEventPublisher>(_spyPublisher);

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<TransactionDbContext>();
        _context.Database.EnsureCreated();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "RabbitMQ:Host", "localhost" },
                { "RabbitMQ:Port", "5672" }
            })
            .Build();

        _consumer = new DebtTransactionConsumer(
            config,
            NullLogger<DebtTransactionConsumer>.Instance,
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
    public async Task Test_N36_ProcessSameDebtPaymentEventTwice_CreatesOnlyOneTransactionAndPublishesOnce()
    {
        var debtId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var evt = new DebtTransactionRequestedEvent
        {
            DebtId = debtId,
            PaymentId = paymentId,
            UserId = Guid.NewGuid(),
            WalletId = Guid.NewGuid(),
            Type = 2,
            Amount = 100_000m,
            CategoryId = Guid.NewGuid(),
            Note = "Thu nợ test N36",
            TransactionDate = DateTime.UtcNow
        };

        await _consumer.HandleEventAsync(evt);
        // Phát lại cùng event lần 2 (mô phỏng RabbitMQ at-least-once)
        await _consumer.HandleEventAsync(evt);

        var txs = await _context.Transactions
            .Where(t => t.SourceType == SourceType.Debt && t.SourceId == debtId)
            .ToListAsync();

        Assert.Single(txs);
        Assert.Equal(paymentId, txs[0].SourceSubId);
        Assert.Single(_spyPublisher.PublishedEvents);
    }

    [Fact]
    public async Task Test_N36_ProcessSameInitialDebtEventTwice_CreatesOnlyOneTransactionAndPublishesOnce()
    {
        var debtId = Guid.NewGuid();
        var evt = new DebtTransactionRequestedEvent
        {
            DebtId = debtId,
            PaymentId = null,
            UserId = Guid.NewGuid(),
            WalletId = Guid.NewGuid(),
            Type = 1,
            Amount = 500_000m,
            CategoryId = Guid.NewGuid(),
            Note = "Cho vay test N36",
            TransactionDate = DateTime.UtcNow
        };

        await _consumer.HandleEventAsync(evt);
        // Phát lại lần 2
        await _consumer.HandleEventAsync(evt);

        var txs = await _context.Transactions
            .Where(t => t.SourceType == SourceType.Debt && t.SourceId == debtId)
            .ToListAsync();

        Assert.Single(txs);
        Assert.Equal(debtId, txs[0].SourceSubId);
        Assert.Single(_spyPublisher.PublishedEvents);
    }
}

