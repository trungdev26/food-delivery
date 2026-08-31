using System.Data.Common;
using FoodDelivery.Application.Abstractions.Messaging;

namespace FoodDelivery.Application.Abstractions;

public interface IUnitOfWork : IAsyncDisposable
{
    IApplicationDbContext DbContext { get; }
    DbConnection Connection { get; }
    DbTransaction Transaction { get; }
    void EnqueueIntegrationEvent(IIntegrationEvent integrationEvent, string routingKey);
    Task<bool> TryBeginInboxMessageAsync(
        string consumerName,
        Guid tenantId,
        Guid shopId,
        Guid messageId,
        CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
