using System.Text.Json;

namespace TransactionService.Services;

public record CategoryValidationResult(bool Exists, int? Type, Guid? ParentId = null, string? Name = null);
public record ImportCategoryInfo(Guid Id, Guid? UserId, string Name, int Type, Guid? ParentId);

public interface ICategoryClient
{
    Task<CategoryValidationResult> ValidateCategoryAsync(Guid categoryId, string? bearerToken = null, CancellationToken cancellationToken = default);
    Task<List<ImportCategoryInfo>> GetUserCategoriesAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(new List<ImportCategoryInfo>());
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

    public async Task<List<ImportCategoryInfo>> GetUserCategoriesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/internal/categories?userId={userId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<ImportCategoryInfo>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonSerializer.Deserialize<List<ImportCategoryInfo>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return list ?? new List<ImportCategoryInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi lấy danh mục của user {UserId} từ CategoryService", userId);
            return new List<ImportCategoryInfo>();
        }
    }
}

