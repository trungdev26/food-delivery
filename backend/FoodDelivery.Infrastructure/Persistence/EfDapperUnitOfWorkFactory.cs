using System.Data.Common;
using FoodDelivery.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace FoodDelivery.Infrastructure.Persistence;

public sealed class EfDapperUnitOfWorkFactory : IUnitOfWorkFactory
{
    private static readonly MySqlServerVersion ServerVersion = new(new Version(8, 0, 21));
    private readonly string _connectionString;
    private readonly IDomainEventDispatcher _eventDispatcher;

    public EfDapperUnitOfWorkFactory(string connectionString, IDomainEventDispatcher eventDispatcher)
    {
        _connectionString = connectionString;
        _eventDispatcher = eventDispatcher;
    }

    public async Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default)
    {
        MySqlConnection? connection = null;
        DbTransaction? transaction = null;
        ApplicationDbContext? dbContext = null;

        try
        {
            connection = new MySqlConnection(_connectionString);
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseMySql(connection, ServerVersion)
                .Options;
            await connection.OpenAsync(cancellationToken);
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            dbContext = new ApplicationDbContext(options);
            await dbContext.Database.UseTransactionAsync(transaction, cancellationToken);

            return new EfDapperUnitOfWork(dbContext, connection, transaction, _eventDispatcher);
        }
        catch
        {
            if (dbContext is not null) await dbContext.DisposeAsync();
            if (transaction is not null) await transaction.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
            throw;
        }
    }
}
