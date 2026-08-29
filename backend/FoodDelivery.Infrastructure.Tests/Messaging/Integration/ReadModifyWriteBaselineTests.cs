using System.Data;
using Dapper;
using FoodDelivery.Infrastructure.Tests.Messaging.Simulation;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class ReadModifyWriteBaselineTests
{
    [Fact]
    [Trait("Category", "BaselineTests")]
    public async Task ConcurrentReadModifyWrite_LosesOneReservation()
    {
        var schema = new TestMessagingSchema();
        var runId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        await schema.InitializeAsync();

        try
        {
            await using (var setup = schema.CreateConnection())
            {
                await setup.OpenAsync();
                await setup.ExecuteAsync(@"
                    INSERT INTO TestInventoryLot(runId, id, expiresAt, receivedAt, available)
                    VALUES (@runId, @lotId, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 10);
                    ", new { runId, lotId });
            }

            var bothRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var readCount = 0;

            async Task ReserveAsync()
            {
                await using var connection = schema.CreateConnection();
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                var available = await connection.ExecuteScalarAsync<int>(
                    "SELECT available FROM TestInventoryLot WHERE runId = @runId AND id = @lotId;",
                    new { runId, lotId }, transaction);

                if (Interlocked.Increment(ref readCount) == 2) bothRead.SetResult(true);
                await bothRead.Task;

                await connection.ExecuteAsync(
                    "UPDATE TestInventoryLot SET available = @available WHERE runId = @runId AND id = @lotId;",
                    new { available = available - 7, runId, lotId }, transaction);
                await transaction.CommitAsync();
            }

            await Task.WhenAll(ReserveAsync(), ReserveAsync());

            await using var verification = schema.CreateConnection();
            await verification.OpenAsync();
            var finalAvailable = await verification.ExecuteScalarAsync<int>(
                "SELECT available FROM TestInventoryLot WHERE runId = @runId AND id = @lotId;",
                new { runId, lotId });

            Assert.Equal(3, finalAvailable);
            Assert.NotEqual(-4, finalAvailable);
        }
        finally
        {
            await schema.CleanupAsync(runId);
        }
    }
}
