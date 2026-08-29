using FoodDelivery.Application.Abstractions.Messaging;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Unit;

public sealed class IntegrationEventContractTests
{
    [Fact]
    public void Contract_ExposesStableTransportMetadata()
    {
        var messageId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var occurredAtUtc = DateTimeOffset.UtcNow;
        IIntegrationEvent integrationEvent = new TestIntegrationEvent(
            messageId,
            "InventoryReserveRequested",
            1,
            occurredAtUtc,
            "order-001",
            tenantId);

        Assert.Equal(messageId, integrationEvent.MessageId);
        Assert.Equal("InventoryReserveRequested", integrationEvent.EventName);
        Assert.Equal(1, integrationEvent.ContractVersion);
        Assert.Equal(occurredAtUtc, integrationEvent.OccurredAtUtc);
        Assert.Equal("order-001", integrationEvent.CorrelationId);
        Assert.Equal(tenantId, integrationEvent.TenantId);
    }

    private sealed record TestIntegrationEvent(
        Guid MessageId,
        string EventName,
        int ContractVersion,
        DateTimeOffset OccurredAtUtc,
        string? CorrelationId,
        Guid? TenantId) : IIntegrationEvent;
}
