using SharedKernel.Events;
using SharedKernel.Messaging;
using WalletService.Services;

namespace WalletService.Tests;

public class TestEventPublisher : IEventPublisher
{
    public List<(IntegrationEvent Event, string RoutingKey)> Published { get; } = new();
    public bool ShouldThrow { get; set; } = false;

    public Task PublishAsync<T>(T @event, string routingKey, CancellationToken cancellationToken = default) where T : IntegrationEvent
    {
        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated publisher error");
        }
        Published.Add((@event, routingKey));
        return Task.CompletedTask;
    }
}

public class TestSavingGoalClient : ISavingGoalClient
{
    public Dictionary<Guid, SavingGoalInfo> Goals { get; } = new();

    public Task<SavingGoalInfo?> GetGoalAsync(Guid goalId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        Goals.TryGetValue(goalId, out var goal);
        return Task.FromResult(goal);
    }
}

public class TestUserProfileClient : IUserProfileClient
{
    public Dictionary<string, decimal> Rates { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool SimulateTimeout { get; set; }

    public Task<decimal?> GetExchangeRateAsync(string fromCurrency, string toCurrency = "VND", CancellationToken cancellationToken = default)
    {
        if (SimulateTimeout)
        {
            return Task.FromResult<decimal?>(null);
        }

        if (Rates.TryGetValue(fromCurrency, out var rate))
        {
            return Task.FromResult<decimal?>(rate);
        }

        return Task.FromResult<decimal?>(null);
    }
}

public class FailingCreditLedger : IWalletLedger
{
    public Task DebitAsync(Guid walletId, decimal amount, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task CreditAsync(Guid walletId, decimal amount, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated credit failure for saga compensation test");
    }
}

