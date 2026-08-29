using Dapper;
using System.Text;
using FoodDelivery.Infrastructure.Tests.Messaging.Simulation;
using RabbitMQ.Client;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class ReliableMessagingReferenceTests
{
    [Fact]
    [Trait("Category", "CorrectnessTests")]
    public async Task CrashAfterConfirm_OutboxRetriesWithSameMessageId()
    {
        var schema = new TestMessagingSchema();
        var runId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var queueName = $"correctness.outbox.{runId:N}";
        await schema.InitializeAsync();

        try
        {
            await using (var database = schema.CreateConnection())
            {
                await database.OpenAsync();
                await using var transaction = await database.BeginTransactionAsync();
                await database.ExecuteAsync(
                    "INSERT INTO TestOrder(runId,id) VALUES (@runId,@orderId); " +
                    "INSERT INTO TestOutbox(runId,messageId,payload) VALUES (@runId,@messageId,JSON_OBJECT('orderId',@orderId));",
                    new { runId, orderId, messageId }, transaction);
                await transaction.CommitAsync();
            }

            await using var broker = await RabbitMqEnvironment.ConnectAsync();
            await using var channel = await broker.CreateChannelAsync(new CreateChannelOptions(true, true));
            await channel.QueueDeclareAsync(queueName, durable: false, exclusive: false, autoDelete: true);
            var properties = new BasicProperties { MessageId = messageId.ToString(), DeliveryMode = DeliveryModes.Persistent };

            var body = Encoding.UTF8.GetBytes("{}");
            await channel.BasicPublishAsync(string.Empty, queueName, true, properties, body);
            // Crash checkpoint: confirm received, sentAt not committed.
            await channel.BasicPublishAsync(string.Empty, queueName, true, properties, body);

            await using var verification = schema.CreateConnection();
            await verification.OpenAsync();
            await verification.ExecuteAsync(
                "UPDATE TestOutbox SET sentAt=UTC_TIMESTAMP(6) WHERE runId=@runId AND messageId=@messageId;",
                new { runId, messageId });
            var first = await channel.BasicGetAsync(queueName, true);
            var second = await channel.BasicGetAsync(queueName, true);
            var sent = await verification.ExecuteScalarAsync<bool>(
                "SELECT sentAt IS NOT NULL FROM TestOutbox WHERE runId=@runId AND messageId=@messageId;",
                new { runId, messageId });

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first!.BasicProperties.MessageId, second!.BasicProperties.MessageId);
            Assert.True(sent);
        }
        finally
        {
            await schema.CleanupAsync(runId);
        }
    }

    [Fact]
    [Trait("Category", "CorrectnessTests")]
    public async Task DuplicateDeliveryAcrossInstances_AppliesInventoryEffectOnce()
    {
        var schema = new TestMessagingSchema();
        var runId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await schema.InitializeAsync();
        try
        {
            await using (var setup = schema.CreateConnection())
            {
                await setup.OpenAsync();
                await setup.ExecuteAsync(
                    "INSERT INTO TestInventoryLot(runId,id,expiresAt,receivedAt,available) VALUES (@runId,@lotId,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),10);",
                    new { runId, lotId });
            }

            var results = await Task.WhenAll(
                ReliableMessagingReference.ApplyOnceAsync(schema, runId, "inventory", tenantId, shopId, messageId, lotId, 4),
                ReliableMessagingReference.ApplyOnceAsync(schema, runId, "inventory", tenantId, shopId, messageId, lotId, 4));

            await using var verification = schema.CreateConnection();
            await verification.OpenAsync();
            var available = await verification.ExecuteScalarAsync<int>(
                "SELECT available FROM TestInventoryLot WHERE runId=@runId AND id=@lotId;", new { runId, lotId });
            Assert.Equal(1, results.Count(x => x));
            Assert.Equal(6, available);
        }
        finally
        {
            await schema.CleanupAsync(runId);
        }
    }

    [Fact]
    [Trait("Category", "CorrectnessTests")]
    public async Task ConcurrentMultiLotReservations_PreserveStrictFefoAndNonNegativeStock()
    {
        var schema = new TestMessagingSchema();
        var runId = Guid.NewGuid();
        var earliestLotId = Guid.NewGuid();
        var laterLotId = Guid.NewGuid();
        await schema.InitializeAsync();
        try
        {
            await using (var setup = schema.CreateConnection())
            {
                await setup.OpenAsync();
                await setup.ExecuteAsync(@"
                    INSERT INTO TestInventoryLot(runId,id,expiresAt,receivedAt,available) VALUES
                    (@runId,@earliestLotId,'2026-09-01','2026-08-01',10),
                    (@runId,@laterLotId,'2026-09-10','2026-08-02',10);",
                    new { runId, earliestLotId, laterLotId });
            }

            await Task.WhenAll(
                ReliableMessagingReference.AllocateStrictFefoAsync(schema, runId, Guid.NewGuid(), 7),
                ReliableMessagingReference.AllocateStrictFefoAsync(schema, runId, Guid.NewGuid(), 7));

            await using var verification = schema.CreateConnection();
            await verification.OpenAsync();
            var lots = (await verification.QueryAsync<(Guid Id, int Available)>(
                "SELECT id,available FROM TestInventoryLot WHERE runId=@runId ORDER BY expiresAt,receivedAt,id;",
                new { runId })).ToList();
            var allocated = await verification.ExecuteScalarAsync<int>(
                "SELECT SUM(quantity) FROM TestAllocation WHERE runId=@runId;", new { runId });

            Assert.Equal(0, lots[0].Available);
            Assert.Equal(6, lots[1].Available);
            Assert.Equal(14, allocated);
            Assert.All(lots, lot => Assert.True(lot.Available >= 0));
        }
        finally
        {
            await schema.CleanupAsync(runId);
        }
    }

    [Fact]
    [Trait("Category", "CorrectnessTests")]
    public async Task OlderAggregateVersion_DoesNotOverwriteNewerState()
    {
        var schema = new TestMessagingSchema();
        var runId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        await schema.InitializeAsync();
        try
        {
            await using var connection = schema.CreateConnection();
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "INSERT INTO TestAggregateVersion(runId,aggregateId,version,value) VALUES (@runId,@aggregateId,0,'initial');",
                new { runId, aggregateId });
            const string apply = @"UPDATE TestAggregateVersion SET version=@version,value=@value
                WHERE runId=@runId AND aggregateId=@aggregateId AND version < @version;";
            await connection.ExecuteAsync(apply, new { runId, aggregateId, version = 2, value = "confirmed" });
            var staleChanged = await connection.ExecuteAsync(apply, new { runId, aggregateId, version = 1, value = "created" });
            var state = await connection.QuerySingleAsync<(int Version, string Value)>(
                "SELECT version,value FROM TestAggregateVersion WHERE runId=@runId AND aggregateId=@aggregateId;",
                new { runId, aggregateId });

            Assert.Equal(0, staleChanged);
            Assert.Equal(2, state.Version);
            Assert.Equal("confirmed", state.Value);
        }
        finally
        {
            await schema.CleanupAsync(runId);
        }
    }
}
