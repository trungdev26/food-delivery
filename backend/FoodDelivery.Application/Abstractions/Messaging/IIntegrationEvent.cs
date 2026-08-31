namespace FoodDelivery.Application.Abstractions.Messaging;

public interface IIntegrationEvent
{
    Guid MessageId { get; }
    string EventName { get; }
    int ContractVersion { get; }
    DateTimeOffset OccurredAtUtc { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    Guid? ShopId { get; }
}
