using FoodDelivery.Domain.Common;
using Xunit;

namespace FoodDelivery.Domain.Tests.Common;

public class AggregateRootTests
{
    private sealed record SomethingHappened : IDomainEvent;

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate() : base(Guid.NewGuid()) { }
        public void Change() => RaiseDomainEvent(new SomethingHappened());
    }

    [Fact]
    public void Raise_and_clear_domain_events()
    {
        var aggregate = new TestAggregate();
        aggregate.Change();

        Assert.Single(aggregate.DomainEvents);
        aggregate.ClearDomainEvents();
        Assert.Empty(aggregate.DomainEvents);
    }
}
