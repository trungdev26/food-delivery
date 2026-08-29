using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dapper;
using FoodDelivery.Infrastructure.Tests.Messaging.Integration;
using FoodDelivery.Infrastructure.Tests.Messaging.Simulation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;
using Xunit.Abstractions;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Load;

public sealed class LargeScaleLoadTests
{
    private readonly ITestOutputHelper _output;

    public LargeScaleLoadTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "LargeLoad")]
    public async Task OneHundredShops_OneHundredThousandStockCardsAndMessages_RemainComplete()
    {
        var shops = ReadPositive("RABBIT_LOAD_SHOPS", 100);
        var stockCards = ReadPositive("RABBIT_LOAD_STOCK_CARDS", 100_000);
        var messages = ReadPositive("RABBIT_LOAD_MESSAGE_COUNT", 100_000);
        var publishers = ReadPositive("RABBIT_LOAD_PUBLISHERS", 3);
        var consumers = ReadPositive("RABBIT_LOAD_CONSUMERS", 8);
        var schema = new TestMessagingSchema();
        var runId = Guid.NewGuid();
        var queueName = $"load.large.{runId:N}";
        var publishElapsed = TimeSpan.Zero;
        var consumeElapsed = TimeSpan.Zero;
        await schema.InitializeAsync();

        try
        {
            var seedWatch = Stopwatch.StartNew();
            await SeedStockCardsAsync(schema, runId, shops, stockCards);
            seedWatch.Stop();

            await using var setupConnection = await RabbitMqEnvironment.ConnectAsync();
            await using var setupChannel = await setupConnection.CreateChannelAsync();
            await setupChannel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });

            var publishWatch = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, publishers)
                .Select(index => PublishPartitionAsync(queueName, index, publishers, messages)));
            publishWatch.Stop();
            publishElapsed = publishWatch.Elapsed;

            var queued = await setupChannel.QueueDeclarePassiveAsync(queueName);
            Assert.Equal((uint)messages, queued.MessageCount);

            var consumeWatch = Stopwatch.StartNew();
            var consumed = await ConsumeAsync(queueName, messages, consumers);
            consumeWatch.Stop();
            consumeElapsed = consumeWatch.Elapsed;

            await using var database = schema.CreateConnection();
            await database.OpenAsync();
            var counts = await database.QuerySingleAsync<(long Cards, int Shops)>(@"
                SELECT COUNT(*) Cards, COUNT(DISTINCT shopId) Shops
                FROM TestStockCardLoad WHERE runId=@runId;", new { runId });
            var hotKeyLots = await database.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM TestStockCardLoad
                WHERE runId=@runId AND shopId=0 AND productId=0;", new { runId });
            var explain = await database.QueryAsync<string>(@"
                EXPLAIN ANALYZE SELECT id FROM TestStockCardLoad
                WHERE runId=@runId AND shopId=0 AND productId=0 AND available>0
                ORDER BY expiresAt,id LIMIT 10;", new { runId });

            Assert.Equal(stockCards, counts.Cards);
            Assert.Equal(shops, counts.Shops);
            Assert.True(hotKeyLots >= 100, $"Hot FEFO key has only {hotKeyLots} lots.");
            Assert.Equal(messages, consumed);
            var empty = await setupChannel.QueueDeclarePassiveAsync(queueName);
            Assert.Equal(0u, empty.MessageCount);

            _output.WriteLine(JsonSerializer.Serialize(new
            {
                shops,
                stockCards,
                messages,
                publishers,
                consumers,
                prefetchPerConsumer = 32,
                maximumUnacked = consumers * 32,
                readyBeforeConsume = queued.MessageCount,
                hotKeyLots,
                seedSeconds = seedWatch.Elapsed.TotalSeconds,
                publishSeconds = publishElapsed.TotalSeconds,
                publishPerSecond = messages / publishElapsed.TotalSeconds,
                consumeSeconds = consumeElapsed.TotalSeconds,
                consumePerSecond = messages / consumeElapsed.TotalSeconds,
                explain = explain.ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            await using var cleanupConnection = await RabbitMqEnvironment.ConnectAsync();
            await using var cleanupChannel = await cleanupConnection.CreateChannelAsync();
            await cleanupChannel.QueueDeleteAsync(queueName);
            await schema.CleanupAsync(runId);
        }
    }

    private static async Task SeedStockCardsAsync(TestMessagingSchema schema, Guid runId, int shops, int count)
    {
        await using var connection = schema.CreateConnection();
        await connection.OpenAsync();
        const string digits = "(SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9)";
        var sql = $@"
            INSERT INTO TestStockCardLoad(runId,id,shopId,productId,expiresAt,available)
            SELECT @runId, seq,
                   CASE WHEN @shops=1 THEN 0
                        WHEN seq < FLOOR(@count/2) THEN 0
                        ELSE 1 + MOD(seq,@shops-1) END,
                   MOD(FLOOR(seq / GREATEST(@shops,1)),100),
                   DATE_ADD('2026-09-01', INTERVAL MOD(seq,365) DAY), 100
            FROM (
                SELECT a.n + b.n*10 + c.n*100 + d.n*1000 + e.n*10000 + f.n*100000 seq
                FROM {digits} a CROSS JOIN {digits} b CROSS JOIN {digits} c
                CROSS JOIN {digits} d CROSS JOIN {digits} e CROSS JOIN {digits} f
            ) numbers WHERE seq < @count;";
        await connection.ExecuteAsync(sql, new { runId, shops, count }, commandTimeout: 180);
    }

    private static async Task PublishPartitionAsync(string queueName, int partition, int partitions, int count)
    {
        await using var connection = await RabbitMqEnvironment.ConnectAsync($"load-publisher-{partition}");
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        var body = Encoding.UTF8.GetBytes("{\"eventName\":\"StockCardChanged\"}");
        var properties = new BasicProperties { DeliveryMode = DeliveryModes.Persistent, ContentType = "application/json" };
        for (var i = partition; i < count; i += partitions)
        {
            properties.MessageId = i.ToString();
            await channel.BasicPublishAsync(string.Empty, queueName, mandatory: true, properties, body);
        }
    }

    private static async Task<int> ConsumeAsync(string queueName, int expected, int consumerCount)
    {
        var consumed = 0;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resources = new List<(IConnection Connection, IChannel Channel)>();
        try
        {
            for (var i = 0; i < consumerCount; i++)
            {
                var connection = await RabbitMqEnvironment.ConnectAsync($"load-consumer-{i}");
                var channel = await connection.CreateChannelAsync();
                await channel.BasicQosAsync(0, 32, false);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    await channel.BasicAckAsync(delivery.DeliveryTag, false);
                    if (Interlocked.Increment(ref consumed) == expected) done.TrySetResult(true);
                };
                await channel.BasicConsumeAsync(queueName, autoAck: false, consumer);
                resources.Add((connection, channel));
            }

            await done.Task.WaitAsync(TimeSpan.FromMinutes(5));
            return consumed;
        }
        finally
        {
            foreach (var resource in resources)
            {
                await resource.Channel.DisposeAsync();
                await resource.Connection.DisposeAsync();
            }
        }
    }

    private static int ReadPositive(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!int.TryParse(raw, out var value) || value < 1)
            throw new InvalidOperationException($"{name} must be a positive integer.");
        return value;
    }
}
