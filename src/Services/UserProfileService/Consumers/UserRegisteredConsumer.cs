using SharedKernel.Events.EventContracts;
using SharedKernel.Messaging;
using UserProfileService.Services;

namespace UserProfileService.Consumers;

public class UserRegisteredConsumer : BaseRabbitMqConsumer<UserRegisteredEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UserRegisteredConsumer> _logger;

    public UserRegisteredConsumer(
        IConfiguration configuration,
        ILogger<UserRegisteredConsumer> logger,
        IServiceProvider serviceProvider)
        : base(configuration, logger, queueName: "profile.user-registered", routingKey: "user.registered")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ProcessEventAsync(UserRegisteredEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing UserRegisteredEvent for UserId {UserId}, Email {Email}", @event.UserId, @event.Email);

        using var scope = _serviceProvider.CreateScope();
        var profileService = scope.ServiceProvider.GetRequiredService<IProfileService>();

        await profileService.CreateOrUpdateDefaultProfileAsync(
            @event.UserId,
            @event.FullName,
            @event.DefaultCurrency);
    }
}
