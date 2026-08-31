using System.Text.Json;
using Dapper;
using FoodDelivery.Application.Abstractions;
using FoodDelivery.Application.Abstractions.Messaging;
using FoodDelivery.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Persistence;

public sealed class TransactionalOutboxTests
{
    [Fact]
    public async Task Rollback_discards_enqueued_integration_event()
    {
        await using var setup = await Setup.CreateAsync();

        setup.UnitOfWork.EnqueueIntegrationEvent(TestIntegrationEvent.Create(), "inventory.reserve");
        await setup.UnitOfWork.RollbackAsync();

        Assert.Equal(0, await setup.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM OutboxMessages"));
    }

    [Fact]
    public async Task Commit_persists_serialized_event_and_transport_metadata()
    {
        await using var setup = await Setup.CreateAsync();
        var integrationEvent = TestIntegrationEvent.Create();

        setup.UnitOfWork.EnqueueIntegrationEvent(integrationEvent, "inventory.reserve");
        await setup.UnitOfWork.CommitAsync();

        await using var readContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(setup.Connection).Options);
        var message = await readContext.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(integrationEvent.MessageId, message.Id);
        Assert.Equal(integrationEvent.EventName, message.EventName);
        Assert.Equal(integrationEvent.ContractVersion, message.ContractVersion);
        Assert.Equal("inventory.reserve", message.RoutingKey);
        Assert.Equal(integrationEvent.OccurredAtUtc.UtcDateTime, message.OccurredAtUtc);
        Assert.Equal(integrationEvent.CorrelationId, message.CorrelationId);
        Assert.Equal(integrationEvent.TenantId, message.TenantId);
        Assert.Equal(integrationEvent.ShopId, message.ShopId);

        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(integrationEvent.MessageId, payload.RootElement.GetProperty("MessageId").GetGuid());
        Assert.Equal("sku-001", payload.RootElement.GetProperty("Sku").GetString());
    }

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(SqliteConnection connection, EfDapperUnitOfWork unitOfWork)
        {
            Connection = connection;
            UnitOfWork = unitOfWork;
        }

        public SqliteConnection Connection { get; }
        public EfDapperUnitOfWork UnitOfWork { get; }

        public static async Task<Setup> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var transaction = await connection.BeginTransactionAsync();
            await context.Database.UseTransactionAsync(transaction);
            return new Setup(connection, new EfDapperUnitOfWork(
                context,
                connection,
                transaction,
                new NoOpDomainEventDispatcher()));
        }

        public ValueTask DisposeAsync() => UnitOfWork.DisposeAsync();
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<FoodDelivery.Domain.Common.IDomainEvent> events,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record TestIntegrationEvent(
        Guid MessageId,
        string EventName,
        int ContractVersion,
        DateTimeOffset OccurredAtUtc,
        string? CorrelationId,
        Guid? TenantId,
        Guid? ShopId,
        string Sku) : IIntegrationEvent
    {
        public static TestIntegrationEvent Create() => new(
            Guid.NewGuid(),
            "InventoryReserveRequested",
            1,
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            "order-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "sku-001");
    }
}
