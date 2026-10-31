using SharedKernel.Events;

namespace SharedKernel.Messaging;

public interface IEventConsumer<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

