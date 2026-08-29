using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class RabbitMqEnvironmentTests
{
    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task Broker_IsReachable()
    {
        await using var connection = await RabbitMqEnvironment.ConnectAsync();

        Assert.True(connection.IsOpen);
    }

    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task ManagementApi_IsReachable()
    {
        using var client = RabbitMqEnvironment.CreateManagementClient();

        using var response = await client.GetAsync("api/overview");

        response.EnsureSuccessStatusCode();
    }
}
