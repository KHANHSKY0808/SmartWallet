using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using SharedKernel.Extensions;

namespace ApiGateway.Yarp.Services;

public record MonitoredServiceEndpoint(string Name, string ClusterKey, string DefaultUrl);

public record BackupFileManifest(string Service, string FileName, long SizeBytes, string Sha256);

public record FailedServiceBackup(string Service, string Error);

public class BackupManifestEntry
{
    public string BackupId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "Running";
    public string? ArchiveFile { get; set; }
    public List<BackupFileManifest> Files { get; set; } = new();
    public List<FailedServiceBackup> FailedServices { get; set; } = new();
}

public class BackupCoordinatorService
{
    public const int MaxRetentionCount = 14;
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static readonly MonitoredServiceEndpoint[] MonitoredServices =
    [
        new("IdentityService",      "identity-cluster",     "http://localhost:8001"),
        new("FileService",          "file-cluster",         "http://localhost:8012"),
        new("UserProfileService",   "profile-cluster",      "http://localhost:8002"),
        new("WalletService",        "wallet-cluster",       "http://localhost:8003"),
        new("TransactionService",   "transaction-cluster",  "http://localhost:8004"),
        new("CategoryService",      "category-cluster",     "http://localhost:8005"),
        new("BudgetService",        "budget-cluster",       "http://localhost:8006"),
        new("SavingGoalService",    "saving-cluster",       "http://localhost:8007"),
        new("RecurringDebtService", "recurring-cluster",    "http://localhost:8008"),
        new("ReportService",        "report-cluster",       "http://localhost:8009"),
        new("NotificationService",  "notification-cluster", "http://localhost:8010"),
        new("AuditService",         "audit-cluster",        "http://localhost:8011")
    ];

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BackupCoordinatorService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _manifestLock = new(1, 1);

    public BackupCoordinatorService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<BackupCoordinatorService> logger,
        TimeProvider? timeProvider = null)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string GetBackupDirectory()
    {
        var dir = _configuration["Backup:Directory"];
        return string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(Directory.GetCurrentDirectory(), "backups")
            : dir;
    }

    public async Task<string> TriggerBackupAsync()
    {
        var vnNow = _timeProvider.GetUtcNow().ToOffset(VietnamOffset);
        var backupId = $"{vnNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var startedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await UpsertManifestEntryAsync(new BackupManifestEntry
        {
            BackupId = backupId,
            StartedAt = startedAt,
            Status = "Running"
        });

        _ = Task.Run(() => ExecuteBackupAsync(backupId, startedAt, CancellationToken.None));
        return backupId;
    }

    public async Task<BackupManifestEntry> ExecuteBackupAsync(
        string backupId,
        DateTime? startedAt = null,
        CancellationToken cancellationToken = default)
    {
        var backupDir = GetBackupDirectory();
        Directory.CreateDirectory(backupDir);
        var backupSubDir = Path.Combine(backupDir, backupId);
        Directory.CreateDirectory(backupSubDir);

        var startUtc = startedAt ?? _timeProvider.GetUtcNow().UtcDateTime;
        var systemToken = ServiceTokenFactory.CreateSystemToken(_configuration, "ApiGateway");
        var client = _httpClientFactory.CreateClient("BackupProbe");

        var files = new List<BackupFileManifest>();
        var failed = new List<FailedServiceBackup>();

        var tasks = MonitoredServices.Select(async svc =>
        {
            var baseUrl = _configuration[$"ReverseProxy:Clusters:{svc.ClusterKey}:Destinations:d1:Address"] ?? svc.DefaultUrl;
            var url = $"{baseUrl.TrimEnd('/')}/internal/backup?backupId={Uri.EscapeDataString(backupId)}";
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", systemToken);
                req.Content = JsonContent.Create(new { backupId });

                using var resp = await client.SendAsync(req, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    return (File: (BackupFileManifest?)null, Error: new FailedServiceBackup(svc.Name, $"HTTP {(int)resp.StatusCode}"));
                }

                var payload = await resp.Content.ReadFromJsonAsync<BackupFileManifest>(JsonOpts, cancellationToken);
                if (payload is null || string.IsNullOrWhiteSpace(payload.FileName) || payload.SizeBytes <= 0)
                {
                    return (File: (BackupFileManifest?)null, Error: new FailedServiceBackup(svc.Name, "Invalid backup payload"));
                }

                return (File: payload, Error: (FailedServiceBackup?)null);
            }
            catch (Exception ex)
            {
                return (File: (BackupFileManifest?)null, Error: new FailedServiceBackup(svc.Name, ex.Message));
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var r in results)
        {
            if (r.File is not null) files.Add(r.File);
            if (r.Error is not null) failed.Add(r.Error);
        }

        string status;
        string? archiveName = null;
        if (failed.Count == 0 && files.Count == MonitoredServices.Length)
        {
            status = "Completed";
            archiveName = $"{backupId}.zip";
            var zipPath = Path.Combine(backupDir, archiveName);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(backupSubDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
            try { Directory.Delete(backupSubDir, recursive: true); } catch { }
        }
        else if (files.Count > 0)
        {
            status = "PartiallyFailed";
            _logger.LogWarning("Backup {BackupId} PartiallyFailed. Lỗi: {Errors}",
                backupId, string.Join(", ", failed.Select(f => $"{f.Service}:{f.Error}")));
        }
        else
        {
            status = "Failed";
            _logger.LogError("Backup {BackupId} Failed hoàn toàn.", backupId);
        }

        var entry = new BackupManifestEntry
        {
            BackupId = backupId,
            StartedAt = startUtc,
            CompletedAt = _timeProvider.GetUtcNow().UtcDateTime,
            Status = status,
            ArchiveFile = archiveName,
            Files = files.OrderBy(f => f.Service, StringComparer.OrdinalIgnoreCase).ToList(),
            FailedServices = failed.OrderBy(f => f.Service, StringComparer.OrdinalIgnoreCase).ToList()
        };

        await UpsertManifestEntryAsync(entry, pruneOldGoodBackups: status == "Completed");
        return entry;
    }

    public async Task<List<BackupManifestEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _manifestLock.WaitAsync(cancellationToken);
        try
        {
            return ReadManifestUnsafe();
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    private async Task UpsertManifestEntryAsync(BackupManifestEntry entry, bool pruneOldGoodBackups = false)
    {
        await _manifestLock.WaitAsync();
        try
        {
            var backupDir = GetBackupDirectory();
            Directory.CreateDirectory(backupDir);

            var entries = ReadManifestUnsafe();
            var idx = entries.FindIndex(e => string.Equals(e.BackupId, entry.BackupId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) entries[idx] = entry;
            else entries.Insert(0, entry);

            entries = entries.OrderByDescending(e => e.StartedAt).ToList();

            // D70 / BK-06 & BK-07: Chỉ giữ tối đa 14 bản Completed mới nhất, và KHÔNG xóa bản tốt cũ khi bản mới bị PartiallyFailed/Failed
            if (pruneOldGoodBackups)
            {
                var completed = entries.Where(e => e.Status == "Completed").OrderByDescending(e => e.StartedAt).ToList();
                if (completed.Count > MaxRetentionCount)
                {
                    var toRemove = completed.Skip(MaxRetentionCount).ToList();
                    foreach (var old in toRemove)
                    {
                        entries.Remove(old);
                        var zipPath = Path.Combine(backupDir, old.ArchiveFile ?? $"{old.BackupId}.zip");
                        if (File.Exists(zipPath)) { try { File.Delete(zipPath); } catch { } }
                        var folderPath = Path.Combine(backupDir, old.BackupId);
                        if (Directory.Exists(folderPath)) { try { Directory.Delete(folderPath, true); } catch { } }
                    }
                }
            }

            var manifestPath = Path.Combine(backupDir, "manifest.json");
            var tmpPath = manifestPath + ".tmp";
            var json = JsonSerializer.Serialize(entries, JsonOpts);
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, manifestPath, overwrite: true);
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    private List<BackupManifestEntry> ReadManifestUnsafe()
    {
        var manifestPath = Path.Combine(GetBackupDirectory(), "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new List<BackupManifestEntry>();
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<List<BackupManifestEntry>>(json, JsonOpts) ?? new List<BackupManifestEntry>();
        }
        catch
        {
            return new List<BackupManifestEntry>();
        }
    }
}

public class BackupSchedulerBackgroundService : BackgroundService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private readonly BackupCoordinatorService _coordinator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BackupSchedulerBackgroundService> _logger;
    private DateOnly? _lastRunVnDate;

    public BackupSchedulerBackgroundService(
        BackupCoordinatorService coordinator,
        TimeProvider timeProvider,
        ILogger<BackupSchedulerBackgroundService> logger)
    {
        _coordinator = coordinator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> CheckAndRunDueBackupAsync(CancellationToken cancellationToken = default)
    {
        var vnNow = _timeProvider.GetUtcNow().ToOffset(VietnamOffset);
        var today = DateOnly.FromDateTime(vnNow.DateTime);

        // D70 / BK-08: Lịch chạy 01:00 giờ VN hằng ngày, tối đa 1 lần mỗi ngày
        if (vnNow.Hour == 1 && _lastRunVnDate != today)
        {
            _lastRunVnDate = today;
            var backupId = $"{vnNow:yyyyMMdd-HHmmss}-sched";
            _logger.LogInformation("Bắt đầu sao lưu tự động 01:00 giờ VN ({BackupId})", backupId);
            await _coordinator.ExecuteBackupAsync(backupId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            return true;
        }

        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAndRunDueBackupAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Lỗi khi chạy tác vụ sao lưu định kỳ 01:00 giờ VN.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}

