using Dapper;
using MySqlConnector;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Simulation;

internal sealed class TestMessagingSchema
{
    private const string ConnectionString =
        "Server=localhost;Port=3307;Database=food_delivery;User=root;Password=123456aA@;";

    internal MySqlConnection CreateConnection() => new(ConnectionString);

    internal async Task InitializeAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS TestOrder (
                runId CHAR(36) NOT NULL,
                id CHAR(36) NOT NULL,
                PRIMARY KEY (runId, id)
            );

            CREATE TABLE IF NOT EXISTS TestInventoryLot (
                runId CHAR(36) NOT NULL,
                id CHAR(36) NOT NULL,
                expiresAt DATETIME(6) NOT NULL,
                receivedAt DATETIME(6) NOT NULL,
                available INT NOT NULL,
                PRIMARY KEY (runId, id),
                INDEX ix_test_lot_fefo (runId, expiresAt, receivedAt, id)
            );
            ");
    }

    internal async Task CleanupAsync(Guid runId)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "DELETE FROM TestInventoryLot WHERE runId = @runId; DELETE FROM TestOrder WHERE runId = @runId;",
            new { runId });
    }
}
