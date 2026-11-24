using System.Text.Json;

namespace BudgetService.Services;

public record CategoryValidationResult(bool Exists, int? Type, Guid? ParentId, string? Name, List<Guid> ChildCategoryIds);

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
                return new CategoryValidationResult(false, null, null, null, new List<Guid>());
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            
            int? type = null;
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                type = typeProp.GetInt32();
            }

            Guid? parentId = null;
            if (doc.RootElement.TryGetProperty("parentId", out var parentProp) && parentProp.ValueKind == JsonValueKind.String)
            {
                if (Guid.TryParse(parentProp.GetString(), out var parsedParentId))
                {
                    parentId = parsedParentId;
                }
            }

            string? name = null;
            if (doc.RootElement.TryGetProperty("name", out var nameProp))
            {
                name = nameProp.GetString();
            }

            // D32: Nếu là danh mục cha (parentId == null), cần lấy danh sách danh mục con
            var childIds = new List<Guid>();
            if (parentId == null)
            {
                childIds = await GetChildCategoryIdsAsync(categoryId, bearerToken, cancellationToken);
            }

            return new CategoryValidationResult(true, type, parentId, name, childIds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi CategoryService để xác thực danh mục {CategoryId}", categoryId);
            return new CategoryValidationResult(false, null, null, null, new List<Guid>());
        }
    }

    private async Task<List<Guid>> GetChildCategoryIdsAsync(Guid parentCategoryId, string? bearerToken, CancellationToken cancellationToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                req.Headers.Add("Authorization", bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearerToken : $"Bearer {bearerToken}");
            }

            var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return new List<Guid>();
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var childIds = new List<Guid>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out var id))
                    {
                        if (id == parentCategoryId && el.TryGetProperty("children", out var childrenProp) && childrenProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var childEl in childrenProp.EnumerateArray())
                            {
                                if (childEl.TryGetProperty("id", out var childIdProp) && Guid.TryParse(childIdProp.GetString(), out var childId))
                                {
                                    childIds.Add(childId);
                                }
                            }
                        }
                    }
                }
            }

            return childIds;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể lấy danh sách danh mục con cho danh mục cha {ParentCategoryId}", parentCategoryId);
            return new List<Guid>();
        }
    }
}

