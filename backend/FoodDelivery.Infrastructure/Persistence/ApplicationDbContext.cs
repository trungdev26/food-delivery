using FoodDelivery.Application.Abstractions;
using FoodDelivery.Domain.Common;
using FoodDelivery.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly IDomainEventDispatcher _eventDispatcher;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IDomainEventDispatcher eventDispatcher) : base(options) => _eventDispatcher = eventDispatcher;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker.Entries()
            .Select(entry => entry.Entity)
            .OfType<AggregateRoot<Guid>>()
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToArray();
        var events = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToArray();

        if (events.Length == 0)
            return await base.SaveChangesAsync(cancellationToken);

        await using var transaction = Database.IsRelational()
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var affected = await base.SaveChangesAsync(cancellationToken);
            await _eventDispatcher.DispatchAsync(events, cancellationToken);
            affected += await base.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            foreach (var aggregate in aggregates) aggregate.ClearDomainEvents();
            return affected;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
