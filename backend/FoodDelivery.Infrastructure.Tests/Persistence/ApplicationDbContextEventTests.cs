using FoodDelivery.Application.Abstractions;
using FoodDelivery.Domain.Common;
using FoodDelivery.Domain.Tenancy;
using FoodDelivery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Persistence;

public class ApplicationDbContextEventTests
{
    [Fact]
    public async Task Save_dispatches_and_clears_events_after_success()
    {
        var dispatcher = new RecordingDispatcher();
        await using var db = CreateDb(dispatcher);
        var tenant = Tenant.Create(Guid.NewGuid(), "Tenant", "tenant");
        tenant.Deactivate();
        db.Tenants.Add(tenant);

        await db.SaveChangesAsync();

        Assert.Single(dispatcher.Events);
        Assert.Empty(tenant.DomainEvents);
    }

    [Fact]
    public async Task Save_keeps_events_when_dispatch_fails()
    {
        var dispatcher = new RecordingDispatcher { Throw = true };
        await using var db = CreateDb(dispatcher);
        var tenant = Tenant.Create(Guid.NewGuid(), "Tenant", "tenant");
        tenant.Deactivate();
        db.Tenants.Add(tenant);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Single(tenant.DomainEvents);
    }

    private static ApplicationDbContext CreateDb(IDomainEventDispatcher dispatcher) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options, dispatcher);

    private sealed class RecordingDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Events { get; } = new();
        public bool Throw { get; init; }

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new InvalidOperationException("dispatch failed");
            Events.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
