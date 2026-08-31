using System.Data.Common;
using System.Text.Json;
using FoodDelivery.Application.Abstractions;
using FoodDelivery.Application.Abstractions.Messaging;
using FoodDelivery.Domain.Common;
using FoodDelivery.Infrastructure.Persistence.Messaging;

namespace FoodDelivery.Infrastructure.Persistence;

public sealed class EfDapperUnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private State _state = State.Active;

    public EfDapperUnitOfWork(
        ApplicationDbContext dbContext,
        DbConnection connection,
        DbTransaction transaction,
        IDomainEventDispatcher eventDispatcher)
    {
        _dbContext = dbContext;
        Connection = connection;
        Transaction = transaction;
        _eventDispatcher = eventDispatcher;
    }

    public IApplicationDbContext DbContext => _dbContext;
    public DbConnection Connection { get; }
    public DbTransaction Transaction { get; }

    public void EnqueueIntegrationEvent(IIntegrationEvent integrationEvent, string routingKey)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (string.IsNullOrWhiteSpace(routingKey))
            throw new ArgumentException("Routing key is required.", nameof(routingKey));

        _dbContext.OutboxMessages.Add(new OutboxMessage(
            integrationEvent.MessageId,
            integrationEvent.EventName,
            integrationEvent.ContractVersion,
            routingKey,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            integrationEvent.OccurredAtUtc.UtcDateTime,
            integrationEvent.CorrelationId,
            integrationEvent.TenantId,
            integrationEvent.ShopId));
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        try
        {
            var dispatched = new HashSet<IDomainEvent>(ReferenceEqualityComparer.Instance);
            while (true)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                var pending = TrackedAggregates()
                    .SelectMany(aggregate => aggregate.DomainEvents)
                    .Where(domainEvent => !dispatched.Contains(domainEvent))
                    .ToArray();
                if (pending.Length == 0) break;

                await _eventDispatcher.DispatchAsync(pending, cancellationToken);
                foreach (var domainEvent in pending) dispatched.Add(domainEvent);
            }

            await Transaction.CommitAsync(cancellationToken);
            foreach (var aggregate in TrackedAggregates()) aggregate.ClearDomainEvents();
            _state = State.Committed;
        }
        catch
        {
            await TryRollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await Transaction.RollbackAsync(cancellationToken);
        _state = State.RolledBack;
    }

    public async ValueTask DisposeAsync()
    {
        if (_state == State.Disposed) return;
        if (_state == State.Active) await TryRollbackAsync(CancellationToken.None);
        await _dbContext.DisposeAsync();
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
        _state = State.Disposed;
    }

    private void EnsureActive()
    {
        if (_state != State.Active)
            throw new InvalidOperationException($"Unit of Work is {_state}.");
    }

    private IEnumerable<AggregateRoot<Guid>> TrackedAggregates() =>
        _dbContext.ChangeTracker.Entries()
            .Select(entry => entry.Entity)
            .OfType<AggregateRoot<Guid>>();

    private async Task TryRollbackAsync(CancellationToken cancellationToken)
    {
        try { await Transaction.RollbackAsync(cancellationToken); }
        catch { /* preserve the original failure */ }
        _state = State.RolledBack;
    }

    private enum State { Active, Committed, RolledBack, Disposed }
}
