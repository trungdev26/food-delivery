namespace FoodDelivery.Infrastructure.Messaging;

public sealed record IntegrationMessage(
    Guid MessageId,
    string EventName,
    int ContractVersion,
    DateTimeOffset OccurredAtUtc,
    string? CorrelationId,
    Guid? TenantId,
    Guid? ShopId,
    ReadOnlyMemory<byte> Payload);
