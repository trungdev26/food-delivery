using Dapper;
using FoodDelivery.Infrastructure.Tests.Messaging.Simulation;
using RabbitMQ.Client;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class DirectPublishBaselineTests
{
    [Fact]
    [Trait("Category", "BaselineTests")]
    public async Task CommitThenCrashBeforePublish_LeavesMissingIntent()
    {
        var schema = new TestMessagingSchema();
        var runId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var queueName = $"baseline.direct-publish.{runId:N}";
        await schema.InitializeAsync();

        try
        {
            await using var brokerConnection = await RabbitMqEnvironment.ConnectAsync();
            await using var channel = await brokerConnection.CreateChannelAsync();
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: false, autoDelete: true);

            await using (var database = schema.CreateConnection())
            {
                await database.OpenAsync();
                await database.ExecuteAsync(
                    "INSERT INTO TestOrder(runId, id) VALUES (@runId, @orderId);",
                    new { runId, orderId });
            }

            // Failure checkpoint: process stops after commit and before BasicPublishAsync.

            await using var verification = schema.CreateConnection();
            await verification.OpenAsync();
            var orderExists = await verification.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM TestOrder WHERE runId = @runId AND id = @orderId);",
                new { runId, orderId });
            var queue = await channel.QueueDeclarePassiveAsync(queueName);

            Assert.True(orderExists);
            Assert.Equal(0u, queue.MessageCount);
        }
        finally
        {
            await schema.CleanupAsync(runId);
        }
    }
}
