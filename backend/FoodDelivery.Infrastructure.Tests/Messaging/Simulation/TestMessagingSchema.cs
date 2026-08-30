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

            CREATE TABLE IF NOT EXISTS TestStockCardLoad (
                runId CHAR(36) NOT NULL,
                id BIGINT NOT NULL,
                shopId INT NOT NULL,
                productId INT NOT NULL,
                expiresAt DATETIME(6) NOT NULL,
                available INT NOT NULL,
                PRIMARY KEY (runId, id),
                INDEX ix_stock_card_load_fefo (runId, shopId, productId, expiresAt, id)
            );

            CREATE TABLE IF NOT EXISTS TestOutboxLease (
                runId CHAR(36) NOT NULL,
                messageId CHAR(36) NOT NULL,
                payload JSON NOT NULL,
                occurredAt DATETIME(6) NOT NULL,
                sentAt DATETIME(6) NULL,
                lockedBy VARCHAR(100) NULL,
                lockedUntil DATETIME(6) NULL,
                attempts INT NOT NULL DEFAULT 0,
                PRIMARY KEY (runId, messageId),
                INDEX ix_outbox_lease_claim (runId, sentAt, lockedUntil, occurredAt, messageId),
                INDEX ix_outbox_claim_order (runId, sentAt, occurredAt, messageId)
            );

            CREATE TABLE IF NOT EXISTS TestReliabilityInbox (
                runId CHAR(36) NOT NULL,
                consumerName VARCHAR(100) NOT NULL,
                tenantId INT NOT NULL,
                shopId INT NOT NULL,
                messageId INT NOT NULL,
                processedAt DATETIME(6) NOT NULL,
                PRIMARY KEY (runId, consumerName, tenantId, shopId, messageId)
            );

            CREATE TABLE IF NOT EXISTS TestReliabilityEffectV2 (
                runId CHAR(36) NOT NULL,
                shopId INT NOT NULL,
                bucketId INT NOT NULL,
                applied INT NOT NULL,
                PRIMARY KEY (runId, shopId, bucketId)
            );
            ");

        try
        {
            await connection.ExecuteAsync(@"
                CREATE INDEX ix_outbox_claim_order
                ON TestOutboxLease(runId, sentAt, occurredAt, messageId);");
        }
        catch (MySqlException exception) when (exception.Number == 1061)
        {
            // Existing test database already has the index.
        }
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
              DELETE FROM TestStockCardLoad WHERE runId = @runId;
              DELETE FROM TestOutboxLease WHERE runId = @runId;
              DELETE FROM TestReliabilityInbox WHERE runId = @runId;
              DELETE FROM TestReliabilityEffectV2 WHERE runId = @runId;
              DELETE FROM TestInventoryLot WHERE runId = @runId;
              DELETE FROM TestOrder WHERE runId = @runId;",
            new { runId });
    }
}
