using FoodDelivery.Application.Abstractions;

namespace FoodDelivery.Infrastructure.Messaging;

public sealed record RabbitMqConsumerRegistration(
    string ConsumerName,
    string QueueName,
    string RoutingKey,
    Func<ConsumedIntegrationMessage, IUnitOfWork, CancellationToken, Task> HandleAsync,
    ushort PrefetchCount = 20,
    int MaxAttempts = 5,
    int RetryDelayMilliseconds = 5000);

public sealed record ConsumedIntegrationMessage(
    Guid MessageId,
    string EventName,
    int ContractVersion,
    DateTimeOffset OccurredAtUtc,
    string? CorrelationId,
    Guid TenantId,
    Guid ShopId,
    ReadOnlyMemory<byte> Payload);

public sealed class PermanentMessageException : Exception
{
    public PermanentMessageException(string message) : base(message) { }
}
