using System.Text.Json;

namespace TransactionService.Services;

public record CategoryValidationResult(bool Exists, int? Type);

public interface ICategoryClient
{
    Task<CategoryValidationResult> ValidateCategoryAsync(Guid categoryId, string? bearerToken = null, CancellationToken cancellationToken = default);
}

public class CategoryClient : ICategoryClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CategoryClient> _logger;

    public CategoryClient(HttpClient httpClient, ILogger<CategoryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CategoryValidationResult> ValidateCategoryAsync(Guid categoryId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/categories/{categoryId}");
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Add("Authorization", bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearerToken : $"Bearer {bearerToken}");
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new CategoryValidationResult(false, null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return new CategoryValidationResult(true, typeProp.GetInt32());
            }

            return new CategoryValidationResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi CategoryService để xác thực danh mục {CategoryId}", categoryId);
            return new CategoryValidationResult(false, null);
        }
    }
}

