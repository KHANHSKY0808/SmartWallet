using System.Net.Http.Json;

namespace WalletService.Services;

public class SavingGoalInfo
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = "InProgress";
    public int StatusCode { get; set; } = 1;
}

public interface ISavingGoalClient
{
    Task<SavingGoalInfo?> GetGoalAsync(Guid goalId, string? bearerToken = null, CancellationToken cancellationToken = default);
}

public class SavingGoalClient : ISavingGoalClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SavingGoalClient> _logger;

    public SavingGoalClient(HttpClient httpClient, ILogger<SavingGoalClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SavingGoalInfo?> GetGoalAsync(Guid goalId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/savings/{goalId}");
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                req.Headers.TryAddWithoutValidation("Authorization", bearerToken);
            }
            var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<SavingGoalInfo>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi khi gọi SavingGoalService lấy mục tiêu {GoalId}", goalId);
            return null;
        }
    }
}
