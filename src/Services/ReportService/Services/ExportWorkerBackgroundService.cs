using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using ReportService.Data;
using ReportService.DTOs;
using ReportService.Models;

namespace ReportService.Services;

public class ExportWorkerBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExportWorkerBackgroundService> _logger;

    public ExportWorkerBackgroundService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<ExportWorkerBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string GetStorageDirectory()
    {
        var configured = _configuration["Export:StoragePath"];
        var dir = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "exports");
        Directory.CreateDirectory(dir);
        return dir;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingJobsAsync(stoppingToken);
                await CleanupExpiredExportsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Lỗi vòng lặp ExportWorkerBackgroundService");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(350), stoppingToken);
        }
    }

    public async Task ProcessPendingJobsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<IExportGenerator>();
        var rateClient = scope.ServiceProvider.GetRequiredService<IExchangeRateClient>();
        var publisher = scope.ServiceProvider.GetService<IEventPublisher>();

        var queuedJobs = await db.ExportJobs
            .Where(j => j.Status == ExportJobStatus.Queued)
            .OrderBy(j => j.CreatedAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        foreach (var job in queuedJobs)
        {
            job.Status = ExportJobStatus.Processing;
            job.StartedAt = _timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                var p = JsonSerializer.Deserialize<ExportParameters>(job.ParametersJson) ?? new ExportParameters();
                var from = DateOnly.Parse(p.From);
                var to = DateOnly.Parse(p.To);

                var (startUtc, endUtc) = VietnamTimeHelper.GetDateRangeUtc(from, to);

                var txList = await db.TransactionProjections
                    .AsNoTracking()
                    .Where(t => t.UserId == job.UserId
                                && !t.IsDeleted
                                && t.TransactionDate >= startUtc
                                && t.TransactionDate < endUtc)
                    .OrderBy(t => t.TransactionDate)
                    .ToListAsync(cancellationToken);

                var rateMap = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase) { ["VND"] = 1m };
                var exportRows = new List<ExportTransactionRow>(txList.Count);

                foreach (var t in txList)
                {
                    var cur = string.IsNullOrWhiteSpace(t.Currency) ? "VND" : t.Currency.Trim().ToUpperInvariant();
                    if (!rateMap.TryGetValue(cur, out var rate))
                    {
                        rate = await rateClient.GetRateToVndAsync(cur, cancellationToken);
                        rateMap[cur] = rate;
                    }

                    var vndAmount = rate.HasValue
                        ? Math.Round(t.Amount * rate.Value, 2, MidpointRounding.AwayFromZero)
                        : 0m;

                    exportRows.Add(new ExportTransactionRow(
                        t.TransactionDate,
                        t.Type,
                        t.CategoryName ?? "Khác",
                        t.Amount,
                        cur,
                        vndAmount,
                        t.Note));
                }

                byte[] bytes = job.Format == ExportFormat.Pdf
                    ? generator.GeneratePdf(from, to, exportRows)
                    : generator.GenerateExcel(from, to, exportRows);

                var ext = job.Format == ExportFormat.Pdf ? "pdf" : "xlsx";
                var storageFileName = $"{Guid.NewGuid()}.{ext}";
                var fullPath = Path.Combine(GetStorageDirectory(), storageFileName);
                await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                job.StorageFileName = storageFileName;
                job.FileId = null; // D60: null ở 6a
                job.Status = ExportJobStatus.Completed;
                job.CompletedAt = nowUtc;
                job.ExpiresAt = nowUtc.AddDays(7);
                await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                if (publisher != null)
                {
                    await publisher.PublishAsync(new ExportCompletedEvent
                    {
                        JobId = job.Id,
                        UserId = job.UserId,
                        Format = ext
                    }, "report.export_completed", cancellationToken);
                }

                await tx.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Export job {JobId} thất bại", job.Id);
                job.Status = ExportJobStatus.Failed;
                job.ErrorMessage = "Không thể tạo báo cáo, vui lòng thử lại"; // EX-10: không lộ stack trace
                job.CompletedAt = _timeProvider.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Dọn dẹp các file export hết hạn sau 7 ngày (EX-08 / D60).
    /// </summary>
    public async Task CleanupExpiredExportsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var expiredJobs = await db.ExportJobs
            .Where(j => j.ExpiresAt != null && j.ExpiresAt < nowUtc && j.StorageFileName != null)
            .ToListAsync(cancellationToken);

        if (expiredJobs.Count == 0) return;

        var dir = GetStorageDirectory();
        foreach (var job in expiredJobs)
        {
            var path = Path.Combine(dir, job.StorageFileName!);
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
            job.StorageFileName = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

