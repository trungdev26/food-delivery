using FoodDelivery.Infrastructure.Tests.Messaging.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Load;

public sealed class ReliabilityLoadTests
{
    private readonly ITestOutputHelper _output;

    public ReliabilityLoadTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "ReliabilityLoad")]
    public async Task WhenEnabled_DuplicatesRetriesAndPostCommitCrashesPreserveLogicalEffects()
    {
        if (Environment.GetEnvironmentVariable("RABBIT_RELIABILITY_LOAD") != "1")
        {
            _output.WriteLine("Set RABBIT_RELIABILITY_LOAD=1 to run the 100k reliability profile.");
            return;
        }

        var options = ReliabilityLoadOptions.FromEnvironment();
        await using var harness = new ReliabilityLoadHarness(options);

        var result = await harness.RunAsync();

        Assert.Equal(options.LogicalMessages, result.InboxRows);
        Assert.Equal(options.LogicalMessages, result.BusinessEffects);
        Assert.Equal(0, result.DuplicateBusinessEffects);
        Assert.Equal(0, result.MissingLogicalMessages);
        Assert.Equal(options.LogicalMessages * options.DuplicatePercent / 100, result.PublishedDuplicates);
        Assert.Equal(options.LogicalMessages * options.TransientFailurePercent / 100, result.TransientFailures);
        Assert.Equal(options.LogicalMessages * options.PostCommitCrashPercent / 100, result.PostCommitCrashes);
        Assert.True(result.Redeliveries >= result.PostCommitCrashes);
        Assert.Equal(0u, result.MainReady);
        Assert.Equal(0u, result.MainUnacked);
        Assert.Equal(0u, result.RetryReady);
        Assert.Equal(0u, result.DeadReady);
        _output.WriteLine(result.ToJson());
    }
}
