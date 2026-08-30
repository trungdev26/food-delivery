using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dapper;
using FoodDelivery.Infrastructure.Tests.Messaging.Integration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Simulation;

internal sealed record InventoryRecalculationCoalescingResult(
    int MessagesAcked,
    uint QueueReady,
    uint QueueUnacked,
    int RequestedVersion,
    int CompletedVersion,
    int MinimumCardVersion,
    int MaximumCardVersion,
    long ActualCardCount,
    int RowsUpdatedBeforeInjectedMessage,
    int FirstPassVersion,
    int Recalculations,
    long BaselineRowVisits,
    long CoalescedRowUpdates,
    double InitialBurstAckMilliseconds,
    double LastMessageAckMilliseconds,
    double TotalMilliseconds)
{
    internal string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

internal sealed class InventoryRecalculationCoalescingHarness : IAsyncDisposable
{
    private readonly int _stockCards;
    private readonly int _initialMessages;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _maxWait;
    private readonly TestMessagingSchema _schema = new();
    private readonly Guid _runId = Guid.NewGuid();
    private readonly string _queue = $"inventory.coalescing.{Guid.NewGuid():N}";
    private readonly TaskCompletionSource _firstPassStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _lastMessageAcked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IConnection? _connection;
    private IChannel? _channel;
    private IChannel? _publisherChannel;
    private int _acked;
    private int _recalculations;
    private int _firstPassVersion;
    private int _rowsUpdatedBeforeInjectedMessage;
    private long _coalescedRowUpdates;

    internal InventoryRecalculationCoalescingHarness(
        int stockCards,
        int initialMessages,
        TimeSpan debounce,
        TimeSpan maxWait)
    {
        _stockCards = stockCards;
        _initialMessages = initialMessages;
        _debounce = debounce;
        _maxWait = maxWait;
    }

    internal async Task<InventoryRecalculationCoalescingResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await StartConsumerAsync(cancellationToken);
        var totalWatch = Stopwatch.StartNew();
        var worker = RunWorkerAsync(cancellationToken);

        var burstWatch = Stopwatch.StartNew();
        for (var version = 1; version <= _initialMessages; version++)
            await PublishAsync(version, cancellationToken);
        await WaitForAckCountAsync(_initialMessages, cancellationToken);
        burstWatch.Stop();

        var startedOrFailed = await Task.WhenAny(
            _firstPassStarted.Task,
            worker,
            Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));
        if (startedOrFailed == worker) await worker;
        if (startedOrFailed != _firstPassStarted.Task)
            throw new TimeoutException("The first recalculation pass did not start within 30 seconds.");
        var lastMessageWatch = Stopwatch.StartNew();
        await PublishAsync(_initialMessages + 1, cancellationToken);
        await _lastMessageAcked.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        lastMessageWatch.Stop();
        await worker.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
        totalWatch.Stop();

        var state = await ReadStateAsync();
        var queue = await ReadQueueAsync();
        return new InventoryRecalculationCoalescingResult(
            _acked,
            queue.Ready,
            queue.Unacked,
            state.RequestedVersion,
            state.CompletedVersion,
            state.MinimumCardVersion,
            state.MaximumCardVersion,
            state.ActualCardCount,
            _rowsUpdatedBeforeInjectedMessage,
            _firstPassVersion,
            _recalculations,
            (long)(_initialMessages + 1) * _stockCards,
            _coalescedRowUpdates,
            burstWatch.Elapsed.TotalMilliseconds,
            lastMessageWatch.Elapsed.TotalMilliseconds,
            totalWatch.Elapsed.TotalMilliseconds);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _schema.InitializeAsync();
        await using (var database = _schema.CreateConnection())
        {
            await database.OpenAsync(cancellationToken);
            await database.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS TestInventoryRecalculation (
                    runId CHAR(36) NOT NULL PRIMARY KEY,
                    requestedVersion INT NOT NULL,
                    completedVersion INT NOT NULL,
                    firstRequestedAt DATETIME(6) NOT NULL,
                    lastRequestedAt DATETIME(6) NOT NULL
                );
                CREATE TABLE IF NOT EXISTS TestInventoryRecalculationCard (
                    runId CHAR(36) NOT NULL,
                    cardId INT NOT NULL,
                    calculatedVersion INT NOT NULL,
                    PRIMARY KEY (runId, cardId)
                );
                INSERT INTO TestInventoryRecalculationCard(runId,cardId,calculatedVersion)
                SELECT @runId, a.n+b.n*10+c.n*100+d.n*1000+e.n*10000, 0
                FROM (SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) a
                CROSS JOIN (SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) b
                CROSS JOIN (SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) c
                CROSS JOIN (SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) d
                CROSS JOIN (SELECT 0 n UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) e
                WHERE a.n+b.n*10+c.n*100+d.n*1000+e.n*10000 < @stockCards;",
                new { runId = _runId, stockCards = _stockCards });
        }

        _connection = await RabbitMqEnvironment.ConnectAsync($"inventory-coalescing-{_runId:N}");
        _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
        _publisherChannel = await _connection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
        await _channel.QueueDeclareAsync(_queue, durable: true, exclusive: false, autoDelete: false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: cancellationToken);
    }

    private async Task StartConsumerAsync(CancellationToken cancellationToken)
    {
        await _channel!.BasicQosAsync(0, 32, false, cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var version = int.Parse(Encoding.UTF8.GetString(delivery.Body.Span));
            await using var database = _schema.CreateConnection();
            await database.OpenAsync(cancellationToken);
            await database.ExecuteAsync(@"
                INSERT INTO TestInventoryRecalculation
                    (runId,requestedVersion,completedVersion,firstRequestedAt,lastRequestedAt)
                VALUES (@runId,@version,0,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    requestedVersion=GREATEST(requestedVersion,VALUES(requestedVersion)),
                    lastRequestedAt=UTC_TIMESTAMP(6);",
                new { runId = _runId, version });
            await _channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
            var count = Interlocked.Increment(ref _acked);
            if (count == _initialMessages + 1) _lastMessageAcked.TrySetResult();
        };
        await _channel.BasicConsumeAsync(_queue, autoAck: false, consumer, cancellationToken);
    }

    private Task PublishAsync(int version, CancellationToken cancellationToken) =>
        _publisherChannel!.BasicPublishAsync(
            string.Empty,
            _queue,
            mandatory: true,
            new BasicProperties { MessageId = version.ToString(), DeliveryMode = DeliveryModes.Persistent },
            Encoding.UTF8.GetBytes(version.ToString()),
            cancellationToken).AsTask();

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var database = _schema.CreateConnection();
            await database.OpenAsync(cancellationToken);
            var state = await database.QuerySingleOrDefaultAsync<PendingState>(@"
                SELECT requestedVersion,completedVersion,
                       CEILING(GREATEST(0,LEAST(
                           TIMESTAMPDIFF(MICROSECOND,UTC_TIMESTAMP(6),TIMESTAMPADD(MICROSECOND,@debounceMicroseconds,lastRequestedAt)),
                           TIMESTAMPDIFF(MICROSECOND,UTC_TIMESTAMP(6),TIMESTAMPADD(MICROSECOND,@maxWaitMicroseconds,firstRequestedAt))
                       ))/1000) remainingMilliseconds
                FROM TestInventoryRecalculation WHERE runId=@runId;",
                new
                {
                    runId = _runId,
                    debounceMicroseconds = (long)(_debounce.TotalMilliseconds * 1000),
                    maxWaitMicroseconds = (long)(_maxWait.TotalMilliseconds * 1000)
                });
            if (state is null || state.RequestedVersion == state.CompletedVersion)
            {
                if (state?.CompletedVersion == _initialMessages + 1) return;
                await Task.Delay(10, cancellationToken);
                continue;
            }

            if (state.RemainingMilliseconds > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(decimal.ToDouble(state.RemainingMilliseconds)), cancellationToken);
                continue;
            }

            var version = state.RequestedVersion;
            var pass = Interlocked.Increment(ref _recalculations);
            if (pass == 1) _firstPassVersion = version;
            const int batchSize = 5_000;
            for (var firstCardId = 0; firstCardId < _stockCards; firstCardId += batchSize)
            {
                var updated = await database.ExecuteAsync(@"
                    UPDATE TestInventoryRecalculationCard SET calculatedVersion=@version
                    WHERE runId=@runId AND cardId>=@firstCardId AND cardId<@firstCardId+@batchSize;",
                    new { runId = _runId, version, firstCardId, batchSize });
                _coalescedRowUpdates += updated;
                if (pass == 1 && firstCardId == 0)
                {
                    _rowsUpdatedBeforeInjectedMessage = updated;
                    _firstPassStarted.TrySetResult();
                    await _lastMessageAcked.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            await database.ExecuteAsync(@"
                UPDATE TestInventoryRecalculation
                SET completedVersion=@version WHERE runId=@runId;",
                new { runId = _runId, version });
        }
    }

    private async Task WaitForAckCountAsync(int expected, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref _acked) < expected && DateTime.UtcNow < deadline)
            await Task.Delay(10, cancellationToken);
        if (_acked < expected) throw new TimeoutException($"Only {_acked}/{expected} messages were ACKed.");
    }

    private async Task<FinalState> ReadStateAsync()
    {
        await using var database = _schema.CreateConnection();
        await database.OpenAsync();
        return await database.QuerySingleAsync<FinalState>(@"
            SELECT r.requestedVersion,r.completedVersion,
                   MIN(c.calculatedVersion) minimumCardVersion,
                   MAX(c.calculatedVersion) maximumCardVersion,
                   COUNT(*) actualCardCount
            FROM TestInventoryRecalculation r
            JOIN TestInventoryRecalculationCard c ON c.runId=r.runId
            WHERE r.runId=@runId GROUP BY r.runId,r.requestedVersion,r.completedVersion;",
            new { runId = _runId });
    }

    private async Task<(uint Ready, uint Unacked)> ReadQueueAsync()
    {
        using var client = RabbitMqEnvironment.CreateManagementClient();
        using var response = await client.GetAsync($"api/queues/food/{Uri.EscapeDataString(_queue)}");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (Metric(json.RootElement, "messages_ready"), Metric(json.RootElement, "messages_unacknowledged"));
    }

    private static uint Metric(JsonElement queue, string name) =>
        queue.TryGetProperty(name, out var value) ? value.GetUInt32() : 0;

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            try { await _channel.QueueDeleteAsync(_queue); } catch { }
            await _channel.DisposeAsync();
        }
        if (_publisherChannel is not null) await _publisherChannel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await using var database = _schema.CreateConnection();
        await database.OpenAsync();
        await database.ExecuteAsync(@"
            DELETE FROM TestInventoryRecalculationCard WHERE runId=@runId;
            DELETE FROM TestInventoryRecalculation WHERE runId=@runId;", new { runId = _runId });
    }

    private sealed record PendingState(
        int RequestedVersion,
        int CompletedVersion,
        decimal RemainingMilliseconds);

    private sealed record FinalState(
        int RequestedVersion,
        int CompletedVersion,
        int MinimumCardVersion,
        int MaximumCardVersion,
        long ActualCardCount);
}
