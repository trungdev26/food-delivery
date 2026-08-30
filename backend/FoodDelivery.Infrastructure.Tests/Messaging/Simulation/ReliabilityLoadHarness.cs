using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dapper;
using FoodDelivery.Infrastructure.Tests.Messaging.Integration;
using MySqlConnector;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Simulation;

internal sealed record ReliabilityLoadOptions(
    int LogicalMessages,
    int Shops,
    int Publishers,
    int Consumers,
    int DuplicatePercent,
    int TransientFailurePercent,
    int PostCommitCrashPercent,
    int RetryDelayMilliseconds)
{
    internal static ReliabilityLoadOptions FromEnvironment()
    {
        var options = new ReliabilityLoadOptions(
            Read("RABBIT_RELIABILITY_MESSAGES", 100_000),
            Read("RABBIT_RELIABILITY_SHOPS", 100),
            Read("RABBIT_RELIABILITY_PUBLISHERS", 3),
            Read("RABBIT_RELIABILITY_CONSUMERS", 32),
            ReadPercent("RABBIT_RELIABILITY_DUPLICATE_PERCENT", 5),
            ReadPercent("RABBIT_RELIABILITY_TRANSIENT_PERCENT", 2),
            ReadPercent("RABBIT_RELIABILITY_POST_COMMIT_CRASH_PERCENT", 1),
            Read("RABBIT_RELIABILITY_RETRY_DELAY_MS", 50));
        if (options.Shops < 2) throw new InvalidOperationException("RABBIT_RELIABILITY_SHOPS must be at least 2.");
        if (100 % options.TransientFailurePercent != 0 || 100 % options.PostCommitCrashPercent != 0)
            throw new InvalidOperationException("Failure percentages must divide 100 for deterministic selection.");
        return options;
    }

    private static int Read(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!int.TryParse(raw, out var value) || value < 1)
            throw new InvalidOperationException($"{name} must be a positive integer.");
        return value;
    }

    private static int ReadPercent(string name, int fallback)
    {
        var value = Read(name, fallback);
        if (value > 100) throw new InvalidOperationException($"{name} must be between 1 and 100.");
        return value;
    }
}

internal sealed record ReliabilityLoadResult(
    int LogicalMessages,
    int PhysicalPublished,
    int PublishedDuplicates,
    int InboxRows,
    int BusinessEffects,
    int DuplicateBusinessEffects,
    int MissingLogicalMessages,
    int TransientFailures,
    int PostCommitCrashes,
    int Redeliveries,
    int DatabaseRetries,
    long RejectedDeaths,
    long ExpiredDeaths,
    uint MainReady,
    uint MainUnacked,
    uint RetryReady,
    uint DeadReady,
    double PublishSeconds,
    double ConsumeSeconds)
{
    internal string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

internal sealed class ReliabilityLoadHarness : IAsyncDisposable
{
    private const string WorkKey = "work";
    private const string RetryKey = "retry";
    private const string ConsumerName = "inventory-reservation";
    private readonly ReliabilityLoadOptions _options;
    private readonly TestMessagingSchema _schema = new();
    private readonly Guid _runId = Guid.NewGuid();
    private readonly string _prefix = $"reliability.load.{Guid.NewGuid():N}";
    private readonly ConcurrentDictionary<int, byte> _transientFailures = new();
    private readonly ConcurrentDictionary<int, byte> _postCommitCrashes = new();
    private readonly List<(IConnection Connection, IChannel Channel)> _consumers = new();
    private int _redeliveries;
    private int _databaseRetries;
    private long _rejectedDeaths;
    private long _expiredDeaths;
    private IConnection? _setupConnection;
    private IChannel? _setupChannel;

    internal ReliabilityLoadHarness(ReliabilityLoadOptions options) => _options = options;

    private string MainExchange => $"{_prefix}.main.exchange";
    private string RetryExchange => $"{_prefix}.retry.exchange";
    private string DeadExchange => $"{_prefix}.dead.exchange";
    private string MainQueue => $"{_prefix}.main";
    private string RetryQueue => $"{_prefix}.retry";
    private string DeadQueue => $"{_prefix}.dead";

    internal async Task<ReliabilityLoadResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var duplicateCount = _options.LogicalMessages * _options.DuplicatePercent / 100;
        var publishWatch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, _options.Publishers)
            .Select(index => PublishPartitionAsync(index, duplicateCount, cancellationToken)));
        publishWatch.Stop();

        var consumeWatch = Stopwatch.StartNew();
        await StartConsumersAsync(cancellationToken);
        await WaitUntilSettledAsync(cancellationToken);
        consumeWatch.Stop();

        var database = await ReadDatabaseCountsAsync();
        var main = await ReadQueueAsync(MainQueue);
        var retry = await ReadQueueAsync(RetryQueue);
        var dead = await ReadQueueAsync(DeadQueue);
        return new ReliabilityLoadResult(
            _options.LogicalMessages,
            _options.LogicalMessages + duplicateCount,
            duplicateCount,
            database.InboxRows,
            database.BusinessEffects,
            database.BusinessEffects - database.InboxRows,
            _options.LogicalMessages - database.InboxRows,
            _transientFailures.Count,
            _postCommitCrashes.Count,
            _redeliveries,
            _databaseRetries,
            _rejectedDeaths,
            _expiredDeaths,
            main.Ready,
            main.Unacked,
            retry.Ready,
            dead.Ready,
            publishWatch.Elapsed.TotalSeconds,
            consumeWatch.Elapsed.TotalSeconds);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _schema.InitializeAsync();
        await using (var database = _schema.CreateConnection())
        {
            await database.OpenAsync(cancellationToken);
            await database.ExecuteAsync(@"
                INSERT INTO TestReliabilityEffectV2(runId,shopId,bucketId,applied)
                SELECT @runId, shops.n, tens.n*10+ones.n, 0
                FROM (
                    SELECT a.n+b.n*10 n
                    FROM (SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
                          UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) a
                    CROSS JOIN (SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
                                UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) b
                ) shops
                CROSS JOIN (SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
                            UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) tens
                CROSS JOIN (SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4
                            UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) ones
                WHERE shops.n < @shops;", new { runId = _runId, shops = _options.Shops });
        }

        _setupConnection = await RabbitMqEnvironment.ConnectAsync($"reliability-setup-{_runId:N}");
        _setupChannel = await _setupConnection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
        var channel = _setupChannel;
        await channel.ExchangeDeclareAsync(MainExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(RetryExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(MainQueue, true, false, false, new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-dead-letter-exchange"] = RetryExchange,
            ["x-dead-letter-routing-key"] = RetryKey
        }, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(RetryQueue, true, false, false, new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-message-ttl"] = _options.RetryDelayMilliseconds,
            ["x-dead-letter-exchange"] = MainExchange,
            ["x-dead-letter-routing-key"] = WorkKey
        }, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(DeadQueue, true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(MainQueue, MainExchange, WorkKey, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(RetryQueue, RetryExchange, RetryKey, cancellationToken: cancellationToken);
    }

    private async Task PublishPartitionAsync(int partition, int duplicateCount, CancellationToken cancellationToken)
    {
        await using var connection = await RabbitMqEnvironment.ConnectAsync($"reliability-publisher-{partition}");
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
        var body = Encoding.UTF8.GetBytes("{}");
        for (var id = partition; id < _options.LogicalMessages; id += _options.Publishers)
        {
            var properties = Properties(id);
            await channel.BasicPublishAsync(MainExchange, WorkKey, true, properties, body, cancellationToken);
            if (id < duplicateCount)
                await channel.BasicPublishAsync(MainExchange, WorkKey, true, properties, body, cancellationToken);
        }
    }

    private BasicProperties Properties(int id)
    {
        var shopId = ShopId(id);
        return new BasicProperties
        {
            MessageId = id.ToString(),
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                ["x-tenant-id"] = shopId / 10,
                ["x-shop-id"] = shopId
            }
        };
    }

    private async Task StartConsumersAsync(CancellationToken cancellationToken)
    {
        for (var index = 0; index < _options.Consumers; index++)
        {
            var connection = await RabbitMqEnvironment.ConnectAsync($"reliability-consumer-{index}");
            var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.BasicQosAsync(0, 32, false, cancellationToken);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, delivery) => await HandleAsync(channel, delivery, cancellationToken);
            await channel.BasicConsumeAsync(MainQueue, autoAck: false, consumer, cancellationToken);
            _consumers.Add((connection, channel));
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var id = int.Parse(delivery.BasicProperties.MessageId!);
        if (delivery.Redelivered) Interlocked.Increment(ref _redeliveries);
        CountDeaths(delivery.BasicProperties.Headers);

        if (MatchesPercent(id, _options.TransientFailurePercent) && _transientFailures.TryAdd(id, 0))
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: false, cancellationToken);
            return;
        }

        try
        {
            await ApplyOnceAsync(id, cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number is 1205 or 1213)
        {
            Interlocked.Increment(ref _databaseRetries);
            await channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: true, cancellationToken);
            return;
        }

        if (MatchesCrashPercent(id) && _postCommitCrashes.TryAdd(id, 0))
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: true, cancellationToken);
            return;
        }
        await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
    }

    private async Task ApplyOnceAsync(int messageId, CancellationToken cancellationToken)
    {
        var shopId = ShopId(messageId);
        await using var connection = _schema.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var inserted = await connection.ExecuteAsync(@"
            INSERT IGNORE INTO TestReliabilityInbox
                (runId,consumerName,tenantId,shopId,messageId,processedAt)
            VALUES (@runId,@consumerName,@tenantId,@shopId,@messageId,UTC_TIMESTAMP(6));",
            new
            {
                runId = _runId,
                consumerName = ConsumerName,
                tenantId = shopId / 10,
                shopId,
                messageId
            }, transaction);
        if (inserted == 1)
        {
            await connection.ExecuteAsync(@"
                UPDATE TestReliabilityEffectV2 SET applied=applied+1
                WHERE runId=@runId AND shopId=@shopId AND bucketId=@bucketId;",
                new { runId = _runId, shopId, bucketId = messageId % 100 }, transaction);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task WaitUntilSettledAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(10);
        var stableChecks = 0;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250, cancellationToken);
            var counts = await ReadDatabaseCountsAsync();
            var main = await ReadQueueAsync(MainQueue);
            var retry = await ReadQueueAsync(RetryQueue);
            if (counts.InboxRows == _options.LogicalMessages && main.Ready == 0 && main.Unacked == 0 && retry.Ready == 0)
            {
                if (++stableChecks == 5) return;
            }
            else stableChecks = 0;
        }
        throw new TimeoutException("Reliability load did not settle within ten minutes.");
    }

    private async Task<(int InboxRows, int BusinessEffects)> ReadDatabaseCountsAsync()
    {
        await using var connection = _schema.CreateConnection();
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<(int InboxRows, int BusinessEffects)>(@"
            SELECT
              (SELECT COUNT(*) FROM TestReliabilityInbox WHERE runId=@runId) InboxRows,
              (SELECT COALESCE(SUM(applied),0) FROM TestReliabilityEffectV2 WHERE runId=@runId) BusinessEffects;",
            new { runId = _runId });
    }

    private static async Task<(uint Ready, uint Unacked)> ReadQueueAsync(string queue)
    {
        using var client = RabbitMqEnvironment.CreateManagementClient();
        using var response = await client.GetAsync($"api/queues/food/{Uri.EscapeDataString(queue)}");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            Metric(json.RootElement, "messages_ready"),
            Metric(json.RootElement, "messages_unacknowledged"));
    }

    private static uint Metric(JsonElement queue, string name) =>
        queue.TryGetProperty(name, out var value) ? value.GetUInt32() : 0;

    private void CountDeaths(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-death", out var raw) || raw is not IList<object?> deaths) return;
        foreach (var item in deaths)
        {
            if (item is not IDictionary<string, object?> death || !death.TryGetValue("reason", out var reasonRaw) ||
                !death.TryGetValue("count", out var countRaw)) continue;
            var reason = reasonRaw is byte[] bytes ? Encoding.UTF8.GetString(bytes) : reasonRaw?.ToString();
            if (reason == "rejected") Interlocked.Add(ref _rejectedDeaths, Convert.ToInt64(countRaw));
            if (reason == "expired") Interlocked.Add(ref _expiredDeaths, Convert.ToInt64(countRaw));
        }
    }

    private int ShopId(int id) => id < _options.LogicalMessages / 2 ? 0 : 1 + id % (_options.Shops - 1);
    private static bool MatchesPercent(int id, int percent) => id % (100 / percent) == 0;
    private bool MatchesCrashPercent(int id) => id % (100 / _options.PostCommitCrashPercent) == 1;

    public async ValueTask DisposeAsync()
    {
        foreach (var consumer in _consumers)
        {
            await consumer.Channel.DisposeAsync();
            await consumer.Connection.DisposeAsync();
        }
        if (_setupChannel is not null)
        {
            foreach (var queue in new[] { MainQueue, RetryQueue, DeadQueue })
                try { await _setupChannel.QueueDeleteAsync(queue); } catch { }
            foreach (var exchange in new[] { MainExchange, RetryExchange, DeadExchange })
                try { await _setupChannel.ExchangeDeleteAsync(exchange); } catch { }
            await _setupChannel.DisposeAsync();
        }
        if (_setupConnection is not null) await _setupConnection.DisposeAsync();
        await _schema.CleanupAsync(_runId);
    }
}
