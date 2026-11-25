using Microsoft.EntityFrameworkCore;
using ReportService.Data;
using ReportService.Models;

namespace ReportService.Services;

public interface IBackfillService
{
    Task<int> BackfillIfEmptyAsync(CancellationToken cancellationToken = default);
}

public class BackfillService : IBackfillService
{
    private readonly ReportDbContext _db;
    private readonly ITransactionBackfillClient _txClient;
    private readonly IWalletLookupClient _walletClient;
    private readonly ICategoryLookupClient _categoryClient;
    private readonly ILogger<BackfillService> _logger;

    public BackfillService(
        ReportDbContext db,
        ITransactionBackfillClient txClient,
        IWalletLookupClient walletClient,
        ICategoryLookupClient categoryClient,
        ILogger<BackfillService> logger)
    {
        _db = db;
        _txClient = txClient;
        _walletClient = walletClient;
        _categoryClient = categoryClient;
        _logger = logger;
    }

    public async Task<int> BackfillIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.TransactionProjections.AnyAsync(cancellationToken))
        {
            return 0;
        }

        int inserted = 0;
        int page = 1;
        const int pageSize = 500;
        var walletCurrencies = new Dictionary<Guid, string>();
        var catInfos = new Dictionary<Guid, (string Name, Guid? ParentId)>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var (items, totalCount) = await _txClient.FetchPageAsync(page, pageSize, cancellationToken);
            if (items.Count == 0) break;

            foreach (var item in items)
            {
                var exists = await _db.TransactionProjections.AnyAsync(t => t.TransactionId == item.Id, cancellationToken);
                if (exists) continue;

                if (!walletCurrencies.TryGetValue(item.WalletId, out var currency))
                {
                    var wRes = await _walletClient.GetWalletAsync(item.WalletId, cancellationToken);
                    currency = wRes.Exists ? wRes.Currency : "VND";
                    walletCurrencies[item.WalletId] = currency;
                }

                string? catName = null;
                Guid? catParentId = null;
                if (item.CategoryId.HasValue && item.CategoryId.Value != Guid.Empty)
                {
                    if (!catInfos.TryGetValue(item.CategoryId.Value, out var cInfo))
                    {
                        var cRes = await _categoryClient.GetCategoryAsync(item.CategoryId.Value, cancellationToken);
                        cInfo = cRes.Exists ? (cRes.Name, cRes.ParentId) : ("Khác", null);
                        catInfos[item.CategoryId.Value] = cInfo;

                        if (cRes.Exists && !await _db.ReportCategories.AnyAsync(c => c.CategoryId == item.CategoryId.Value, cancellationToken))
                        {
                            _db.ReportCategories.Add(new ReportCategoryCache
                            {
                                CategoryId = item.CategoryId.Value,
                                UserId = item.UserId,
                                Name = cRes.Name,
                                ParentId = cRes.ParentId,
                                UpdatedAt = DateTime.UtcNow
                            });
                        }

                        if (cRes.ParentId.HasValue && !catInfos.ContainsKey(cRes.ParentId.Value))
                        {
                            var pRes = await _categoryClient.GetCategoryAsync(cRes.ParentId.Value, cancellationToken);
                            if (pRes.Exists)
                            {
                                catInfos[cRes.ParentId.Value] = (pRes.Name, pRes.ParentId);
                                if (!await _db.ReportCategories.AnyAsync(c => c.CategoryId == cRes.ParentId.Value, cancellationToken))
                                {
                                    _db.ReportCategories.Add(new ReportCategoryCache
                                    {
                                        CategoryId = cRes.ParentId.Value,
                                        UserId = item.UserId,
                                        Name = pRes.Name,
                                        ParentId = pRes.ParentId,
                                        UpdatedAt = DateTime.UtcNow
                                    });
                                }
                            }
                        }
                    }
                    catName = cInfo.Name;
                    catParentId = cInfo.ParentId;
                }

                _db.TransactionProjections.Add(new TransactionProjection
                {
                    TransactionId = item.Id,
                    UserId = item.UserId,
                    WalletId = item.WalletId,
                    CategoryId = item.CategoryId,
                    CategoryParentId = catParentId,
                    CategoryName = catName,
                    Type = item.Type,
                    Amount = item.Amount,
                    Currency = currency,
                    TransactionDate = item.TransactionDate,
                    Note = item.Note,
                    IsDeleted = false,
                    LastEventAt = item.UpdatedAt,
                    UpdatedAt = DateTime.UtcNow
                });
                inserted++;
            }

            await _db.SaveChangesAsync(cancellationToken);

            if (page * pageSize >= totalCount) break;
            page++;
        }

        if (inserted > 0)
        {
            _logger.LogInformation("ReportService: Backfill hoàn tất {Count} giao dịch vào TransactionProjections", inserted);
        }

        return inserted;
    }
}

public class BackfillHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackfillHostedService> _logger;

    public BackfillHostedService(IServiceProvider serviceProvider, ILogger<BackfillHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Đợi ngắn để TransactionService sẵn sàng khi startup song song (RP-06)
        await Task.Delay(TimeSpan.FromSeconds(1.5), stoppingToken);

        for (int attempt = 1; attempt <= 5 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var backfill = scope.ServiceProvider.GetRequiredService<IBackfillService>();
                await backfill.BackfillIfEmptyAsync(stoppingToken);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Thử lại backfill lần {Attempt}", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}

