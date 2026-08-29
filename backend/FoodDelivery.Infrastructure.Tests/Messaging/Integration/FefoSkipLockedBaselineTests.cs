using System.Data;
using Dapper;
using FoodDelivery.Infrastructure.Tests.Messaging.Simulation;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class FefoSkipLockedBaselineTests
{
    [Fact]
    [Trait("Category", "BaselineTests")]
    public async Task SkipLocked_SelectsLaterLotWhileEarliestLotStillHasStock()
    {
        var schema = new TestMessagingSchema();
        var runId = Guid.NewGuid();
        var earliestLotId = Guid.NewGuid();
        var laterLotId = Guid.NewGuid();
        await schema.InitializeAsync();

        try
        {
            await using (var setup = schema.CreateConnection())
            {
                await setup.OpenAsync();
                await setup.ExecuteAsync(@"
                    INSERT INTO TestInventoryLot(runId, id, expiresAt, receivedAt, available) VALUES
                    (@runId, @earliestLotId, '2026-09-01', '2026-08-01', 5),
                    (@runId, @laterLotId, '2026-09-10', '2026-08-02', 8);
                    ", new { runId, earliestLotId, laterLotId });
            }

            await using var firstConnection = schema.CreateConnection();
            await firstConnection.OpenAsync();
            await using var firstTransaction = await firstConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var lockedLotId = await firstConnection.ExecuteScalarAsync<Guid>(@"
                SELECT id FROM TestInventoryLot
                WHERE runId = @runId AND available > 0
                ORDER BY expiresAt, receivedAt, id
                LIMIT 1 FOR UPDATE;
                ", new { runId }, firstTransaction);

            await using var secondConnection = schema.CreateConnection();
            await secondConnection.OpenAsync();
            await using var secondTransaction = await secondConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var skippedToLotId = await secondConnection.ExecuteScalarAsync<Guid>(@"
                SELECT id FROM TestInventoryLot
                WHERE runId = @runId AND available > 0
                ORDER BY expiresAt, receivedAt, id
                LIMIT 1 FOR UPDATE SKIP LOCKED;
                ", new { runId }, secondTransaction);

            Assert.Equal(earliestLotId, lockedLotId);
            Assert.Equal(laterLotId, skippedToLotId);

            await secondTransaction.RollbackAsync();
            await firstTransaction.RollbackAsync();
        }
        finally
        {
            await schema.CleanupAsync(runId);
        }
    }
}
