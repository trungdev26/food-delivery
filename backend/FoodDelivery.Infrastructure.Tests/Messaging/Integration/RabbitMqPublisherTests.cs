using System.Text.Json;
using FoodDelivery.Application.Abstractions.Messaging;
using FoodDelivery.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class RabbitMqPublisherTests
{
    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task PublishAsync_PersistsWireMetadataAndJsonBody()
    {
        var options = CreateOptions();
        var queueName = $"publisher-wire.{Guid.NewGuid():N}";
        const string routingKey = "inventory.reserve.requested.v1";
        await using var manager = new RabbitMqConnectionManager(options);
        await using var publisher = new RabbitMqPublisher(manager, options);
        var connection = await manager.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queueName, durable: false, exclusive: false, autoDelete: true);
        await channel.ExchangeDeclareAsync(options.ExchangeName, ExchangeType.Direct, durable: true);
        await channel.QueueBindAsync(queueName, options.ExchangeName, routingKey);
        var integrationEvent = TestIntegrationEvent.Create();

        try
        {
            await publisher.PublishAsync(integrationEvent, routingKey);

            var delivery = await channel.BasicGetAsync(queueName, autoAck: true);
            Assert.NotNull(delivery);
            Assert.Equal(integrationEvent.MessageId.ToString(), delivery!.BasicProperties.MessageId);
            Assert.Equal(integrationEvent.EventName, delivery.BasicProperties.Type);
            Assert.Equal(integrationEvent.CorrelationId, delivery.BasicProperties.CorrelationId);
            Assert.Equal("application/json", delivery.BasicProperties.ContentType);
            Assert.Equal(DeliveryModes.Persistent, delivery.BasicProperties.DeliveryMode);
            Assert.Equal(1, Convert.ToInt32(delivery.BasicProperties.Headers!["x-event-version"]));
            Assert.Equal(integrationEvent.TenantId!.Value.ToString(), HeaderText(delivery, "x-tenant-id"));
            Assert.Equal(integrationEvent.ShopId!.Value.ToString(), HeaderText(delivery, "x-shop-id"));

            var body = JsonSerializer.Deserialize<TestIntegrationEvent>(delivery.Body.Span);
            Assert.Equal(integrationEvent, body);
        }
        finally
        {
            await channel.ExchangeDeleteAsync(options.ExchangeName);
        }
    }

    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task PublishRawAsync_PreservesPersistedPayloadAndMetadata()
    {
        var options = CreateOptions();
        var queueName = $"publisher-raw.{Guid.NewGuid():N}";
        const string routingKey = "inventory.raw.v1";
        await using var manager = new RabbitMqConnectionManager(options);
        await using var publisher = new RabbitMqPublisher(manager, options);
        var connection = await manager.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queueName, durable: false, exclusive: false, autoDelete: true);
        await channel.ExchangeDeclareAsync(options.ExchangeName, ExchangeType.Direct, durable: true);
        await channel.QueueBindAsync(queueName, options.ExchangeName, routingKey);
        var tenantId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"quantity\":10}");
        var message = new IntegrationMessage(Guid.NewGuid(), "InventoryAdjusted", 2,
            DateTimeOffset.UtcNow, "order-002", tenantId, shopId, payload);

        try
        {
            await publisher.PublishRawAsync(message, routingKey);

            var delivery = await channel.BasicGetAsync(queueName, autoAck: true);
            Assert.NotNull(delivery);
            Assert.Equal(payload, delivery!.Body.ToArray());
            Assert.Equal(message.MessageId.ToString(), delivery.BasicProperties.MessageId);
            Assert.Equal(shopId.ToString(), HeaderText(delivery, "x-shop-id"));
        }
        finally
        {
            await channel.ExchangeDeleteAsync(options.ExchangeName);
        }
    }

    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task PublishAsync_UnroutableMandatoryMessage_ThrowsPublishException()
    {
        var options = CreateOptions();
        await using var manager = new RabbitMqConnectionManager(options);
        await using var publisher = new RabbitMqPublisher(manager, options);
        try
        {
            var exception = await Assert.ThrowsAsync<PublishReturnException>(() =>
                publisher.PublishAsync(TestIntegrationEvent.Create(), "no.queue.is.bound"));

            Assert.Equal(Constants.NoRoute, exception.ReplyCode);
        }
        finally
        {
            var connection = await manager.GetConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            await channel.ExchangeDeleteAsync(options.ExchangeName);
        }
    }

    private static RabbitMqOptions CreateOptions() => new()
    {
        HostName = RabbitMqEnvironment.HostName,
        Port = RabbitMqEnvironment.Port,
        UserName = RabbitMqEnvironment.UserName,
        Password = RabbitMqEnvironment.Password,
        VirtualHost = RabbitMqEnvironment.VirtualHost,
        ConnectionName = $"publisher-tests-{Guid.NewGuid():N}",
        ExchangeName = $"food.events.tests.{Guid.NewGuid():N}"
    };

    private static string HeaderText(BasicGetResult delivery, string name)
    {
        var value = Assert.IsType<byte[]>(delivery.BasicProperties.Headers![name]);
        return System.Text.Encoding.UTF8.GetString(value);
    }

    private sealed record TestIntegrationEvent(
        Guid MessageId,
        string EventName,
        int ContractVersion,
        DateTimeOffset OccurredAtUtc,
        string? CorrelationId,
        Guid? TenantId,
        Guid? ShopId,
        Guid HangHoaId,
        int SoLuong) : IIntegrationEvent
    {
        internal static TestIntegrationEvent Create() => new(
            Guid.NewGuid(),
            "InventoryReserveRequested",
            1,
            DateTimeOffset.UtcNow,
            $"order-{Guid.NewGuid():N}",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            10);
    }
}
