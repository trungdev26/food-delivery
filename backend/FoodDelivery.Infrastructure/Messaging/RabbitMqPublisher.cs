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
        if (string.IsNullOrWhiteSpace(routingKey)) throw new ArgumentException("Routing key is required.", nameof(routingKey));

        await _publishGate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                MessageId = integrationEvent.MessageId.ToString(),
                Type = integrationEvent.EventName,
                CorrelationId = integrationEvent.CorrelationId,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(integrationEvent.OccurredAtUtc.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["x-event-version"] = integrationEvent.ContractVersion,
                    ["x-tenant-id"] = integrationEvent.TenantId?.ToString() ?? string.Empty
                }
            };
            var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, integrationEvent.GetType());

            await channel.BasicPublishAsync(
                _options.ExchangeName,
                routingKey,
                mandatory: true,
                basicProperties: properties,
                body,
                cancellationToken);
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
