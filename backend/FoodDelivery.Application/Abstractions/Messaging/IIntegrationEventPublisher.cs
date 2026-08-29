namespace FoodDelivery.Application.Abstractions.Messaging;

public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(
        TEvent integrationEvent,
        string routingKey,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}
