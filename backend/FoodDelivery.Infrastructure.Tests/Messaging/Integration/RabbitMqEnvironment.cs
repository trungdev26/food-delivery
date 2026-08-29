using RabbitMQ.Client;
using System.Net.Http.Headers;
using System.Text;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

internal static class RabbitMqEnvironment
{
    internal const string HostName = "localhost";
    internal const int Port = 5673;
    internal const string VirtualHost = "food";
    internal const string UserName = "food_app";
    internal const string Password = "food_dev";

    internal static HttpClient CreateManagementClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("http://localhost:15673/") };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserName}:{Password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return client;
    }

    internal static Task<IConnection> ConnectAsync(string connectionName = "food-delivery-tests") =>
        new ConnectionFactory
        {
            HostName = HostName,
            Port = Port,
            VirtualHost = VirtualHost,
            UserName = UserName,
            Password = Password,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        }.CreateConnectionAsync(connectionName);

    internal static Task<IConnection> ConnectClusterAsync(
        string connectionName,
        params int[] ports) =>
        new ConnectionFactory
        {
            VirtualHost = VirtualHost,
            UserName = UserName,
            Password = Password,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(2)
        }.CreateConnectionAsync(
            ports.Select(port => new AmqpTcpEndpoint(HostName, port)),
            connectionName);

    internal static HttpClient CreateClusterManagementClient(int port = 15674)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/") };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{UserName}:{Password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return client;
    }
}
