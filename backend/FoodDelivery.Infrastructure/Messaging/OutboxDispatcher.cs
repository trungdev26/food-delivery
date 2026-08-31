using System.Data;
using System.Text;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace FoodDelivery.Infrastructure.Messaging;

public sealed class OutboxDispatcher : BackgroundService
{
    private readonly string _connectionString;
    private readonly RabbitMqPublisher _publisher;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    public OutboxDispatcher(
        string connectionString,
        RabbitMqPublisher publisher,
        RabbitMqOptions options,
        ILogger<OutboxDispatcher> logger)
    {
        _connectionString = connectionString;
        _publisher = publisher;
        _options = options;
        _logger = logger;
    }

    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken = default)
    {
        var messages = await ClaimAsync(cancellationToken);
        MessagingMetrics.OutboxClaimed.Add(messages.Count);
        foreach (var message in messages)
        {
            try
            {
                await _publisher.PublishRawAsync(message.ToIntegrationMessage(), message.RoutingKey, cancellationToken);
                await MarkSentAsync(message.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await RecordFailureAsync(message.Id, exception.Message, cancellationToken);
                _logger.LogError(exception, "Failed to publish outbox message {MessageId}", message.Id);
            }
        }

        return messages.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await DispatchOnceAsync(stoppingToken) == 0)
                    await Task.Delay(_options.OutboxPollMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox dispatch cycle failed");
                await Task.Delay(_options.OutboxPollMilliseconds, stoppingToken);
            }
        }
    }

    private async Task<List<OutboxRow>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var rows = (await connection.QueryAsync<OutboxRow>(new CommandDefinition(@"
            SELECT Id, EventName, ContractVersion, RoutingKey, Payload, OccurredAtUtc,
                   CorrelationId, TenantId, ShopId
            FROM OutboxMessages
            WHERE SentAtUtc IS NULL
              AND (LockedUntilUtc IS NULL OR LockedUntilUtc < UTC_TIMESTAMP(6))
            ORDER BY OccurredAtUtc, Id
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED;",
            new { BatchSize = _options.OutboxBatchSize }, transaction, cancellationToken: cancellationToken))).AsList();

        if (rows.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE OutboxMessages
                SET LockedBy=@WorkerId,
                    LockedUntilUtc=DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @LeaseSeconds SECOND),
                    Attempts=Attempts+1
                WHERE Id=@Id AND SentAtUtc IS NULL;",
                rows.Select(row => new { row.Id, WorkerId = _workerId, LeaseSeconds = _options.OutboxLeaseSeconds }),
                transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private Task MarkSentAsync(Guid id, CancellationToken cancellationToken) => ExecuteAsync(@"
        UPDATE OutboxMessages
        SET SentAtUtc=UTC_TIMESTAMP(6), LockedBy=NULL, LockedUntilUtc=NULL, LastError=NULL
        WHERE Id=@Id AND SentAtUtc IS NULL AND LockedBy=@WorkerId;", id, null, cancellationToken);

    private Task RecordFailureAsync(Guid id, string error, CancellationToken cancellationToken) => ExecuteAsync(@"
        UPDATE OutboxMessages SET LastError=@Error
        WHERE Id=@Id AND SentAtUtc IS NULL AND LockedBy=@WorkerId;", id, error, cancellationToken);

    private async Task ExecuteAsync(string sql, Guid id, string? error, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(sql,
            new { Id = id, WorkerId = _workerId, Error = error?[..Math.Min(error.Length, 4000)] },
            cancellationToken: cancellationToken));
    }

    private sealed record OutboxRow(
        Guid Id,
        string EventName,
        int ContractVersion,
        string RoutingKey,
        string Payload,
        DateTime OccurredAtUtc,
        string? CorrelationId,
        Guid? TenantId,
        Guid? ShopId)
    {
        internal IntegrationMessage ToIntegrationMessage() => new(
            Id, EventName, ContractVersion,
            new DateTimeOffset(DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc)),
            CorrelationId, TenantId, ShopId, Encoding.UTF8.GetBytes(Payload));
    }
}
