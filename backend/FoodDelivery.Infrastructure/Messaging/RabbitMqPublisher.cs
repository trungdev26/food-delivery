using System.Text.Json;
using FoodDelivery.Application.Abstractions.Messaging;
using RabbitMQ.Client;

namespace FoodDelivery.Infrastructure.Messaging;

public sealed class RabbitMqPublisher : IIntegrationEventPublisher, IAsyncDisposable
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private IChannel? _channel;

    public RabbitMqPublisher(RabbitMqConnectionManager connectionManager, RabbitMqOptions options)
    {
        options.EnsureValid();
        _connectionManager = connectionManager;
        _options = options;
    }

    public async Task PublishAsync<TEvent>(
        TEvent integrationEvent,
        string routingKey,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await PublishRawAsync(new IntegrationMessage(
            integrationEvent.MessageId,
            integrationEvent.EventName,
            integrationEvent.ContractVersion,
            integrationEvent.OccurredAtUtc,
            integrationEvent.CorrelationId,
            integrationEvent.TenantId,
            integrationEvent.ShopId,
            JsonSerializer.SerializeToUtf8Bytes(integrationEvent, integrationEvent.GetType())), routingKey, cancellationToken);
    }

    public async Task PublishRawAsync(
        IntegrationMessage message,
        string routingKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(routingKey)) throw new ArgumentException("Routing key is required.", nameof(routingKey));

        await _publishGate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                MessageId = message.MessageId.ToString(),
                Type = message.EventName,
                CorrelationId = message.CorrelationId,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(message.OccurredAtUtc.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["x-event-version"] = message.ContractVersion,
                    ["x-tenant-id"] = message.TenantId?.ToString() ?? string.Empty,
                    ["x-shop-id"] = message.ShopId?.ToString() ?? string.Empty
                }
            };

            await channel.BasicPublishAsync(
                _options.ExchangeName,
                routingKey,
                mandatory: true,
                basicProperties: properties,
                message.Payload,
                cancellationToken);
            MessagingMetrics.Published.Add(1, new KeyValuePair<string, object?>("event.name", message.EventName));
        }
        catch
        {
            MessagingMetrics.PublishFailures.Add(1);
            throw;
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel?.IsOpen == true) return _channel;
        if (_channel is not null) await _channel.DisposeAsync();

        var connection = await _connectionManager.GetConnectionAsync(cancellationToken);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await _channel.ExchangeDeclareAsync(
            _options.ExchangeName,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        _publishGate.Dispose();
    }
}
