using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Extensions;
using SharedKernel.Messaging;

namespace SharedKernel.Tests;

public class OutboxTests : IDisposable
{
    private sealed record TestEvent : IntegrationEvent
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestItem
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    private sealed class TestOutboxDbContext : DbContext, IOutboxDbContext
    {
        public TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options) : base(options) { }
        public DbSet<TestItem> Items => Set<TestItem>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigureOutboxMessages();
        }
    }

    private sealed class ScriptedRawPublisher : IRawEventPublisher
    {
        public Func<Guid, string, Exception?>? FailureFor { get; set; }
        public List<(Guid EventId, string EventType, string RoutingKey, string Payload, string? CorrelationId)> Published { get; } = new();

        public Task PublishRawAsync(
            Guid eventId,
            string eventType,
            string routingKey,
            string payloadJson,
            string? correlationId = null,
            DateTime? occurredAtUtc = null,
            CancellationToken cancellationToken = default)
        {
            var ex = FailureFor?.Invoke(eventId, routingKey);
            if (ex != null) throw ex;

            Published.Add((eventId, eventType, routingKey, payloadJson, correlationId));
            return Task.CompletedTask;
        }
    }

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _sp;
    private readonly TestOutboxDbContext _db;

    public OutboxTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(_connection));
        _sp = services.BuildServiceProvider();

        _db = _sp.GetRequiredService<TestOutboxDbContext>();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        CorrelationContext.CurrentCorrelationId = null;
        _db.Dispose();
        _sp.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task OB03_AtomicCommitAndRollback_WritesAndRollsBackDomainAndOutboxTogether()
    {
        var publisher = new OutboxEventPublisher<TestOutboxDbContext>(_db);

        // 1. Rolled-back transaction must discard BOTH domain entity and OutboxMessage
        await using (var txRollback = await _db.Database.BeginTransactionAsync())
        {
            _db.Items.Add(new TestItem { Id = Guid.NewGuid(), Label = "rolled-back" });
            await _db.SaveChangesAsync();
            await publisher.PublishAsync(new TestEvent { Name = "rolled-back" }, "test.rolled_back");
            await txRollback.RollbackAsync();
        }

        _db.ChangeTracker.Clear();
        Assert.Empty(await _db.Items.ToListAsync());
        Assert.Empty(await _db.OutboxMessages.ToListAsync());

        // 2. Committed transaction persists both atomically
        await using (var txCommit = await _db.Database.BeginTransactionAsync())
        {
            _db.Items.Add(new TestItem { Id = Guid.NewGuid(), Label = "committed" });
            await _db.SaveChangesAsync();
            await publisher.PublishAsync(new TestEvent { Name = "committed" }, "test.committed");
            await txCommit.CommitAsync();
        }

        Assert.Single(await _db.Items.ToListAsync());
        var outbox = await _db.OutboxMessages.SingleAsync();
        Assert.Null(outbox.SentAt);
        Assert.Equal("test.committed", outbox.RoutingKey);
    }

    [Fact]
    public async Task OB04_WorkerRetainsOnBrokerDown_ThenDispatchesIdempotentlyOnRecovery_AndCleansOld()
    {
        CorrelationContext.CurrentCorrelationId = "cid-ob04-123";
        var wakeSignal = new OutboxWakeSignal();
        var publisher = new OutboxEventPublisher<TestOutboxDbContext>(_db, wakeSignal);
        var raw = new ScriptedRawPublisher
        {
            FailureFor = (_, _) => new InvalidOperationException("RabbitMQ down")
        };
        var dispatcher = new OutboxDispatcher<TestOutboxDbContext>(_sp, raw, wakeSignal, NullLogger<OutboxDispatcher<TestOutboxDbContext>>.Instance);

        var ev = new TestEvent { Name = "hello" };
        await publisher.PublishAsync(ev, "test.hello");

        // Down -> does not mark sent, records Attempts + LastError
        Assert.Equal(0, await dispatcher.ProcessPendingAsync());
        _db.ChangeTracker.Clear();
        var pending = await _db.OutboxMessages.SingleAsync();
        Assert.Null(pending.SentAt);
        Assert.Equal(1, pending.Attempts);
        Assert.Contains("RabbitMQ down", pending.LastError);
        Assert.Equal("cid-ob04-123", pending.CorrelationId);

        // Recover -> dispatches once and sets SentAt + clears LastError
        raw.FailureFor = null;
        Assert.Equal(1, await dispatcher.ProcessPendingAsync());
        Assert.Equal(0, await dispatcher.ProcessPendingAsync()); // Already sent

        var dispatched = Assert.Single(raw.Published);
        Assert.Equal(ev.Id, dispatched.EventId);
        Assert.Equal("cid-ob04-123", dispatched.CorrelationId);

        _db.ChangeTracker.Clear();
        var sent = await _db.OutboxMessages.SingleAsync();
        Assert.NotNull(sent.SentAt);
        Assert.Null(sent.LastError);

        // Cleanup removes >7 days old sent rows, keeps recent ones
        Assert.Equal(0, await dispatcher.CleanupOldSentMessagesAsync());
        sent.SentAt = DateTime.UtcNow.AddDays(-8);
        await _db.SaveChangesAsync();
        Assert.Equal(1, await dispatcher.CleanupOldSentMessagesAsync());
        Assert.Empty(await _db.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task OB08_DispatcherPreservesCreatedAtFifoOrder_StopsBatchOnFailure()
    {
        var wakeSignal = new OutboxWakeSignal();
        var raw = new ScriptedRawPublisher();
        var dispatcher = new OutboxDispatcher<TestOutboxDbContext>(_sp, raw, wakeSignal, NullLogger<OutboxDispatcher<TestOutboxDbContext>>.Instance);

        var t0 = DateTime.UtcNow.AddMinutes(-3);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        _db.OutboxMessages.AddRange(
            new OutboxMessage { Id = id3, Type = "TestEvent", RoutingKey = "test.3", Payload = "{}", CreatedAt = t0.AddSeconds(20) },
            new OutboxMessage { Id = id1, Type = "TestEvent", RoutingKey = "test.1", Payload = "{}", CreatedAt = t0 },
            new OutboxMessage { Id = id2, Type = "TestEvent", RoutingKey = "test.2", Payload = "{}", CreatedAt = t0.AddSeconds(10) }
        );
        await _db.SaveChangesAsync();

        // Fail specifically on id2 -> id1 must send, batch stops BEFORE id3!
        raw.FailureFor = (eid, _) => eid == id2 ? new InvalidOperationException("Transient fail on #2") : null;
        Assert.Equal(1, await dispatcher.ProcessPendingAsync());
        Assert.Equal(new[] { id1 }, raw.Published.Select(p => p.EventId).ToArray());

        // Recover -> id2 then id3 sent in strict FIFO CreatedAt order
        raw.FailureFor = null;
        Assert.Equal(2, await dispatcher.ProcessPendingAsync());
        Assert.Equal(new[] { id1, id2, id3 }, raw.Published.Select(p => p.EventId).ToArray());
    }
}

