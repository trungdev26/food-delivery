using System.Diagnostics.Metrics;

namespace FoodDelivery.Infrastructure.Messaging;

internal static class MessagingMetrics
{
    private static readonly Meter Meter = new("FoodDelivery.Messaging");

    internal static readonly Counter<long> Published = Meter.CreateCounter<long>("messaging.published");
    internal static readonly Counter<long> PublishFailures = Meter.CreateCounter<long>("messaging.publish.failures");
    internal static readonly Counter<long> OutboxClaimed = Meter.CreateCounter<long>("messaging.outbox.claimed");
    internal static readonly Counter<long> Consumed = Meter.CreateCounter<long>("messaging.consumed");
    internal static readonly Counter<long> Duplicates = Meter.CreateCounter<long>("messaging.inbox.duplicates");
    internal static readonly Counter<long> Retries = Meter.CreateCounter<long>("messaging.retries");
    internal static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("messaging.dead_lettered");
}
