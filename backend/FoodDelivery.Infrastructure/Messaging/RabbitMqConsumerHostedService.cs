using System.Text;
using FoodDelivery.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FoodDelivery.Infrastructure.Messaging;

public sealed class RabbitMqConsumerHostedService : BackgroundService
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyCollection<RabbitMqConsumerRegistration> _registrations;
    private readonly ILogger<RabbitMqConsumerHostedService> _logger;

    public RabbitMqConsumerHostedService(
        RabbitMqConnectionManager connectionManager,
        RabbitMqOptions options,
        IServiceScopeFactory scopeFactory,
        IEnumerable<RabbitMqConsumerRegistration> registrations,
        ILogger<RabbitMqConsumerHostedService> logger)
    {
        _connectionManager = connectionManager;
        _options = options;
        _scopeFactory = scopeFactory;
        _registrations = registrations.ToArray();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_registrations.Count == 0) return;
        await Task.WhenAll(_registrations.Select(registration => RunAsync(registration, stoppingToken)));
    }

    private async Task RunAsync(RabbitMqConsumerRegistration registration, CancellationToken stoppingToken)
    {
        Validate(registration);
        var connection = await _connectionManager.GetConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(true, true), stoppingToken);
        await DeclareTopologyAsync(channel, registration, stoppingToken);
        await channel.BasicQosAsync(0, registration.PrefetchCount, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, registration, delivery, stoppingToken);
        var consumerTag = await channel.BasicConsumeAsync(registration.QueueName, false, consumer, stoppingToken);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown. Unacked messages return to the queue when the channel closes.
        }
        finally
        {
            if (channel.IsOpen) await channel.BasicCancelAsync(consumerTag, false, CancellationToken.None);
        }
    }

    private async Task HandleAsync(
        IChannel channel,
        RabbitMqConsumerRegistration registration,
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken)
    {
        if (!TryReadMessage(delivery, out var message))
        {
            await PublishDeadAndAckAsync(channel, registration, delivery, "invalid-message-metadata", cancellationToken);
            return;
        }
        var messageId = message.MessageId;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            _logger.LogDebug("Consumer {ConsumerName} starting message {MessageId}", registration.ConsumerName, messageId);
            await using var unitOfWork = await scope.ServiceProvider
                .GetRequiredService<IUnitOfWorkFactory>().CreateAsync(cancellationToken);
            _logger.LogDebug("Consumer {ConsumerName} opened unit of work for {MessageId}", registration.ConsumerName, messageId);
            if (!await unitOfWork.TryBeginInboxMessageAsync(
                    registration.ConsumerName, message.TenantId, message.ShopId, messageId, cancellationToken))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                MessagingMetrics.Duplicates.Add(1, new KeyValuePair<string, object?>("consumer", registration.ConsumerName));
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                return;
            }

            _logger.LogDebug("Consumer {ConsumerName} acquired inbox message {MessageId}", registration.ConsumerName, messageId);
            await registration.HandleAsync(message, unitOfWork, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            MessagingMetrics.Consumed.Add(1, new KeyValuePair<string, object?>("consumer", registration.ConsumerName));
            await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave unacked; closing the channel returns the message to the queue.
        }
        catch (PermanentMessageException exception)
        {
            await PublishDeadAndAckAsync(channel, registration, delivery, exception.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Consumer {ConsumerName} failed message {MessageId}",
                registration.ConsumerName, messageId);
            if (RejectedDeaths(delivery.BasicProperties.Headers) + 1 >= registration.MaxAttempts)
                await PublishDeadAndAckAsync(channel, registration, delivery, "max-attempts", cancellationToken);
            else
            {
                MessagingMetrics.Retries.Add(1, new KeyValuePair<string, object?>("consumer", registration.ConsumerName));
                await channel.BasicNackAsync(delivery.DeliveryTag, false, false, cancellationToken);
            }
        }
    }

    private async Task DeclareTopologyAsync(
        IChannel channel, RabbitMqConsumerRegistration registration, CancellationToken cancellationToken)
    {
        var retryExchange = $"{_options.ExchangeName}.retry";
        var deadExchange = $"{_options.ExchangeName}.dead";
        var retryQueue = $"{registration.QueueName}.retry";
        var deadQueue = $"{registration.QueueName}.dead";
        await channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Direct, true, false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(retryExchange, ExchangeType.Direct, true, false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(deadExchange, ExchangeType.Direct, true, false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(registration.QueueName, true, false, false,
            new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = retryExchange,
                ["x-dead-letter-routing-key"] = registration.QueueName
            }, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(retryQueue, true, false, false,
            new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-message-ttl"] = registration.RetryDelayMilliseconds,
                ["x-dead-letter-exchange"] = _options.ExchangeName,
                ["x-dead-letter-routing-key"] = registration.RoutingKey
            }, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(deadQueue, true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(registration.QueueName, _options.ExchangeName,
            registration.RoutingKey, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(retryQueue, retryExchange,
            registration.QueueName, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(deadQueue, deadExchange,
            registration.QueueName, cancellationToken: cancellationToken);
    }

    private async Task PublishDeadAndAckAsync(
        IChannel channel,
        RabbitMqConsumerRegistration registration,
        BasicDeliverEventArgs delivery,
        string reason,
        CancellationToken cancellationToken)
    {
        var headers = delivery.BasicProperties.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(delivery.BasicProperties.Headers);
        headers["x-failure-reason"] = reason;
        await channel.BasicPublishAsync(
            $"{_options.ExchangeName}.dead",
            registration.QueueName,
            true,
            new BasicProperties
            {
                MessageId = delivery.BasicProperties.MessageId,
                Type = delivery.BasicProperties.Type,
                CorrelationId = delivery.BasicProperties.CorrelationId,
                ContentType = delivery.BasicProperties.ContentType,
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = delivery.BasicProperties.Timestamp,
                Headers = headers
            }, delivery.Body, cancellationToken);
        MessagingMetrics.DeadLettered.Add(1,
            new KeyValuePair<string, object?>("consumer", registration.ConsumerName),
            new KeyValuePair<string, object?>("reason", reason));
        await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
    }

    private static bool TryReadMessage(
        BasicDeliverEventArgs delivery, out ConsumedIntegrationMessage message)
    {
        message = null!;
        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId) ||
            string.IsNullOrWhiteSpace(delivery.BasicProperties.Type) ||
            !int.TryParse(HeaderText(delivery.BasicProperties.Headers, "x-event-version"), out var version) ||
            version < 1 ||
            !Guid.TryParse(HeaderText(delivery.BasicProperties.Headers, "x-tenant-id"), out var tenantId) ||
            !Guid.TryParse(HeaderText(delivery.BasicProperties.Headers, "x-shop-id"), out var shopId))
            return false;

        message = new ConsumedIntegrationMessage(
            messageId,
            delivery.BasicProperties.Type,
            version,
            DateTimeOffset.FromUnixTimeSeconds(delivery.BasicProperties.Timestamp.UnixTime),
            delivery.BasicProperties.CorrelationId,
            tenantId,
            shopId,
            delivery.Body);
        return true;
    }

    private static long RejectedDeaths(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-death", out var raw) || raw is not IList<object?> deaths)
            return 0;
        return deaths.OfType<IDictionary<string, object?>>()
            .Where(death => HeaderText(death, "reason") == "rejected")
            .Sum(death => death.TryGetValue("count", out var count) ? Convert.ToInt64(count) : 0);
    }

    private static string? HeaderText(IDictionary<string, object?>? headers, string name)
    {
        if (headers is null || !headers.TryGetValue(name, out var value)) return null;
        return value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();
    }

    private static void Validate(RabbitMqConsumerRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(registration.ConsumerName) ||
            string.IsNullOrWhiteSpace(registration.QueueName) ||
            string.IsNullOrWhiteSpace(registration.RoutingKey) ||
            registration.PrefetchCount == 0 || registration.MaxAttempts < 1 || registration.RetryDelayMilliseconds < 1)
            throw new InvalidOperationException("RabbitMQ consumer registration is invalid.");
    }
}
