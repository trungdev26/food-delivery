using Dapper;
using MySqlConnector;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Simulation;

internal sealed class TestMessagingSchema
{
    private const string ConnectionString =
        "Server=localhost;Port=3307;Database=food_delivery;User=root;Password=123456aA@;SslMode=None;";

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

            CREATE TABLE IF NOT EXISTS TestOutbox (
                runId CHAR(36) NOT NULL,
                messageId CHAR(36) NOT NULL,
                payload JSON NOT NULL,
                sentAt DATETIME(6) NULL,
                PRIMARY KEY (runId, messageId)
            );

            CREATE TABLE IF NOT EXISTS TestInbox (
                runId CHAR(36) NOT NULL,
                consumerName VARCHAR(100) NOT NULL,
                tenantId CHAR(36) NOT NULL,
                shopId CHAR(36) NOT NULL,
                messageId CHAR(36) NOT NULL,
                processedAt DATETIME(6) NOT NULL,
                PRIMARY KEY (runId, consumerName, tenantId, shopId, messageId)
            );

            CREATE TABLE IF NOT EXISTS TestAllocation (
                runId CHAR(36) NOT NULL,
                reservationId CHAR(36) NOT NULL,
                lotId CHAR(36) NOT NULL,
                quantity INT NOT NULL,
                PRIMARY KEY (runId, reservationId, lotId)
            );

            CREATE TABLE IF NOT EXISTS TestAggregateVersion (
                runId CHAR(36) NOT NULL,
                aggregateId CHAR(36) NOT NULL,
                version INT NOT NULL,
                value VARCHAR(100) NOT NULL,
                PRIMARY KEY (runId, aggregateId)
            );
            ");
    }

    internal async Task CleanupAsync(Guid runId)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            @"DELETE FROM TestAllocation WHERE runId = @runId;
              DELETE FROM TestInbox WHERE runId = @runId;
              DELETE FROM TestOutbox WHERE runId = @runId;
              DELETE FROM TestAggregateVersion WHERE runId = @runId;
              DELETE FROM TestInventoryLot WHERE runId = @runId;
              DELETE FROM TestOrder WHERE runId = @runId;",
            new { runId });
    }
}
