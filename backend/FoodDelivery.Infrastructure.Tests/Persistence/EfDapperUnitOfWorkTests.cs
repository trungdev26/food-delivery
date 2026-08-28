using Dapper;
using FoodDelivery.Application.Abstractions;
using FoodDelivery.Domain.Common;
using FoodDelivery.Domain.Tenancy;
using FoodDelivery.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Persistence;

public class EfDapperUnitOfWorkTests
{
    [Fact]
    public async Task Commit_persists_EF_and_Dapper_writes_and_dispatches_event()
    {
        var dispatcher = new RecordingDispatcher();
        await using var setup = await Setup.CreateAsync(dispatcher);
        var tenant = Tenant.Create(Guid.NewGuid(), "EF Tenant", "ef-tenant");
        tenant.Deactivate();
        setup.Context.Tenants.Add(tenant);
        await InsertTenantWithDapper(setup, "dapper-tenant");

        await setup.UnitOfWork.CommitAsync();

        Assert.Equal(2, await setup.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Tenants"));
        Assert.Single(dispatcher.Events);
        Assert.Empty(tenant.DomainEvents);
    }

    [Fact]
    public async Task Dispatch_failure_rolls_back_EF_and_Dapper_writes_and_keeps_event()
    {
        var dispatcher = new RecordingDispatcher { Throw = true };
        await using var setup = await Setup.CreateAsync(dispatcher);
        var tenant = Tenant.Create(Guid.NewGuid(), "EF Tenant", "ef-tenant");
        tenant.Deactivate();
        setup.Context.Tenants.Add(tenant);
        await InsertTenantWithDapper(setup, "dapper-tenant");

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.UnitOfWork.CommitAsync());

        Assert.Equal(0, await setup.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Tenants"));
        Assert.Single(tenant.DomainEvents);
    }

    [Fact]
    public async Task Commit_dispatches_events_raised_by_event_handlers()
    {
        var dispatcher = new RecordingDispatcher();
        await using var setup = await Setup.CreateAsync(dispatcher);
        var tenant = Tenant.Create(Guid.NewGuid(), "EF Tenant", "ef-tenant");
        tenant.Deactivate();
        setup.Context.Tenants.Add(tenant);
        dispatcher.AfterFirstDispatch = tenant.Activate;

        await setup.UnitOfWork.CommitAsync();

        Assert.Equal(2, dispatcher.Events.Count);
        Assert.Empty(tenant.DomainEvents);
    }

    private static Task InsertTenantWithDapper(Setup setup, string subdomain) =>
        setup.Connection.ExecuteAsync(
            "INSERT INTO Tenants (Id, Name, Subdomain, Status, ConcurrencyToken) " +
            "VALUES (@Id, @Name, @Subdomain, @Status, @ConcurrencyToken)",
            new
            {
                Id = Guid.NewGuid(),
                Name = "Dapper Tenant",
                Subdomain = subdomain,
                Status = 1,
                ConcurrencyToken = Guid.NewGuid()
            },
            setup.Transaction);

    private sealed class RecordingDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Events { get; } = new();
        public bool Throw { get; init; }
        public Action? AfterFirstDispatch { get; set; }
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new InvalidOperationException("dispatch failed");
            Events.AddRange(events);
            if (Events.Count == 1)
            {
                var callback = AfterFirstDispatch;
                AfterFirstDispatch = null;
                callback?.Invoke();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(SqliteConnection connection, System.Data.Common.DbTransaction transaction,
            ApplicationDbContext context, EfDapperUnitOfWork unitOfWork)
        {
            Connection = connection;
            Transaction = transaction;
            Context = context;
            UnitOfWork = unitOfWork;
        }

        public SqliteConnection Connection { get; }
        public System.Data.Common.DbTransaction Transaction { get; }
        public ApplicationDbContext Context { get; }
        public EfDapperUnitOfWork UnitOfWork { get; }

        public static async Task<Setup> CreateAsync(IDomainEventDispatcher dispatcher)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();
            var transaction = await connection.BeginTransactionAsync();
            await context.Database.UseTransactionAsync(transaction);
            return new Setup(connection, transaction, context,
                new EfDapperUnitOfWork(context, connection, transaction, dispatcher));
        }

        public ValueTask DisposeAsync() => UnitOfWork.DisposeAsync();
    }
}
