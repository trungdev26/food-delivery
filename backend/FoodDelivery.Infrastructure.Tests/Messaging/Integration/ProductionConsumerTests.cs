using Dapper;
using FoodDelivery.Application.Abstractions;
using FoodDelivery.Infrastructure.Messaging;
using FoodDelivery.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using RabbitMQ.Client;
using System.Text;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class ProductionConsumerTests
{
    private const string DatabaseConnection =
        "Server=localhost;Port=3307;Database=food_delivery;User=root;Password=123456aA@;SslMode=None;";

    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task Duplicate_delivery_runs_handler_once_and_commits_inbox()
    {
        var options = CreateOptions();
        var queue = $"consumer-production.{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var calls = 0;
        var registration = new RabbitMqConsumerRegistration(
            "production-consumer-test", queue, "test.consumer",
            (_, _, _) => { Interlocked.Increment(ref calls); return Task.CompletedTask; },
            RetryDelayMilliseconds: 50);
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWorkFactory>(
            new EfDapperUnitOfWorkFactory(DatabaseConnection, new NoOpDomainEventDispatcher()));
        await using var provider = services.BuildServiceProvider();
        await using var manager = new RabbitMqConnectionManager(options);
        await using var publisher = new RabbitMqPublisher(manager, options);
        var logger = new ListLogger<RabbitMqConsumerHostedService>();
        var service = new RabbitMqConsumerHostedService(manager, options,
            provider.GetRequiredService<IServiceScopeFactory>(), new[] { registration },
            logger);

        await service.StartAsync(CancellationToken.None);
        var connection = await manager.GetConnectionAsync();
        await WaitForQueueAsync(connection, queue);
        await using var channel = await connection.CreateChannelAsync();
        var message = new IntegrationMessage(messageId, "TestEvent", 1, DateTimeOffset.UtcNow,
            null, tenantId, shopId, Encoding.UTF8.GetBytes("{}"));
        try
        {
            await publisher.PublishRawAsync(message, registration.RoutingKey);
            await publisher.PublishRawAsync(message, registration.RoutingKey);
            try { await WaitForInboxAsync(registration.ConsumerName, tenantId, shopId, messageId); }
            catch (TimeoutException)
            {
                var dead = await channel.BasicGetAsync($"{queue}.dead", true);
                var main = await channel.BasicGetAsync(queue, true);
                var retry = await channel.BasicGetAsync($"{queue}.retry", true);
                var reason = dead?.BasicProperties.Headers?["x-failure-reason"];
                throw new Xunit.Sdk.XunitException(
                    $"calls={calls}; main={main is not null}; retry={retry is not null}; dead={dead is not null}; " +
                    $"reason={FormatHeader(reason)}; logs={string.Join(" | ", logger.Messages)}");
            }
            await Task.Delay(100);

            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            await using var database = new MySqlConnection(DatabaseConnection);
            await database.ExecuteAsync(@"
                DELETE FROM InboxMessages
                WHERE ConsumerName=@ConsumerName AND TenantId=@TenantId AND ShopId=@ShopId AND MessageId=@MessageId;",
                new { registration.ConsumerName, TenantId = tenantId, ShopId = shopId, MessageId = messageId });
            foreach (var name in new[] { queue, $"{queue}.retry", $"{queue}.dead" })
                try { await channel.QueueDeleteAsync(name); } catch { }
            foreach (var name in new[] { options.ExchangeName, $"{options.ExchangeName}.retry", $"{options.ExchangeName}.dead" })
                try { await channel.ExchangeDeleteAsync(name); } catch { }
            service.Dispose();
        }
    }

    private static string? FormatHeader(object? value) =>
        value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();

    private static async Task WaitForQueueAsync(IConnection connection, string queue)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var channel = await connection.CreateChannelAsync();
            try
            {
                var state = await channel.QueueDeclarePassiveAsync(queue);
                if (state.ConsumerCount > 0) return;
                await Task.Delay(20);
            }
            catch when (attempt < 99) { await Task.Delay(20); }
        }
    }

    private static async Task WaitForInboxAsync(string consumerName, Guid tenantId, Guid shopId, Guid messageId)
    {
        await using var connection = new MySqlConnection(DatabaseConnection);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await connection.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM InboxMessages
                    WHERE ConsumerName=@ConsumerName AND TenantId=@TenantId AND ShopId=@ShopId AND MessageId=@MessageId;",
                    new { ConsumerName = consumerName, TenantId = tenantId, ShopId = shopId, MessageId = messageId }) == 1)
                return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Inbox message was not committed.");
    }

    private static RabbitMqOptions CreateOptions() => new()
    {
        HostName = RabbitMqEnvironment.HostName,
        Port = RabbitMqEnvironment.Port,
        UserName = RabbitMqEnvironment.UserName,
        Password = RabbitMqEnvironment.Password,
        VirtualHost = RabbitMqEnvironment.VirtualHost,
        ConnectionName = $"consumer-production-{Guid.NewGuid():N}",
        ExchangeName = $"food.events.tests.{Guid.NewGuid():N}"
    };

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<FoodDelivery.Domain.Common.IDomainEvent> events,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add($"{formatter(state, exception)}: {exception}");
    }
}
