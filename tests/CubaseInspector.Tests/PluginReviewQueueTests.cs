using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;
public class PluginReviewQueueTests
{
    [Fact]
    public void NextReviewSkipsReviewedAndRejectedAndWraps()
    {
        string[] states = ["Reviewed", "Name hint", "Rejected", "No match"];
        Assert.Equal(1, PluginReviewQueue.NextIndex(states, -1));
        Assert.Equal(3, PluginReviewQueue.NextIndex(states, 1));
        Assert.Equal(1, PluginReviewQueue.NextIndex(states, 3));
    }

    [Fact]
    public void EmptyOrFullyReviewedReturnsNone()
    {
        Assert.Equal(-1, PluginReviewQueue.NextIndex([], -1));
        Assert.Equal(-1, PluginReviewQueue.NextIndex(["Reviewed", "Rejected"], 0));
    }
}
