namespace FoodDelivery.Infrastructure.Persistence.Messaging;

public sealed class InboxMessage
{
    public InboxMessage(string consumerName, Guid tenantId, Guid shopId, Guid messageId, DateTime processedAtUtc)
    {
        ConsumerName = consumerName;
        TenantId = tenantId;
        ShopId = shopId;
        MessageId = messageId;
        ProcessedAtUtc = processedAtUtc;
    }

    public string ConsumerName { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid MessageId { get; private set; }
    public DateTime ProcessedAtUtc { get; private set; }
}
