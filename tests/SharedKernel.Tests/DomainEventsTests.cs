using System.Text.Json;
using SharedKernel.Events.EventContracts;
using Xunit;

namespace SharedKernel.Tests;

public class DomainEventsTests
{
    [Fact]
    public void UserRegisteredEvent_ShouldSerializeAndDeserializeCorrectly()
    {
        var original = new UserRegisteredEvent
        {
            UserId = Guid.NewGuid(),
            Email = "user@example.com",
            FullName = "Nguyễn Văn A",
            DefaultCurrency = "VND"
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<UserRegisteredEvent>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.UserId, deserialized.UserId);
        Assert.Equal(original.Email, deserialized.Email);
        Assert.Equal("UserRegisteredEvent", deserialized.EventType);
    }

    [Fact]
    public void BudgetThresholdReachedEvent_ShouldPreserveValues()
    {
        var original = new BudgetThresholdReachedEvent
        {
            BudgetId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            CategoryName = "Ăn uống",
            Percent = 80,
            CurrentSpent = 1600000m,
            BudgetLimit = 2000000m,
            Month = 11,
            Year = 2026
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<BudgetThresholdReachedEvent>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(80, deserialized.Percent);
        Assert.Equal(1600000m, deserialized.CurrentSpent);
        Assert.Equal(2000000m, deserialized.BudgetLimit);
    }
}

