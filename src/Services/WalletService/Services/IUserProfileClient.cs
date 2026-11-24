using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace WalletService.Services;

public interface IUserProfileClient
{
    Task<decimal?> GetExchangeRateAsync(string fromCurrency, string toCurrency = "VND", CancellationToken cancellationToken = default);
}

public class UserProfileClient : IUserProfileClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserProfileClient> _logger;

    public UserProfileClient(HttpClient httpClient, ILogger<UserProfileClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<decimal?> GetExchangeRateAsync(string fromCurrency, string toCurrency = "VND", CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/internal/exchange-rates?base={fromCurrency}&target={toCurrency}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<ExchangeRateResponse>(cancellationToken: cancellationToken);
            return result?.Rate;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể lấy tỷ giá {From} sang {To} từ UserProfileService", fromCurrency, toCurrency);
            return null;
        }
    }

    private class ExchangeRateResponse
    {
        public string? BaseCurrency { get; set; }
        public string? TargetCurrency { get; set; }
        public decimal Rate { get; set; }
    }
}

