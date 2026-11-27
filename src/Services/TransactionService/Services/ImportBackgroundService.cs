using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using TransactionService.Data;
using TransactionService.Models;

namespace TransactionService.Services;

public interface IImportJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}

public sealed class ImportJobQueue : IImportJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

public class ImportBackgroundService : BackgroundService
{
    private const int BatchSize = 100;
    private readonly IServiceProvider _serviceProvider;
    private readonly IImportJobQueue _queue;
    private readonly ILogger<ImportBackgroundService> _logger;

    public ImportBackgroundService(
        IServiceProvider serviceProvider,
        IImportJobQueue queue,
        ILogger<ImportBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // D68 / IM-08: Tiếp tục các job Queued/Processing còn dở khi service khởi động lại
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
            var pendingIds = await db.ImportJobs
                .Where(j => j.Status == ImportJobStatus.Queued || j.Status == ImportJobStatus.Processing)
                .OrderBy(j => j.CreatedAt)
                .Select(j => j.Id)
                .ToListAsync(stoppingToken);

            foreach (var jobId in pendingIds)
            {
                await _queue.EnqueueAsync(jobId, stoppingToken);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Không thể nạp các ImportJob đang chờ khi khởi động TransactionService.");
        }

        await foreach (var jobId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Lỗi không mong muốn khi xử lý ImportJob {JobId}", jobId);
            }
        }
    }

    public async Task ProcessJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var walletClient = scope.ServiceProvider.GetRequiredService<IWalletClient>();
        var categoryClient = scope.ServiceProvider.GetRequiredService<ICategoryClient>();
        var eventPublisher = scope.ServiceProvider.GetService<IEventPublisher>();

        var job = await db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || job.Status is ImportJobStatus.Completed or ImportJobStatus.Failed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(job.TempFilePath) || !File.Exists(job.TempFilePath))
        {
            job.Status = ImportJobStatus.Failed;
            job.ErrorMessage = "Tệp nguồn tạm không tồn tại để tiếp tục xử lý.";
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        job.Status = ImportJobStatus.Processing;
        job.StartedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var fileBytes = await File.ReadAllBytesAsync(job.TempFilePath, cancellationToken);
            var rawRows = ImportParser.ParseFile(fileBytes, job.Extension, job.ColumnMappingJson);
            job.TotalRows = rawRows.Count;
            await db.SaveChangesAsync(cancellationToken);

            var wallets = await FetchWithRetryAsync(() => walletClient.GetUserWalletsAsync(job.UserId, cancellationToken), cancellationToken);
            var categories = await FetchWithRetryAsync(() => categoryClient.GetUserCategoriesAsync(job.UserId, cancellationToken), cancellationToken);

            var walletsByName = wallets
                .Where(w => !w.IsArchived)
                .GroupBy(w => w.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var categoriesByName = categories.ToLookup(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase);

            // IM-08: Tải danh sách dòng đã ghi thành công hoặc đã ghi lỗi để resume không tạo trùng
            var existingSubIds = (await db.Transactions.AsNoTracking()
                .Where(t => t.SourceType == SourceType.Import && t.SourceId == job.Id && t.SourceSubId != null)
                .Select(t => t.SourceSubId!.Value)
                .ToListAsync(cancellationToken)).ToHashSet();

            var existingErrorRows = (await db.ImportJobErrors.AsNoTracking()
                .Where(e => e.ImportJobId == job.Id)
                .Select(e => e.RowNumber)
                .ToListAsync(cancellationToken)).ToHashSet();

            var remainingRows = rawRows
                .Where(r => !existingSubIds.Contains(ImportParser.DeterministicRowGuid(job.Id, r.RowNumber))
                         && !existingErrorRows.Contains(r.RowNumber))
                .ToList();

            foreach (var batch in remainingRows.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pendingEvents = new List<TransactionCreatedEvent>();
                int batchSucceeded = 0;
                int batchFailed = 0;

                using var dbTx = await db.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    foreach (var row in batch)
                    {
                        var rowGuid = ImportParser.DeterministicRowGuid(job.Id, row.RowNumber);
                        if (ImportParser.TryValidateAndResolveRow(row, walletsByName, categoriesByName, out var parsed, out var reason)
                            && parsed is not null)
                        {
                            var tx = new Transaction
                            {
                                Id = rowGuid,
                                UserId = job.UserId,
                                WalletId = parsed.WalletId,
                                CategoryId = parsed.CategoryId,
                                Type = parsed.Type,
                                Amount = parsed.Amount,
                                TransactionDate = parsed.TransactionDateUtc,
                                Note = parsed.Note,
                                SourceType = SourceType.Import,
                                SourceId = job.Id,
                                SourceSubId = rowGuid,
                                RowVersion = Guid.NewGuid(),
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            };
                            db.Transactions.Add(tx);
                            batchSucceeded++;

                            pendingEvents.Add(new TransactionCreatedEvent
                            {
                                Id = rowGuid, // Tất định theo (JobId, RowNumber) -> idempotent tuyệt đối khi resume (IM-08)
                                OccurredAt = DateTime.UtcNow,
                                TransactionId = tx.Id,
                                UserId = tx.UserId,
                                WalletId = tx.WalletId,
                                CategoryId = tx.CategoryId ?? Guid.Empty,
                                CategoryParentId = parsed.CategoryParentId,
                                CategoryName = parsed.CategoryName,
                                Type = (int)tx.Type,
                                Amount = tx.Amount,
                                Currency = parsed.WalletCurrency,
                                TransactionDate = tx.TransactionDate,
                                Note = tx.Note
                            });
                        }
                        else
                        {
                            db.ImportJobErrors.Add(new ImportJobError
                            {
                                ImportJobId = job.Id,
                                RowNumber = row.RowNumber,
                                DateRaw = Truncate(row.DateRaw, 100),
                                AmountRaw = Truncate(row.AmountRaw, 100),
                                TypeRaw = Truncate(row.TypeRaw, 50),
                                CategoryRaw = Truncate(row.CategoryRaw, 200),
                                WalletRaw = Truncate(row.WalletRaw, 200),
                                NoteRaw = Truncate(row.NoteRaw, 1000),
                                Reason = Truncate(reason, 500)
                            });
                            batchFailed++;
                        }
                    }

                    job.Succeeded += batchSucceeded;
                    job.Failed += batchFailed;
                    await db.SaveChangesAsync(cancellationToken);

                    if (eventPublisher != null && pendingEvents.Count > 0)
                    {
                        foreach (var ev in pendingEvents)
                        {
                            await eventPublisher.PublishAsync(ev, "transaction.created", cancellationToken);
                        }
                    }

                    await dbTx.CommitAsync(cancellationToken);
                }
                catch
                {
                    await dbTx.RollbackAsync(cancellationToken);
                    throw;
                }
            }

            // Đồng bộ lại tổng số cuối cùng từ DB
            job.Succeeded = await db.Transactions.CountAsync(
                t => t.SourceType == SourceType.Import && t.SourceId == job.Id, cancellationToken);
            job.Failed = await db.ImportJobErrors.CountAsync(e => e.ImportJobId == job.Id, cancellationToken);
            job.Status = ImportJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            DeleteTempFileQuietly(job.TempFilePath);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "ImportJob {JobId} thất bại.", jobId);
            db.ChangeTracker.Clear();
            var freshJob = await db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
            if (freshJob != null)
            {
                // N49 / OB-09: Sau rollback batch, Succeeded/Failed phải phản ánh đúng số bản ghi thực tế đã commit
                freshJob.Succeeded = await db.Transactions.CountAsync(
                    t => t.SourceType == SourceType.Import && t.SourceId == jobId, CancellationToken.None);
                freshJob.Failed = await db.ImportJobErrors.CountAsync(
                    e => e.ImportJobId == jobId, CancellationToken.None);
                freshJob.Status = ImportJobStatus.Failed;
                freshJob.ErrorMessage = Truncate(ex.Message, 500);
                freshJob.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            DeleteTempFileQuietly(job.TempFilePath);
        }
    }

    private static async Task<List<T>> FetchWithRetryAsync<T>(Func<Task<List<T>>> action, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var result = await action();
            if (result.Count > 0 || attempt == 2)
            {
                return result;
            }
            await Task.Delay(500, ct);
        }
        return new List<T>();
    }

    private static void DeleteTempFileQuietly(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { File.Delete(path); } catch { /* ignore cleanup failures */ }
        }
    }

    private static string Truncate(string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= maxLen ? s : s[..maxLen];
    }
}
