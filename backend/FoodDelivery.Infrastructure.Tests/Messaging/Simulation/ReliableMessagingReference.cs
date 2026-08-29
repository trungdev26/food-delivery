using System.Data;
using Dapper;
using MySqlConnector;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Simulation;

internal static class ReliableMessagingReference
{
    internal static async Task<bool> ApplyOnceAsync(
        TestMessagingSchema schema,
        Guid runId,
        string consumerName,
        Guid tenantId,
        Guid shopId,
        Guid messageId,
        Guid lotId,
        int quantity)
    {
        await using var connection = schema.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var inserted = await connection.ExecuteAsync(@"
                INSERT IGNORE INTO TestInbox
                    (runId, consumerName, tenantId, shopId, messageId, processedAt)
                VALUES (@runId, @consumerName, @tenantId, @shopId, @messageId, UTC_TIMESTAMP(6));",
                new { runId, consumerName, tenantId, shopId, messageId }, transaction);

            if (inserted == 0)
            {
                await transaction.RollbackAsync();
                return false;
            }

            var changed = await connection.ExecuteAsync(@"
                UPDATE TestInventoryLot
                SET available = available - @quantity
                WHERE runId = @runId AND id = @lotId AND available >= @quantity;",
                new { runId, lotId, quantity }, transaction);
            if (changed != 1) throw new InvalidOperationException("Insufficient inventory.");

            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    internal static async Task AllocateStrictFefoAsync(
        TestMessagingSchema schema,
        Guid runId,
        Guid reservationId,
        int requestedQuantity)
    {
        await using var connection = schema.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var lots = (await connection.QueryAsync<Lot>(@"
                SELECT id, available
                FROM TestInventoryLot
                WHERE runId = @runId AND available > 0
                ORDER BY expiresAt, receivedAt, id
                FOR UPDATE;", new { runId }, transaction)).ToList();

            if (lots.Sum(x => x.Available) < requestedQuantity)
                throw new InvalidOperationException("Insufficient inventory.");

            var remaining = requestedQuantity;
            foreach (var lot in lots)
            {
                var quantity = Math.Min(lot.Available, remaining);
                if (quantity == 0) continue;

                await connection.ExecuteAsync(@"
                    UPDATE TestInventoryLot SET available = available - @quantity
                    WHERE runId = @runId AND id = @lotId;
                    INSERT INTO TestAllocation(runId, reservationId, lotId, quantity)
                    VALUES (@runId, @reservationId, @lotId, @quantity);",
                    new { runId, reservationId, lotId = lot.Id, quantity }, transaction);
                remaining -= quantity;
                if (remaining == 0) break;
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private sealed record Lot(Guid Id, int Available);
}
