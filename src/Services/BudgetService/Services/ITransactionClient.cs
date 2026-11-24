using System.Text.Json;

namespace BudgetService.Services;

public record BackfillTransactionItem(Guid Id, Guid UserId, decimal Amount, DateTime TransactionDate, Guid CategoryId);

public record BackfillResult(bool Success, List<BackfillTransactionItem> Transactions, string? ErrorMessage = null);

public interface ITransactionClient
{
    Task<BackfillResult> GetExpenseTransactionsAsync(
        IEnumerable<Guid> categoryIds, 
        DateTime startUtc, 
        DateTime endUtc, 
        string? bearerToken = null, 
        CancellationToken cancellationToken = default);
}

public class TransactionClient : ITransactionClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TransactionClient> _logger;

    public TransactionClient(HttpClient httpClient, ILogger<TransactionClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<BackfillResult> GetExpenseTransactionsAsync(
        IEnumerable<Guid> categoryIds, 
        DateTime startUtc, 
        DateTime endUtc, 
        string? bearerToken = null, 
        CancellationToken cancellationToken = default)
    {
        var allTransactions = new List<BackfillTransactionItem>();
        var catList = categoryIds.Distinct().ToList();

        try
        {
            foreach (var catId in catList)
            {
                var page = 1;
                const int pageSize = 100;
                var hasMore = true;

                while (hasMore)
                {
                    var url = $"/api/transactions?type=1&categoryId={catId}&startDate={Uri.EscapeDataString(startUtc.ToString("o"))}&endDate={Uri.EscapeDataString(endUtc.ToString("o"))}&page={page}&pageSize={pageSize}";
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (!string.IsNullOrWhiteSpace(bearerToken))
                    {
                        request.Headers.Add("Authorization", bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearerToken : $"Bearer {bearerToken}");
                    }

                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("TransactionService trả mã lỗi {StatusCode} khi backfill danh mục {CategoryId}", response.StatusCode, catId);
                        return new BackfillResult(false, new List<BackfillTransactionItem>(), $"TransactionService returned {response.StatusCode}");
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);

                    if (!doc.RootElement.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
                    {
                        break;
                    }

                    var countOnPage = 0;
                    foreach (var item in itemsElement.EnumerateArray())
                    {
                        countOnPage++;
                        var id = item.GetProperty("id").GetGuid();
                        var userId = item.GetProperty("userId").GetGuid();
                        var amount = item.GetProperty("amount").GetDecimal();
                        var txDate = item.GetProperty("transactionDate").GetDateTime();
                        var cId = item.TryGetProperty("categoryId", out var cProp) && cProp.ValueKind == JsonValueKind.String && Guid.TryParse(cProp.GetString(), out var parsedCId)
                            ? parsedCId
                            : catId;

                        allTransactions.Add(new BackfillTransactionItem(id, userId, amount, txDate, cId));
                    }

                    int totalPages = 1;
                    if (doc.RootElement.TryGetProperty("totalPages", out var totalPagesProp))
                    {
                        totalPages = totalPagesProp.GetInt32();
                    }

                    if (page >= totalPages || countOnPage == 0)
                    {
                        hasMore = false;
                    }
                    else
                    {
                        page++;
                    }
                }
            }

            return new BackfillResult(true, allTransactions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi kết nối tới TransactionService khi backfill giao dịch");
            return new BackfillResult(false, new List<BackfillTransactionItem>(), ex.Message);
        }
    }
}

