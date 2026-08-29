using FoodDelivery.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class RabbitMqHealthCheckTests
{
    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task CheckHealthAsync_WhenBrokerIsReachable_ReturnsHealthy()
    {
        var options = new RabbitMqOptions
        {
            HostName = RabbitMqEnvironment.HostName,
            Port = RabbitMqEnvironment.Port,
            UserName = RabbitMqEnvironment.UserName,
            Password = RabbitMqEnvironment.Password,
            VirtualHost = RabbitMqEnvironment.VirtualHost,
            ConnectionName = $"health-tests-{Guid.NewGuid():N}",
            ExchangeName = $"food.events.tests.{Guid.NewGuid():N}"
        };
        await using var manager = new RabbitMqConnectionManager(options);
        var healthCheck = new RabbitMqHealthCheck(manager);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
