using FoodDelivery.Infrastructure.Tests.Messaging.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Integration;

public sealed class RetryAndDeadLetterTests
{
    private readonly ITestOutputHelper _output;

    public RetryAndDeadLetterTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task TransientFailure_RetriesWithDelayThenSucceeds()
    {
        await using var harness = new RabbitMqRetryHarness(delayMilliseconds: 100, maxAttempts: 3);

        var result = await harness.RunAsync(
            Guid.NewGuid(),
            attempt => attempt < 3 ? RetryDisposition.TransientFailure : RetryDisposition.Success);

        Assert.Equal(3, result.Attempts);
        Assert.Equal(2, result.RejectedDeaths);
        Assert.Equal(2, result.ExpiredDeaths);
        Assert.Null(result.DeadLetter);
        Assert.Equal(0u, result.MainReady);
        Assert.Equal(0u, result.RetryReady);
        Assert.Equal(0u, result.DeadReady);
        WriteResult("transient-then-success", result);
    }

    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task PermanentFailure_GoesDirectlyToDeadLetterQueue()
    {
        var messageId = Guid.NewGuid();
        await using var harness = new RabbitMqRetryHarness(delayMilliseconds: 100, maxAttempts: 3);

        var result = await harness.RunAsync(messageId, _ => RetryDisposition.PermanentFailure);

        Assert.Equal(1, result.Attempts);
        Assert.Equal(messageId.ToString(), result.DeadLetter!.BasicProperties.MessageId);
        Assert.Equal("permanent", result.FailureReason);
        Assert.Equal(0u, result.MainReady);
        Assert.Equal(0u, result.RetryReady);
        Assert.Equal(0u, result.DeadReady);
        WriteResult("permanent-to-dlq", result);
    }

    [Fact]
    [Trait("Category", "RabbitMqIntegration")]
    public async Task PoisonMessage_StopsAtMaxAttemptsAndPreservesDeathHistory()
    {
        var messageId = Guid.NewGuid();
        await using var harness = new RabbitMqRetryHarness(delayMilliseconds: 100, maxAttempts: 3);

        var result = await harness.RunAsync(messageId, _ => RetryDisposition.TransientFailure);

        Assert.Equal(3, result.Attempts);
        Assert.Equal(2, result.RejectedDeaths);
        Assert.Equal(2, result.ExpiredDeaths);
        Assert.Equal(messageId.ToString(), result.DeadLetter!.BasicProperties.MessageId);
        Assert.Equal("max-attempts", result.FailureReason);
        Assert.Equal(0u, result.MainReady);
        Assert.Equal(0u, result.RetryReady);
        Assert.Equal(0u, result.DeadReady);
        WriteResult("poison-to-dlq", result);
    }

    private void WriteResult(string scenario, RetryRunResult result) =>
        _output.WriteLine(
            $"scenario={scenario}; attempts={result.Attempts}; rejected={result.RejectedDeaths}; " +
            $"expired={result.ExpiredDeaths}; failureReason={result.FailureReason ?? "none"}; " +
            $"ready(main/retry/dead)={result.MainReady}/{result.RetryReady}/{result.DeadReady}");
}
