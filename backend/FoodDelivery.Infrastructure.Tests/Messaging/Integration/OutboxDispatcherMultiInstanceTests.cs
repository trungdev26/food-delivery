using Dapper;
using FoodDelivery.Infrastructure.Tests.Messaging.Simulation;
using RabbitMQ.Client;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class OutboxDispatcherMultiInstanceTests
{
    [Fact]
    [Trait("Category", "CorrectnessTests")]
    public async Task FourInstances_ClaimDisjointBatchesAndPublishEveryIntent()
    {
        var context = await OutboxTestContext.CreateAsync(messageCount: 100);
        await using (context)
        {
            var dispatchers = Enumerable.Range(1, 4)
                .Select(index => new TestOutboxDispatcher(context.Schema, context.RunId, context.QueueName, $"worker-{index}", 25))
                .ToArray();
            try
            {
                var results = await Task.WhenAll(dispatchers.Select(x => x.DispatchOnceAsync()));

                Assert.True(
                    results.Sum(x => x.Claimed) == 100,
                    $"claims=[{string.Join(',', results.Select(x => x.Claimed))}]");
                Assert.Equal(100, results.Sum(x => x.MarkedSent));
                Assert.Equal(100, await context.CountSentAsync());
                var ids = await context.DrainMessageIdsAsync();
                Assert.Equal(100, ids.Count);
                Assert.Equal(100, ids.Distinct().Count());
            }
            finally
            {
                foreach (var dispatcher in dispatchers) await dispatcher.DisposeAsync();
            }
        }
    }

    [Fact]
    [Trait("Category", "CorrectnessTests")]
    public async Task WorkerDiesBeforePublish_ExpiredLeaseLetsAnotherInstanceRecover()
    {
        var context = await OutboxTestContext.CreateAsync(messageCount: 1);
        await using (context)
        await using (var crashed = new TestOutboxDispatcher(context.Schema, context.RunId, context.QueueName, "crashed", 1, leaseSeconds: 1))
        await using (var recovered = new TestOutboxDispatcher(context.Schema, context.RunId, context.QueueName, "recovered", 1, leaseSeconds: 1))
        {
            var first = await crashed.DispatchOnceAsync(OutboxFailureCheckpoint.BeforePublish);
            Assert.Equal(1, first.Claimed);
            Assert.Equal(0, first.Published);

            var second = await DispatchAfterLeaseExpiresAsync(recovered);

            Assert.Equal(1, second.MarkedSent);
            Assert.Equal(1, await context.CountSentAsync());
            Assert.Single(await context.DrainMessageIdsAsync());
            var state = await context.ReadStateAsync();
            Assert.Equal(2, state.Attempts);
            Assert.Null(state.LockedBy);
            Assert.Null(state.LockedUntil);
        }
    }

    [Fact]
    [Trait("Category", "CorrectnessTests")]
    public async Task WorkerDiesAfterConfirm_DuplicatePublishUsesSameMessageId()
    {
        var context = await OutboxTestContext.CreateAsync(messageCount: 1);
        await using (context)
        await using (var crashed = new TestOutboxDispatcher(context.Schema, context.RunId, context.QueueName, "crashed", 1, leaseSeconds: 1))
        await using (var recovered = new TestOutboxDispatcher(context.Schema, context.RunId, context.QueueName, "recovered", 1, leaseSeconds: 1))
        {
            var first = await crashed.DispatchOnceAsync(OutboxFailureCheckpoint.AfterConfirmBeforeMarkSent);
            Assert.Equal(1, first.Published);
            Assert.Equal(0, first.MarkedSent);

            var second = await DispatchAfterLeaseExpiresAsync(recovered);

            Assert.Equal(1, second.MarkedSent);
            var ids = await context.DrainMessageIdsAsync();
            Assert.Equal(2, ids.Count);
            Assert.Single(ids.Distinct());
            Assert.Equal(1, await context.CountSentAsync());
            var state = await context.ReadStateAsync();
            Assert.Equal(2, state.Attempts);
            Assert.Null(state.LockedBy);
            Assert.Null(state.LockedUntil);
        }
    }

    private static async Task<OutboxDispatchResult> DispatchAfterLeaseExpiresAsync(TestOutboxDispatcher dispatcher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var result = await dispatcher.DispatchOnceAsync();
            if (result.Claimed > 0) return result;
            await Task.Delay(50);
        }
        throw new TimeoutException("Expired Outbox lease was not reclaimed within five seconds.");
    }

    private sealed class OutboxTestContext : IAsyncDisposable
    {
        private readonly Guid _runId;
        private readonly IConnection _connection;
        private readonly IChannel _channel;

        private OutboxTestContext(Guid runId, TestMessagingSchema schema, string queueName, IConnection connection, IChannel channel)
        {
            _runId = runId;
            Schema = schema;
            QueueName = queueName;
            _connection = connection;
            _channel = channel;
        }

        internal TestMessagingSchema Schema { get; }
        internal Guid RunId => _runId;
        internal string QueueName { get; }

        internal static async Task<OutboxTestContext> CreateAsync(int messageCount)
        {
            var runId = Guid.NewGuid();
            var schema = new TestMessagingSchema();
            var queueName = $"outbox.tests.{runId:N}";
            await schema.InitializeAsync();
            await using (var database = schema.CreateConnection())
            {
                await database.OpenAsync();
                for (var offset = 0; offset < messageCount; offset += 100)
                {
                    var rows = Enumerable.Range(offset, Math.Min(100, messageCount - offset))
                        .Select(index => new { runId, messageId = Guid.NewGuid(), payload = $"{{\"index\":{index}}}" });
                    await database.ExecuteAsync(@"
                        INSERT INTO TestOutboxLease(runId,messageId,payload,occurredAt)
                        VALUES (@runId,@messageId,@payload,UTC_TIMESTAMP(6));", rows);
                }
            }

            var connection = await RabbitMqEnvironment.ConnectAsync($"outbox-context-{runId:N}");
            var channel = await connection.CreateChannelAsync();
            await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
            return new OutboxTestContext(runId, schema, queueName, connection, channel);
        }

        internal async Task<int> CountSentAsync()
        {
            await using var database = Schema.CreateConnection();
            await database.OpenAsync();
            return await database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM TestOutboxLease WHERE runId=@runId AND sentAt IS NOT NULL;", new { runId = _runId });
        }

        internal async Task<List<string>> DrainMessageIdsAsync()
        {
            var ids = new List<string>();
            while (true)
            {
                var delivery = await _channel.BasicGetAsync(QueueName, autoAck: true);
                if (delivery is null) return ids;
                ids.Add(delivery.BasicProperties.MessageId!);
            }
        }

        internal async Task<OutboxState> ReadStateAsync()
        {
            await using var database = Schema.CreateConnection();
            await database.OpenAsync();
            return await database.QuerySingleAsync<OutboxState>(@"
                SELECT attempts,lockedBy,lockedUntil
                FROM TestOutboxLease WHERE runId=@runId;", new { runId = _runId });
        }

        public async ValueTask DisposeAsync()
        {
            await _channel.QueueDeleteAsync(QueueName);
            await _channel.DisposeAsync();
            await _connection.DisposeAsync();
            await Schema.CleanupAsync(_runId);
        }

        internal sealed record OutboxState(int Attempts, string? LockedBy, DateTime? LockedUntil);
    }
}
