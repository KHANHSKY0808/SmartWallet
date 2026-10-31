using SharedKernel.Events;

namespace SharedKernel.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;
}

