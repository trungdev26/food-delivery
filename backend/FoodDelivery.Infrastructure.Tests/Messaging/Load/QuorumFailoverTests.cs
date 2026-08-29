using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FoodDelivery.Infrastructure.Tests.Messaging.Integration;
using RabbitMQ.Client;
using Xunit;
using Xunit.Abstractions;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Load;

public sealed class QuorumFailoverTests
{
    private readonly ITestOutputHelper _output;

    public QuorumFailoverTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "ClusterFailover")]
    public async Task WhenEnabled_LeaderStops_ConfirmedMessagesSurviveAndPublishingResumes()
    {
        if (Environment.GetEnvironmentVariable("RABBIT_CLUSTER_FAILOVER") != "1")
        {
            _output.WriteLine("Set RABBIT_CLUSTER_FAILOVER=1 to run the destructive cluster profile.");
            return;
        }

        const int messageCount = 2_000;
        var queueName = $"load.failover.{Guid.NewGuid():N}";
        string? stoppedContainer = null;
        await using var connection = await RabbitMqEnvironment.ConnectClusterAsync("failover-test", 5674, 5675, 5676);
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-quorum-initial-group-size"] = 3
            });

        try
        {
            for (var id = 0; id < messageCount / 2; id++)
                await PublishAsync(channel, queueName, id);

            var before = await ReadQueueAsync(queueName);
            Assert.Equal(3, before.Members.Length);
            stoppedContainer = before.Leader.Replace("rabbit@rabbit", "food-rabbit");

            var publishFailures = 0;
            await using var survivingConnection = await RabbitMqEnvironment.ConnectClusterAsync(
                "failover-survivor", SurvivingPorts(stoppedContainer));
            await using var survivingChannel = await survivingConnection.CreateChannelAsync(new CreateChannelOptions(true, true));
            var failureWindowReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var publishing = Task.Run(async () =>
            {
                for (var id = messageCount / 2; id < messageCount; id++)
                {
                    if (id == messageCount / 2 + 50) failureWindowReached.TrySetResult(true);
                    try
                    {
                        await PublishAsync(survivingChannel, queueName, id);
                    }
                    catch
                    {
                        publishFailures++;
                        id--;
                        await Task.Delay(100);
                    }
                }
            });

            await failureWindowReached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var electionWatch = Stopwatch.StartNew();
            await DockerAsync("stop", stoppedContainer);
            var after = await WaitForNewLeaderAsync(queueName, before.Leader);
            electionWatch.Stop();
            await publishing.WaitAsync(TimeSpan.FromMinutes(2));

            var ids = new HashSet<int>();
            var deliveries = 0;
            while (ids.Count < messageCount)
            {
                var delivery = await survivingChannel.BasicGetAsync(queueName, autoAck: true);
                if (delivery is null) break;
                deliveries++;
                ids.Add(int.Parse(delivery.BasicProperties.MessageId!));
            }

            Assert.Equal(messageCount, ids.Count);
            Assert.True(deliveries >= messageCount);
            Assert.NotEqual(before.Leader, after.Leader);
            _output.WriteLine(JsonSerializer.Serialize(new
            {
                messages = messageCount,
                leaderBefore = before.Leader,
                leaderAfter = after.Leader,
                electionSeconds = electionWatch.Elapsed.TotalSeconds,
                publishFailures,
                deliveries,
                duplicateDeliveries = deliveries - ids.Count
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (stoppedContainer is not null) await DockerAsync("start", stoppedContainer);
            try { await channel.QueueDeleteAsync(queueName); } catch { }
        }
    }

    private static ValueTask PublishAsync(IChannel channel, string queueName, int id) =>
        channel.BasicPublishAsync(
            string.Empty,
            queueName,
            mandatory: true,
            new BasicProperties { MessageId = id.ToString(), DeliveryMode = DeliveryModes.Persistent },
            Encoding.UTF8.GetBytes("{}"));

    private static async Task<QueueState> WaitForNewLeaderAsync(string queueName, string oldLeader)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var port in new[] { 15674, 15675, 15676 })
            {
                try
                {
                    var state = await ReadQueueAsync(queueName, port);
                    if (state.Leader != oldLeader && state.Members.Length == 3) return state;
                }
                catch { }
            }
            await Task.Delay(250);
        }
        throw new TimeoutException("Quorum queue did not elect a new leader within 30 seconds.");
    }

    private static async Task<QueueState> ReadQueueAsync(string queueName, int port = 15674)
    {
        using var client = RabbitMqEnvironment.CreateClusterManagementClient(port);
        using var response = await client.GetAsync($"api/queues/food/{Uri.EscapeDataString(queueName)}");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new QueueState(
            json.RootElement.GetProperty("leader").GetString()!,
            json.RootElement.GetProperty("members").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    private static int[] SurvivingPorts(string stoppedContainer) => stoppedContainer switch
    {
        "food-rabbit1" => new[] { 5675, 5676 },
        "food-rabbit2" => new[] { 5674, 5676 },
        "food-rabbit3" => new[] { 5674, 5675 },
        _ => throw new InvalidOperationException($"Unknown cluster container {stoppedContainer}.")
    };

    private static async Task DockerAsync(string command, string container)
    {
        using var process = Process.Start(new ProcessStartInfo("docker", $"{command} {container}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start Docker CLI.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(await process.StandardError.ReadToEndAsync());
    }

    private sealed record QueueState(string Leader, string[] Members);
}
