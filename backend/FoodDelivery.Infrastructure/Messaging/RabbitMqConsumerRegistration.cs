using FoodDelivery.Application.Abstractions;

namespace FoodDelivery.Infrastructure.Messaging;

public sealed record RabbitMqConsumerRegistration(
    string ConsumerName,
    string QueueName,
    string RoutingKey,
    Func<ReadOnlyMemory<byte>, IUnitOfWork, CancellationToken, Task> HandleAsync,
    ushort PrefetchCount = 20,
    int MaxAttempts = 5,
    int RetryDelayMilliseconds = 5000);

public sealed class PermanentMessageException : Exception
{
    public PermanentMessageException(string message) : base(message) { }
}
