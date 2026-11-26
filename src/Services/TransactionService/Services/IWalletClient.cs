using System.Text.Json;

namespace TransactionService.Services;

public record WalletValidationResult(bool Exists, bool IsArchived, string Currency = "VND");
public record ImportWalletInfo(Guid Id, string Name, string Currency, bool IsArchived);

public interface IWalletClient
{
    Task<WalletValidationResult> ValidateWalletAsync(Guid walletId, string? bearerToken = null, CancellationToken cancellationToken = default);
    Task<List<ImportWalletInfo>> GetUserWalletsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(new List<ImportWalletInfo>());
}

public class WalletClient : IWalletClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WalletClient> _logger;

    public WalletClient(HttpClient httpClient, ILogger<WalletClient> logger)
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
                return new WalletValidationResult(false, false);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var isArchived = false;
            if (doc.RootElement.TryGetProperty("isArchived", out var archProp) ||
                doc.RootElement.TryGetProperty("IsArchived", out archProp))
            {
                isArchived = archProp.GetBoolean();
            }

            var currency = "VND";
            if ((doc.RootElement.TryGetProperty("currency", out var currProp) ||
                 doc.RootElement.TryGetProperty("Currency", out currProp)) &&
                currProp.ValueKind == JsonValueKind.String)
            {
                currency = currProp.GetString() ?? "VND";
            }

            return new WalletValidationResult(true, isArchived, currency);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi WalletService để xác thực ví {WalletId}", walletId);
            return new WalletValidationResult(false, false);
        }
    }

    public async Task<List<ImportWalletInfo>> GetUserWalletsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/internal/wallets?userId={userId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<ImportWalletInfo>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonSerializer.Deserialize<List<ImportWalletInfo>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return list ?? new List<ImportWalletInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi lấy danh sách ví của user {UserId} từ WalletService", userId);
            return new List<ImportWalletInfo>();
        }
    }
}
