using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace SavingGoalService.Services;

public class WalletInfo
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Currency { get; set; } = "VND";
    public bool IsArchived { get; set; }
}

public interface IWalletServiceClient
{
    Task<WalletInfo?> GetWalletAsync(Guid walletId, CancellationToken cancellationToken = default);
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

    public async Task<WalletInfo?> GetWalletAsync(Guid walletId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/internal/wallets/{walletId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            return await response.Content.ReadFromJsonAsync<WalletInfo>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi khi gọi WalletService lấy thông tin ví {WalletId}", walletId);
            return null;
        }
    }
}
