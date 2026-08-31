using Dapper;
using FoodDelivery.Infrastructure.Messaging;
using FoodDelivery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using RabbitMQ.Client;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class ProductionOutboxDispatcherTests
{
    private const string DatabaseConnection =
        "Server=localhost;Port=3307;Database=food_delivery;User=root;Password=123456aA@;SslMode=None;";

    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task Multiple_dispatchers_claim_each_outbox_row_once()
    {
        await EnsureSchemaAsync();
        var ids = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        var options = CreateRabbitOptions();
        var queueName = $"outbox-production.{Guid.NewGuid():N}";
        await using var manager = new RabbitMqConnectionManager(options);
        await using var publisher = new RabbitMqPublisher(manager, options);
        var broker = await manager.GetConnectionAsync();
        await using var channel = await broker.CreateChannelAsync();
        await channel.QueueDeclareAsync(queueName, durable: false, exclusive: false, autoDelete: true);
        await channel.ExchangeDeclareAsync(options.ExchangeName, ExchangeType.Direct, durable: true);
        await channel.QueueBindAsync(queueName, options.ExchangeName, "test.outbox");

        await InsertMessagesAsync(ids);
        try
        {
            var workers = Enumerable.Range(0, 4)
                .Select(_ => new OutboxDispatcher(DatabaseConnection, publisher, options,
                    NullLogger<OutboxDispatcher>.Instance))
                .ToArray();

            var claimed = await Task.WhenAll(workers.Select(DrainAsync));
            Assert.Equal(ids.Length, claimed.Sum());

            await using var database = new MySqlConnection(DatabaseConnection);
            Assert.Equal(ids.Length, await database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM OutboxMessages WHERE Id IN @Ids AND SentAtUtc IS NOT NULL", new { Ids = ids }));

            var received = new HashSet<string>();
            for (var i = 0; i < ids.Length; i++)
            {
                var delivery = await channel.BasicGetAsync(queueName, autoAck: true);
                Assert.NotNull(delivery);
                Assert.True(received.Add(delivery!.BasicProperties.MessageId!));
            }
            Assert.Equal(ids.Length, received.Count);

        }
        finally
        {
            await using var database = new MySqlConnection(DatabaseConnection);
            await database.ExecuteAsync("DELETE FROM OutboxMessages WHERE Id IN @Ids", new { Ids = ids });
            await channel.ExchangeDeleteAsync(options.ExchangeName);
        }
    }

    private static async Task<int> DrainAsync(OutboxDispatcher worker)
    {
        var total = 0;
        int claimed;
        do
        {
            claimed = await worker.DispatchOnceAsync();
            total += claimed;
        } while (claimed > 0);
        return total;
    }

    private static async Task EnsureSchemaAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(DatabaseConnection, new MySqlServerVersion(new Version(8, 0, 21))).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
    }

    private static async Task InsertMessagesAsync(IEnumerable<Guid> ids)
    {
        await using var connection = new MySqlConnection(DatabaseConnection);
        await connection.ExecuteAsync(@"
            INSERT INTO OutboxMessages
                (Id,EventName,ContractVersion,RoutingKey,Payload,OccurredAtUtc,CorrelationId,TenantId,ShopId,
                 SentAtUtc,LockedBy,LockedUntilUtc,Attempts,LastError)
            VALUES
                (@Id,'TestEvent',1,'test.outbox','{}',UTC_TIMESTAMP(6),NULL,@TenantId,@ShopId,
                 NULL,NULL,NULL,0,NULL);",
            ids.Select(id => new { Id = id, TenantId = Guid.NewGuid(), ShopId = Guid.NewGuid() }));
    }

    private static RabbitMqOptions CreateRabbitOptions() => new()
    {
        HostName = RabbitMqEnvironment.HostName,
        Port = RabbitMqEnvironment.Port,
        UserName = RabbitMqEnvironment.UserName,
        Password = RabbitMqEnvironment.Password,
        VirtualHost = RabbitMqEnvironment.VirtualHost,
        ConnectionName = $"outbox-production-{Guid.NewGuid():N}",
        ExchangeName = $"food.events.tests.{Guid.NewGuid():N}",
        OutboxBatchSize = 10
    };
}
