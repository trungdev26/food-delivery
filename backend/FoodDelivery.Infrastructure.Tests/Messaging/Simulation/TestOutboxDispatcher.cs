using System.Data;
using System.Text;
using Dapper;
using FoodDelivery.Infrastructure.Tests.Messaging.Integration;
using RabbitMQ.Client;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Simulation;

internal enum OutboxFailureCheckpoint
{
    None,
    BeforePublish,
    AfterConfirmBeforeMarkSent
}

internal sealed record OutboxDispatchResult(int Claimed, int Published, int MarkedSent);

internal sealed class TestOutboxDispatcher : IAsyncDisposable
{
    private readonly TestMessagingSchema _schema;
    private readonly Guid _runId;
    private readonly string _queueName;
    private readonly string _workerId;
    private readonly int _batchSize;
    private readonly int _leaseSeconds;
    private IConnection? _brokerConnection;
    private IChannel? _channel;

    internal TestOutboxDispatcher(
        TestMessagingSchema schema,
        Guid runId,
        string queueName,
        string workerId,
        int batchSize,
        int leaseSeconds = 10)
    {
        _schema = schema;
        _runId = runId;
        _queueName = queueName;
        _workerId = workerId;
        _batchSize = batchSize;
        _leaseSeconds = leaseSeconds;
    }

    internal async Task<OutboxDispatchResult> DispatchOnceAsync(
        OutboxFailureCheckpoint checkpoint = OutboxFailureCheckpoint.None,
        CancellationToken cancellationToken = default)
    {
        var rows = await ClaimAsync(cancellationToken);
        if (checkpoint == OutboxFailureCheckpoint.BeforePublish)
            return new OutboxDispatchResult(rows.Count, 0, 0);

        await EnsureBrokerAsync(cancellationToken);
        var published = 0;
        var marked = 0;
        foreach (var row in rows)
        {
            await _channel!.BasicPublishAsync(
                string.Empty,
                _queueName,
                mandatory: true,
                new BasicProperties
                {
                    MessageId = row.MessageId.ToString(),
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent
                },
                Encoding.UTF8.GetBytes(row.Payload),
                cancellationToken);
            published++;

            if (checkpoint == OutboxFailureCheckpoint.AfterConfirmBeforeMarkSent)
                return new OutboxDispatchResult(rows.Count, published, marked);

            await using var connection = _schema.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            marked += await connection.ExecuteAsync(@"
                UPDATE TestOutboxLease
                SET sentAt=UTC_TIMESTAMP(6), lockedBy=NULL, lockedUntil=NULL
                WHERE runId=@runId AND messageId=@messageId
                  AND sentAt IS NULL AND lockedBy=@workerId;",
                new { row.RunId, row.MessageId, workerId = _workerId });
        }
        return new OutboxDispatchResult(rows.Count, published, marked);
    }

    private async Task<List<OutboxRow>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var connection = _schema.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var rows = (await connection.QueryAsync<OutboxRow>(@"
            SELECT runId,messageId,payload
            FROM TestOutboxLease FORCE INDEX (ix_outbox_claim_order)
            WHERE runId=@runId AND sentAt IS NULL
              AND (lockedUntil IS NULL OR lockedUntil < UTC_TIMESTAMP(6))
            ORDER BY occurredAt,messageId
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED;",
            new { runId = _runId, batchSize = _batchSize }, transaction)).ToList();
        if (rows.Count > 0)
        {
            await connection.ExecuteAsync(@"
                UPDATE TestOutboxLease
                SET lockedBy=@workerId,
                    lockedUntil=DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @leaseSeconds SECOND),
                    attempts=attempts+1
                WHERE runId=@runId AND messageId=@messageId;",
                rows.Select(row => new { row.RunId, row.MessageId, workerId = _workerId, leaseSeconds = _leaseSeconds }),
                transaction);
        }
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private async Task EnsureBrokerAsync(CancellationToken cancellationToken)
    {
        if (_channel?.IsOpen == true) return;
        _brokerConnection = await RabbitMqEnvironment.ConnectAsync($"outbox-{_workerId}");
        _channel = await _brokerConnection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_brokerConnection is not null) await _brokerConnection.DisposeAsync();
    }

    private sealed record OutboxRow(Guid RunId, Guid MessageId, string Payload);
}
