using FoodDelivery.Application.Abstractions;
using FoodDelivery.Domain.Common;
using FoodDelivery.Domain.Tenancy;
using FoodDelivery.Infrastructure.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;

namespace FoodDelivery.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(x => typeof(AggregateRoot<Guid>).IsAssignableFrom(x.ClrType)))
        {
            entityType.FindProperty(nameof(AggregateRoot<Guid>.ConcurrencyToken))!
                .IsConcurrencyToken = true;
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RotateConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RotateConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RotateConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<AggregateRoot<Guid>>()
                     .Where(x => x.State == EntityState.Modified))
        {
            entry.Property(x => x.ConcurrencyToken).CurrentValue = Guid.NewGuid();
        }
    }
}
