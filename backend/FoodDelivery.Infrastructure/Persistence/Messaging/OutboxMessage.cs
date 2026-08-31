namespace FoodDelivery.Infrastructure.Persistence.Messaging;

public sealed class OutboxMessage
{
    public OutboxMessage(
        Guid id,
        string eventName,
        int contractVersion,
        string routingKey,
        string payload,
        DateTime occurredAtUtc,
        string? correlationId,
        Guid? tenantId,
        Guid? shopId)
    {
        Id = id;
        EventName = eventName;
        ContractVersion = contractVersion;
        RoutingKey = routingKey;
        Payload = payload;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        TenantId = tenantId;
        ShopId = shopId;
    }

    public Guid Id { get; private set; }
    public string EventName { get; private set; }
    public int ContractVersion { get; private set; }
    public string RoutingKey { get; private set; }
    public string Payload { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? CorrelationId { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? ShopId { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public string? LockedBy { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
}
