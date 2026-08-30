using FoodDelivery.Infrastructure.Tests.Messaging.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace FoodDelivery.Infrastructure.Tests.Messaging.Load;

public sealed class InventoryRecalculationCoalescingTests
{
    private readonly ITestOutputHelper _output;

    public InventoryRecalculationCoalescingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "InventoryCoalescingLoad")]
    public async Task BurstAndMessageDuringRecalculation_ConvergeWithoutLosingTheLatestVersion()
    {
        if (Environment.GetEnvironmentVariable("RABBIT_INVENTORY_COALESCING") != "1")
        {
            _output.WriteLine("Set RABBIT_INVENTORY_COALESCING=1 to run the 50k stock-card profile.");
            return;
        }

        await using var harness = new InventoryRecalculationCoalescingHarness(
            stockCards: 50_000,
            initialMessages: 100,
            debounce: TimeSpan.FromMilliseconds(200),
            maxWait: TimeSpan.FromSeconds(2));

        var result = await harness.RunAsync();

        Assert.Equal(101, result.MessagesAcked);
        Assert.Equal(0u, result.QueueReady);
        Assert.Equal(0u, result.QueueUnacked);
        Assert.Equal(101, result.RequestedVersion);
        Assert.Equal(result.RequestedVersion, result.CompletedVersion);
        Assert.Equal(101, result.MinimumCardVersion);
        Assert.Equal(101, result.MaximumCardVersion);
        Assert.Equal(50_000L, result.ActualCardCount);
        Assert.InRange(result.RowsUpdatedBeforeInjectedMessage, 1, 49_999);
        Assert.InRange(result.FirstPassVersion, 1, 100);
        Assert.Equal(2, result.Recalculations);
        Assert.Equal(5_050_000, result.BaselineRowVisits);
        Assert.Equal(100_000, result.CoalescedRowUpdates);
        _output.WriteLine(result.ToJson());
    }
}
