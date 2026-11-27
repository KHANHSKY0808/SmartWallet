using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Events;

namespace SharedKernel.Messaging;

/// <summary>Bản ghi Transactional Outbox lưu cùng DB transaction với dữ liệu nghiệp vụ (D72).</summary>
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IRawEventPublisher
{
    Task PublishRawAsync(
        Guid eventId,
        string eventType,
        string routingKey,
        string payloadJson,
        string? correlationId = null,
        DateTime? occurredAtUtc = null,
        CancellationToken cancellationToken = default);
}

public interface IBatchRawEventPublisher : IRawEventPublisher
{
    Task PublishBatchRawAsync(
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken = default);
}

public sealed class OutboxWakeSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); } catch (SemaphoreFullException) { }
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
        => _signal.WaitAsync(timeout, ct);
}

public static class OutboxModelBuilderExtensions
{
    public static void ConfigureOutboxMessages(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Type).IsRequired().HasMaxLength(200);
            entity.Property(e => e.RoutingKey).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(100);
            entity.Property(e => e.LastError).HasMaxLength(1000);
            entity.HasIndex(e => new { e.SentAt, e.CreatedAt })
                .HasDatabaseName("IX_OutboxMessages_SentAt_CreatedAt");
        });
    }
}

/// <summary>Ghi IntegrationEvent vào bảng OutboxMessages trong cùng DbContext thay vì publish trực tiếp lên broker (D72).</summary>
public class OutboxEventPublisher<TDbContext> : IEventPublisher
    where TDbContext : DbContext, IOutboxDbContext
{
    private readonly TDbContext _db;
    private readonly OutboxWakeSignal? _wakeSignal;

    public OutboxEventPublisher(TDbContext db, OutboxWakeSignal? wakeSignal = null)
    {
        _db = db;
        _wakeSignal = wakeSignal;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        var msg = new OutboxMessage
        {
            Id = @event.Id,
            Type = @event.EventType,
            RoutingKey = routingKey,
            Payload = JsonSerializer.Serialize(@event, @event.GetType()),
            CorrelationId = SharedKernel.Extensions.CorrelationContext.CurrentCorrelationId,
            CreatedAt = DateTime.UtcNow
        };

        _db.OutboxMessages.Add(msg);
        await _db.SaveChangesAsync(cancellationToken);
        _wakeSignal?.Notify();
    }
}

/// <summary>Worker nền quét OutboxMessages định kỳ 1s (lô 200), giữ thứ tự CreatedAt, backoff 1→30s khi lỗi, dọn bản cũ sau 7 ngày (D72).</summary>
public class OutboxDispatcher<TDbContext> : BackgroundService
    where TDbContext : DbContext, IOutboxDbContext
{
    private static readonly int[] BackoffSeconds = [1, 2, 5, 10, 30];
    private const int BatchSize = 200;

    private readonly IServiceProvider _serviceProvider;
    private readonly IRawEventPublisher _rawPublisher;
    private readonly OutboxWakeSignal _wakeSignal;
    private readonly ILogger<OutboxDispatcher<TDbContext>> _logger;
    private int _consecutiveFailures;
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public OutboxDispatcher(
        IServiceProvider serviceProvider,
        IRawEventPublisher rawPublisher,
        OutboxWakeSignal wakeSignal,
        ILogger<OutboxDispatcher<TDbContext>> logger)
    {
        _serviceProvider = serviceProvider;
        _rawPublisher = rawPublisher;
        _wakeSignal = wakeSignal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            var hadFailure = false;
            try
            {
                var result = await ProcessPendingInternalAsync(stoppingToken);
                hadFailure = result.HadFailure;

                if (DateTime.UtcNow - _lastCleanupUtc > TimeSpan.FromHours(1))
                {
                    await CleanupOldSentMessagesAsync(stoppingToken);
                    _lastCleanupUtc = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                hadFailure = true;
                _logger.LogWarning(ex, "OutboxDispatcher<{Db}> gặp lỗi ngoài dự kiến khi quét.", typeof(TDbContext).Name);
            }

            if (hadFailure)
            {
                var idx = Math.Min(_consecutiveFailures, BackoffSeconds.Length - 1);
                var delay = TimeSpan.FromSeconds(BackoffSeconds[idx]);
                _consecutiveFailures = Math.Min(_consecutiveFailures + 1, BackoffSeconds.Length - 1);
                try { await Task.Delay(delay, stoppingToken); } catch (OperationCanceledException) { break; }
            }
            else
            {
                _consecutiveFailures = 0;
                try { await _wakeSignal.WaitAsync(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
        => (await ProcessPendingInternalAsync(cancellationToken)).DispatchedCount;

    private async Task<(int DispatchedCount, bool HadFailure)> ProcessPendingInternalAsync(CancellationToken cancellationToken)
    {
        int dispatched = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var pending = await db.OutboxMessages
                .Where(m => m.SentAt == null)
                .OrderBy(m => m.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
            {
                return (dispatched, false);
            }

            if (_rawPublisher is IBatchRawEventPublisher batchPublisher && pending.Count > 1)
            {
                try
                {
                    await batchPublisher.PublishBatchRawAsync(pending, cancellationToken);
                    var sentAt = DateTime.UtcNow;
                    foreach (var msg in pending)
                    {
                        msg.SentAt = sentAt;
                        msg.LastError = null;
                    }
                    await db.SaveChangesAsync(cancellationToken);
                    dispatched += pending.Count;
                    continue;
                }
                catch
                {
                    // Fallback từng bản ghi để bảo toàn prefix đã gửi và thứ tự FIFO (OB-08)
                }
            }

            foreach (var msg in pending)
            {
                try
                {
                    await _rawPublisher.PublishRawAsync(
                        msg.Id,
                        msg.Type,
                        msg.RoutingKey,
                        msg.Payload,
                        msg.CorrelationId,
                        msg.CreatedAt,
                        cancellationToken);

                    msg.SentAt = DateTime.UtcNow;
                    msg.LastError = null;
                    await db.SaveChangesAsync(cancellationToken);
                    dispatched++;
                }
                catch (Exception ex)
                {
                    msg.Attempts++;
                    msg.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    try { await db.SaveChangesAsync(cancellationToken); } catch { }
                    // Dừng lô tại phần tử lỗi đầu tiên để giữ đúng thứ tự CreatedAt (OB-08)
                    return (dispatched, true);
                }
            }

            if (pending.Count < BatchSize)
            {
                return (dispatched, false);
            }
        }

        return (dispatched, false);
    }

    public async Task<int> CleanupOldSentMessagesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var cutoff = DateTime.UtcNow.AddDays(-7);

        var old = await db.OutboxMessages
            .Where(m => m.SentAt != null && m.SentAt < cutoff)
            .ToListAsync(cancellationToken);

        if (old.Count == 0) return 0;

        db.OutboxMessages.RemoveRange(old);
        await db.SaveChangesAsync(cancellationToken);
        return old.Count;
    }
}

public static class OutboxServiceCollectionExtensions
{
    /// <summary>Đăng ký Transactional Outbox (D72) thay thế publish đồng bộ D30 cho service có DB.</summary>
    public static IServiceCollection AddSmartWalletOutbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext, IOutboxDbContext
    {
        services.AddSingleton<RabbitMqEventPublisher>();
        services.AddSingleton<IRawEventPublisher>(sp => sp.GetRequiredService<RabbitMqEventPublisher>());
        services.AddSingleton<OutboxWakeSignal>();
        services.AddScoped<IEventPublisher, OutboxEventPublisher<TDbContext>>();
        services.AddSingleton<OutboxDispatcher<TDbContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher<TDbContext>>());
        return services;
    }
}

