using System.Text;
using FoodDelivery.Infrastructure.Tests.Messaging.Integration;
using RabbitMQ.Client;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Simulation;

internal enum RetryDisposition
{
    Success,
    TransientFailure,
    PermanentFailure
}

internal sealed record RetryRunResult(
    int Attempts,
    long RejectedDeaths,
    long ExpiredDeaths,
    BasicGetResult? DeadLetter,
    string? FailureReason,
    uint MainReady,
    uint RetryReady,
    uint DeadReady);

internal sealed class RabbitMqRetryHarness : IAsyncDisposable
{
    private const string WorkKey = "work";
    private const string RetryKey = "retry";
    private const string DeadKey = "dead";
    private readonly int _delayMilliseconds;
    private readonly int _maxAttempts;
    private readonly string _prefix = $"retry.tests.{Guid.NewGuid():N}";
    private IConnection? _connection;
    private IChannel? _channel;

    internal RabbitMqRetryHarness(int delayMilliseconds, int maxAttempts)
    {
        if (delayMilliseconds < 1) throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        _delayMilliseconds = delayMilliseconds;
        _maxAttempts = maxAttempts;
    }

    private string MainExchange => $"{_prefix}.main.exchange";
    private string RetryExchange => $"{_prefix}.retry.exchange";
    private string DeadExchange => $"{_prefix}.dead.exchange";
    private string MainQueue => $"{_prefix}.main";
    private string RetryQueue => $"{_prefix}.retry";
    private string DeadQueue => $"{_prefix}.dead";

    internal async Task<RetryRunResult> RunAsync(
        Guid messageId,
        Func<int, RetryDisposition> handler,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var channel = _channel!;
        await channel.BasicPublishAsync(
            MainExchange,
            WorkKey,
            mandatory: true,
            new BasicProperties
            {
                MessageId = messageId.ToString(),
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent
            },
            Encoding.UTF8.GetBytes("{}"),
            cancellationToken);

        var attempts = 0;
        BasicGetResult? terminalDelivery = null;
        while (attempts < _maxAttempts)
        {
            var delivery = await WaitForMessageAsync(MainQueue, cancellationToken);
            attempts++;
            var disposition = handler(attempts);
            if (disposition == RetryDisposition.Success)
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                terminalDelivery = delivery;
                break;
            }

            if (disposition == RetryDisposition.TransientFailure && attempts < _maxAttempts)
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: false, cancellationToken);
                continue;
            }

            var reason = disposition == RetryDisposition.PermanentFailure ? "permanent" : "max-attempts";
            await PublishDeadAsync(delivery, reason, cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
            terminalDelivery = delivery;
            break;
        }

        var deadLetter = await channel.BasicGetAsync(DeadQueue, autoAck: true, cancellationToken);
        var deaths = ReadDeaths((terminalDelivery ?? deadLetter)?.BasicProperties.Headers);
        var main = await channel.QueueDeclarePassiveAsync(MainQueue, cancellationToken);
        var retry = await channel.QueueDeclarePassiveAsync(RetryQueue, cancellationToken);
        var dead = await channel.QueueDeclarePassiveAsync(DeadQueue, cancellationToken);
        return new RetryRunResult(
            attempts,
            deaths.GetValueOrDefault("rejected"),
            deaths.GetValueOrDefault("expired"),
            deadLetter,
            HeaderText(deadLetter?.BasicProperties.Headers, "x-failure-reason"),
            main.MessageCount,
            retry.MessageCount,
            dead.MessageCount);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _connection = await RabbitMqEnvironment.ConnectAsync($"retry-tests-{Guid.NewGuid():N}");
        _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
        var channel = _channel;
        await channel.ExchangeDeclareAsync(MainExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(RetryExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(MainQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = RetryExchange,
                ["x-dead-letter-routing-key"] = RetryKey
            }, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(RetryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-message-ttl"] = _delayMilliseconds,
                ["x-dead-letter-exchange"] = MainExchange,
                ["x-dead-letter-routing-key"] = WorkKey
            }, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(DeadQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(MainQueue, MainExchange, WorkKey, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(RetryQueue, RetryExchange, RetryKey, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DeadQueue, DeadExchange, DeadKey, cancellationToken: cancellationToken);
    }

    private async Task<BasicGetResult> WaitForMessageAsync(string queue, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var delivery = await _channel!.BasicGetAsync(queue, autoAck: false, cancellationToken);
            if (delivery is not null) return delivery;
            await Task.Delay(20, cancellationToken);
        }
        throw new TimeoutException($"No message arrived in {queue}.");
    }

    private Task PublishDeadAsync(BasicGetResult delivery, string reason, CancellationToken cancellationToken)
    {
        var headers = delivery.BasicProperties.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(delivery.BasicProperties.Headers);
        headers["x-failure-reason"] = reason;
        return _channel!.BasicPublishAsync(
            DeadExchange,
            DeadKey,
            mandatory: true,
            new BasicProperties
            {
                MessageId = delivery.BasicProperties.MessageId,
                ContentType = delivery.BasicProperties.ContentType,
                DeliveryMode = DeliveryModes.Persistent,
                Headers = headers
            },
            delivery.Body,
            cancellationToken).AsTask();
    }

    private static Dictionary<string, long> ReadDeaths(IDictionary<string, object?>? headers)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (headers is null || !headers.TryGetValue("x-death", out var rawDeaths) ||
            rawDeaths is not IList<object?> deaths) return result;
        foreach (var raw in deaths)
        {
            if (raw is not IDictionary<string, object?> death) continue;
            var reason = HeaderText(death, "reason");
            if (reason is null || !death.TryGetValue("count", out var count)) continue;
            result[reason] = result.GetValueOrDefault(reason) + Convert.ToInt64(count);
        }
        return result;
    }

    private static string? HeaderText(IDictionary<string, object?>? headers, string name)
    {
        if (headers is null || !headers.TryGetValue(name, out var value)) return null;
        return value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            foreach (var queue in new[] { MainQueue, RetryQueue, DeadQueue })
                try { await _channel.QueueDeleteAsync(queue); } catch { }
            foreach (var exchange in new[] { MainExchange, RetryExchange, DeadExchange })
                try { await _channel.ExchangeDeleteAsync(exchange); } catch { }
            await _channel.DisposeAsync();
        }
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
