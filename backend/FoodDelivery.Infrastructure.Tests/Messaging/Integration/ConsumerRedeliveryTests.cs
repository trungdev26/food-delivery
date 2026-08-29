using RabbitMQ.Client;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class ConsumerRedeliveryTests
{
    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task ChannelClosesBeforeAck_MessageIsRedeliveredWithSameMessageId()
    {
        var queueName = $"redelivery.tests.{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid().ToString();
        await using var connection = await RabbitMqEnvironment.ConnectAsync();
        await using var setup = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        await setup.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        try
        {
            await setup.BasicPublishAsync(
                string.Empty,
                queueName,
                mandatory: true,
                new BasicProperties { MessageId = messageId, DeliveryMode = DeliveryModes.Persistent },
                Array.Empty<byte>());

            await using (var crashedConsumer = await connection.CreateChannelAsync())
            {
                var first = await crashedConsumer.BasicGetAsync(queueName, autoAck: false);
                Assert.NotNull(first);
                Assert.False(first!.Redelivered);
                // Crash checkpoint: channel closes before BasicAckAsync.
            }

            await using var recoveredConsumer = await connection.CreateChannelAsync();
            BasicGetResult? second = null;
            for (var attempt = 0; attempt < 50 && second is null; attempt++)
            {
                second = await recoveredConsumer.BasicGetAsync(queueName, autoAck: false);
                if (second is null) await Task.Delay(20);
            }

            Assert.NotNull(second);
            Assert.True(second!.Redelivered);
            Assert.Equal(messageId, second.BasicProperties.MessageId);
            await recoveredConsumer.BasicAckAsync(second.DeliveryTag, false);
        }
        finally
        {
            await setup.QueueDeleteAsync(queueName);
        }
    }
}
