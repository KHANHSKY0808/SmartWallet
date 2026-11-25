using System.Text.Json;

namespace TransactionService.Services;

public record CategoryValidationResult(bool Exists, int? Type, Guid? ParentId = null, string? Name = null);

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
            var path = string.IsNullOrWhiteSpace(bearerToken)
                ? $"/internal/categories/{categoryId}"
                : $"/api/categories/{categoryId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
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
            int? type = null;
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                type = typeProp.GetInt32();
            }

            Guid? parentId = null;
            if ((doc.RootElement.TryGetProperty("parentId", out var parentProp) ||
                 doc.RootElement.TryGetProperty("ParentId", out parentProp)) && parentProp.ValueKind == JsonValueKind.String)
            {
                if (Guid.TryParse(parentProp.GetString(), out var parsedParentId))
                {
                    parentId = parsedParentId;
                }
            }

            string? name = null;
            if ((doc.RootElement.TryGetProperty("name", out var nameProp) ||
                 doc.RootElement.TryGetProperty("Name", out nameProp)) && nameProp.ValueKind == JsonValueKind.String)
            {
                name = nameProp.GetString();
            }

            return new CategoryValidationResult(true, type, parentId, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi CategoryService để xác thực danh mục {CategoryId}", categoryId);
            return new CategoryValidationResult(false, null);
        }
    }
}

