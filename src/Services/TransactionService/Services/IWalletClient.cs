using System.Text.Json;

namespace TransactionService.Services;

public record WalletValidationResult(bool Exists, bool IsArchived);

public interface IWalletClient
{
    Task<WalletValidationResult> ValidateWalletAsync(Guid walletId, string? bearerToken = null, CancellationToken cancellationToken = default);
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
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/wallets/{walletId}");
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

            return new WalletValidationResult(true, isArchived);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi gọi WalletService để xác thực ví {WalletId}", walletId);
            return new WalletValidationResult(false, false);
        }
    }
}
