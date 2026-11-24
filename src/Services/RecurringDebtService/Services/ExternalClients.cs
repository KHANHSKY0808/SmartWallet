using System.Text.Json;

namespace RecurringDebtService.Services;

public record WalletValidationResult(bool Exists, Guid? UserId, bool IsArchived, string Currency);
public record CategoryValidationResult(bool Exists, Guid? UserId, int? Type, bool IsDefault, string Name);

public interface IWalletServiceClient
{
    Task<WalletValidationResult> ValidateWalletAsync(Guid walletId, string? bearerToken = null, CancellationToken cancellationToken = default);
}

public interface ICategoryServiceClient
{
    Task<CategoryValidationResult> ValidateCategoryAsync(Guid categoryId, string? bearerToken = null, CancellationToken cancellationToken = default);
}

public class WalletServiceClient : IWalletServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WalletServiceClient> _logger;

    public WalletServiceClient(HttpClient httpClient, ILogger<WalletServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WalletValidationResult> ValidateWalletAsync(Guid walletId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(bearerToken)
                ? $"/internal/wallets/{walletId}"
                : $"/api/wallets/{walletId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Add("Authorization", bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearerToken : $"Bearer {bearerToken}");
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new WalletValidationResult(false, null, false, "VND");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Guid? uid = null;
            if (root.TryGetProperty("userId", out var uProp) && Guid.TryParse(uProp.GetString(), out var parsedUid))
            {
                uid = parsedUid;
            }

            var isArchived = false;
            if (root.TryGetProperty("isArchived", out var aProp))
            {
                isArchived = aProp.GetBoolean();
            }

            var currency = "VND";
            if (root.TryGetProperty("currency", out var cProp))
            {
                currency = cProp.GetString() ?? "VND";
            }

            return new WalletValidationResult(true, uid, isArchived, currency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi kiểm tra ví {WalletId}", walletId);
            return new WalletValidationResult(false, null, false, "VND");
        }
    }
}

public class CategoryServiceClient : ICategoryServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CategoryServiceClient> _logger;

    public CategoryServiceClient(HttpClient httpClient, ILogger<CategoryServiceClient> logger)
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
                return new CategoryValidationResult(false, null, null, false, string.Empty);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Guid? uid = null;
            if (root.TryGetProperty("userId", out var uProp) && Guid.TryParse(uProp.GetString(), out var parsedUid))
            {
                uid = parsedUid;
            }

            int? type = null;
            if (root.TryGetProperty("type", out var tProp))
            {
                type = tProp.GetInt32();
            }

            var isDefault = false;
            if (root.TryGetProperty("isDefault", out var dProp))
            {
                isDefault = dProp.GetBoolean();
            }

            var name = string.Empty;
            if (root.TryGetProperty("name", out var nProp))
            {
                name = nProp.GetString() ?? string.Empty;
            }

            return new CategoryValidationResult(true, uid, type, isDefault, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi kiểm tra danh mục {CategoryId}", categoryId);
            return new CategoryValidationResult(false, null, null, false, string.Empty);
        }
    }
}

