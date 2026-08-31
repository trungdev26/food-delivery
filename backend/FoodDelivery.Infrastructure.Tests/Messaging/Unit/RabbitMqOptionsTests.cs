using FoodDelivery.Infrastructure.Messaging;
using Xunit;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Unit;

public sealed class RabbitMqOptionsTests
{
    [Fact]
    public void EnsureValid_RejectsMissingConnectionSettings()
    {
        var options = new RabbitMqOptions();

        var exception = Assert.Throws<InvalidOperationException>(options.EnsureValid);

        Assert.Contains("RabbitMq:HostName", exception.Message);
        Assert.Contains("RabbitMq:UserName", exception.Message);
        Assert.Contains("RabbitMq:Password", exception.Message);
        Assert.Contains("RabbitMq:ExchangeName", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void EnsureValid_RejectsPortOutsideTcpRange(int port)
    {
        var options = ValidOptions();
        options.Port = port;

        var exception = Assert.Throws<InvalidOperationException>(options.EnsureValid);

        Assert.Contains("RabbitMq:Port", exception.Message);
    }

    [Fact]
    public void EnsureValid_AcceptsCompleteConfiguration()
    {
        var options = ValidOptions();

        options.EnsureValid();
    }

    [Fact]
    public void EnsureValid_RequiresServerNameWhenTlsIsEnabled()
    {
        var options = ValidOptions();
        options.TlsEnabled = true;

        var exception = Assert.Throws<InvalidOperationException>(options.EnsureValid);

        Assert.Contains("RabbitMq:TlsServerName", exception.Message);
    }

    private static RabbitMqOptions ValidOptions() => new()
    {
        HostName = "localhost",
        Port = 5673,
        UserName = "food_app",
        Password = "food_dev",
        VirtualHost = "food",
        ConnectionName = "food-api",
        ExchangeName = "food.events"
    };
}
