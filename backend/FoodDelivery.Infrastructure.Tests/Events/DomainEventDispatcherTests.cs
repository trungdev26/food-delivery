using FoodDelivery.Application.Abstractions;
using FoodDelivery.Domain.Common;
using FoodDelivery.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Events;

public class DomainEventDispatcherTests
{
    private sealed record TestEvent : IDomainEvent;

    private sealed class TestHandler : IDomainEventHandler<TestEvent>
    {
        public int Calls { get; private set; }

        public Task Handle(TestEvent domainEvent, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Dispatches_event_to_registered_handler_once()
    {
        var services = new ServiceCollection();
        var handler = new TestHandler();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
        await using var provider = services.BuildServiceProvider();

        var dispatcher = new DomainEventDispatcher(provider);
        await dispatcher.DispatchAsync(new IDomainEvent[] { new TestEvent() });

        Assert.Equal(1, handler.Calls);
    }
}
