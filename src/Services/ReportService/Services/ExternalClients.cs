using System.Text.Json;
using ReportService.DTOs;
using SharedKernel.Extensions;

namespace ReportService.Services;

public interface IExchangeRateClient
{
    Task<decimal?> GetRateToVndAsync(string baseCurrency, CancellationToken cancellationToken = default);
}

public interface IBudgetClient
{
    Task<(List<DashboardBudgetDto>? Budgets, bool Unavailable)> GetBudgetsAsync(int month, int year, string? bearerToken, CancellationToken cancellationToken = default);
}

public interface ICategoryLookupClient
{
    Task<(bool Exists, string Name, Guid? ParentId)> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}

public interface IWalletLookupClient
{
    Task<(bool Exists, string Currency)> GetWalletAsync(Guid walletId, CancellationToken cancellationToken = default);
}

public record BackfillTransactionItem(
    Guid Id,
    Guid UserId,
    Guid WalletId,
    Guid? CategoryId,
    int Type,
    decimal Amount,
    DateTime TransactionDate,
    string? Note,
    DateTime UpdatedAt);

public interface ITransactionBackfillClient
{
    Task<(List<BackfillTransactionItem> Items, int TotalCount)> FetchPageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}

public class ExchangeRateClient : IExchangeRateClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExchangeRateClient> _logger;

    public ExchangeRateClient(HttpClient httpClient, ILogger<ExchangeRateClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<decimal?> GetRateToVndAsync(string baseCurrency, CancellationToken cancellationToken = default)
    {
        var code = baseCurrency.Trim().ToUpperInvariant();
        if (code == "VND") return 1m;

        try
        {
            var resp = await _httpClient.GetAsync($"/internal/exchange-rates?base={Uri.EscapeDataString(code)}&target=VND", cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("rate", out var rProp) && rProp.TryGetDecimal(out var rate))
            {
                return rate;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi tra cứu tỷ giá {Currency} -> VND", code);
            return null;
        }
    }
}

public class BudgetClient : IBudgetClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BudgetClient> _logger;

    public BudgetClient(HttpClient httpClient, ILogger<BudgetClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<(List<DashboardBudgetDto>? Budgets, bool Unavailable)> GetBudgetsAsync(
        int month, int year, string? bearerToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return (null, true);
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2)); // D59: timeout > 2s -> budgetsUnavailable = true

            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/budgets?month={month}&year={year}");
            req.Headers.Add("Authorization", bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? bearerToken
                : $"Bearer {bearerToken}");

            var resp = await _httpClient.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return (null, true);
            }

            var json = await resp.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            var list = new List<DashboardBudgetDto>();
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var itemsProp))
            {
                root = itemsProp;
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var pId) && Guid.TryParse(pId.GetString(), out var gId) ? gId : Guid.Empty;
                    var catName = el.TryGetProperty("categoryName", out var pCat) ? pCat.GetString() ?? "Khác" : "Khác";
                    var limit = el.TryGetProperty("amount", out var pAmt) && pAmt.TryGetDecimal(out var dAmt) ? dAmt : 0m;
                    var spent = el.TryGetProperty("currentSpent", out var pSp) && pSp.TryGetDecimal(out var dSp) ? dSp : 0m;
                    var percent = el.TryGetProperty("percent", out var pPct) && pPct.TryGetDecimal(out var dPct) ? dPct : 0m;
                    list.Add(new DashboardBudgetDto
                    {
                        BudgetId = id,
                        CategoryName = catName,
                        Limit = limit,
                        Spent = spent,
                        Percent = percent
                    });
                }
            }

            return (list, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể đọc BudgetService cho tháng {Month}/{Year}", month, year);
            return (null, true);
        }
    }
}

public class CategoryLookupClient : ICategoryLookupClient
{
    private readonly HttpClient _httpClient;

    public CategoryLookupClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Exists, string Name, Guid? ParentId)> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _httpClient.GetAsync($"/internal/categories/{categoryId}", cancellationToken);
            if (!resp.IsSuccessStatusCode) return (false, string.Empty, null);

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var name = doc.RootElement.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty;
            Guid? parentId = null;
            if (doc.RootElement.TryGetProperty("parentId", out var pProp) && pProp.ValueKind == JsonValueKind.String &&
                Guid.TryParse(pProp.GetString(), out var parsed))
            {
                parentId = parsed;
            }
            return (true, name, parentId);
        }
        catch
        {
            return (false, string.Empty, null);
        }
    }
}

public class WalletLookupClient : IWalletLookupClient
{
    private readonly HttpClient _httpClient;

    public WalletLookupClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Exists, string Currency)> GetWalletAsync(Guid walletId, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _httpClient.GetAsync($"/internal/wallets/{walletId}", cancellationToken);
            if (!resp.IsSuccessStatusCode) return (false, "VND");

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var currency = doc.RootElement.TryGetProperty("currency", out var cProp) ? cProp.GetString() ?? "VND" : "VND";
            return (true, currency);
        }
        catch
        {
            return (false, "VND");
        }
    }
}

public class TransactionBackfillClient : ITransactionBackfillClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TransactionBackfillClient> _logger;

    public TransactionBackfillClient(HttpClient httpClient, IConfiguration configuration, ILogger<TransactionBackfillClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(List<BackfillTransactionItem> Items, int TotalCount)> FetchPageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = ServiceTokenFactory.CreateSystemToken(_configuration, "ReportService");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/internal/transactions?page={page}&pageSize={pageSize}");
            req.Headers.Add("Authorization", $"Bearer {token}");

            var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Backfill /internal/transactions trả mã {Status}", resp.StatusCode);
                return (new List<BackfillTransactionItem>(), 0);
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var totalCount = doc.RootElement.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : 0;
            var list = new List<BackfillTransactionItem>();

            if (doc.RootElement.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in itemsProp.EnumerateArray())
                {
                    var id = el.GetProperty("id").GetGuid();
                    var userId = el.GetProperty("userId").GetGuid();
                    var walletId = el.GetProperty("walletId").GetGuid();
                    Guid? catId = el.TryGetProperty("categoryId", out var cProp) && cProp.ValueKind == JsonValueKind.String
                        && Guid.TryParse(cProp.GetString(), out var gCat) ? gCat : null;
                    var type = el.GetProperty("type").GetInt32();
                    var amount = el.GetProperty("amount").GetDecimal();
                    var txDate = el.GetProperty("transactionDate").GetDateTime();
                    var note = el.TryGetProperty("note", out var nProp) && nProp.ValueKind == JsonValueKind.String ? nProp.GetString() : null;
                    var updatedAt = el.GetProperty("updatedAt").GetDateTime();

                    list.Add(new BackfillTransactionItem(id, userId, walletId, catId, type, amount, txDate, note, updatedAt));
                }
            }

            return (list, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi backfill TransactionService trang {Page}", page);
            return (new List<BackfillTransactionItem>(), 0);
        }
    }
}

