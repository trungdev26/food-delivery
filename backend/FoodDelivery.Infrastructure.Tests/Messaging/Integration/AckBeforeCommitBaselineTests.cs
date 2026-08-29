using RabbitMQ.Client;
using System.Text;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class AckBeforeCommitBaselineTests
{
    [Fact]
    [Trait("Category", "BaselineTests")]
    public async Task AckBeforeBusinessCommit_LosesDelivery()
    {
        var queueName = $"baseline.early-ack.{Guid.NewGuid():N}";
        await using var connection = await RabbitMqEnvironment.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queueName, durable: false, exclusive: false, autoDelete: true);
        await channel.BasicPublishAsync(
            string.Empty,
            queueName,
            Encoding.UTF8.GetBytes("reserve-stock"));

        var delivery = await channel.BasicGetAsync(queueName, autoAck: false);
        Assert.NotNull(delivery);

        await channel.BasicAckAsync(delivery!.DeliveryTag, multiple: false);
        // Failure checkpoint: consumer stops before the business transaction commits.

        var redelivery = await channel.BasicGetAsync(queueName, autoAck: false);
        var queue = await channel.QueueDeclarePassiveAsync(queueName);

        Assert.Null(redelivery);
        Assert.Equal(0u, queue.MessageCount);
    }
}
