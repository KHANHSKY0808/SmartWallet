using System.Net.Http.Json;

namespace CategoryService.Services;

public interface ITransactionClient
{
    Task<bool> ReassignCategoryAsync(Guid oldCategoryId, Guid replacementCategoryId, string? bearerToken = null, CancellationToken cancellationToken = default);
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

    public async Task<bool> ReassignCategoryAsync(Guid oldCategoryId, Guid replacementCategoryId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/transactions/reassign-category");
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Add("Authorization", bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearerToken : $"Bearer {bearerToken}");
            }
            request.Content = JsonContent.Create(new
            {
                oldCategoryId,
                replacementCategoryId
            });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gọi TransactionService reassign category trả về mã {StatusCode}", response.StatusCode);
            }
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi TransactionService để reassign category {OldCategoryId} -> {ReplacementCategoryId}", oldCategoryId, replacementCategoryId);
            return false;
        }
    }
}

