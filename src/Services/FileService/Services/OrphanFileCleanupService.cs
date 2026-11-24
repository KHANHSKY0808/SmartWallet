using Microsoft.EntityFrameworkCore;
using FileService.Data;

namespace FileService.Services;

public class OrphanFileCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrphanFileCleanupService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public OrphanFileCleanupService(IServiceProvider serviceProvider, ILogger<OrphanFileCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrphanFileCleanupService bắt đầu chạy.");

        // Chờ 5 giây sau startup để khởi động xong
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOrphanFilesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Lỗi khi chạy OrphanFileCleanupService.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    public async Task CleanupOrphanFilesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FileDbContext>();

        var cutoffDate = DateTime.UtcNow.AddDays(-7);
        var orphans = await context.FileMetadatas
            .Where(f => f.DeletedAt != null ||
                        (f.RelatedEntityId == null && f.CreatedAt < cutoffDate) ||
                        (f.ExpiresAt != null && f.ExpiresAt < DateTime.UtcNow))
            .ToListAsync(cancellationToken);

        if (!orphans.Any())
        {
            return;
        }

        _logger.LogInformation("OrphanFileCleanupService: Tìm thấy {Count} file mồ côi hoặc hết hạn cần xóa.", orphans.Count);

        foreach (var file in orphans)
        {
            try
            {
                if (System.IO.File.Exists(file.StoragePath))
                {
                    System.IO.File.Delete(file.StoragePath);
                    _logger.LogInformation("Đã xóa file vật lý: {Path}", file.StoragePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể xóa file vật lý: {Path}", file.StoragePath);
            }
        }

        context.FileMetadatas.RemoveRange(orphans);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã hoàn tất dọn dẹp {Count} bản ghi file mồ côi.", orphans.Count);
    }
}
