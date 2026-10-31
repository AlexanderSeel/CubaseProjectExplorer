using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectChartTests
{
    [Fact]
    public void DistributionsHaveExactCountsAndFractions()
    {
        var project = new ProjectSnapshot("a.dawproject", "A", "DAWproject",
            null, null, EvidenceLevel.Confirmed,
            [Track("a", "Audio", EvidenceLevel.Confirmed), Track("b", "Audio", EvidenceLevel.Confirmed),
             Track("c", "MIDI", EvidenceLevel.Confirmed)],
            [new PluginReference("id", "Echo", null, "VST3", "a", EvidenceLevel.Confirmed)], [], []);
        var bars = ProjectChartService.Build(project);
        Assert.Equal(2, bars.TrackTypes.Count);
        Assert.Equal(2, bars.TrackTypes[0].Count);
        Assert.Equal(200d / 3d, bars.TrackTypes[0].Percentage, 5);
        Assert.Single(bars.PluginFormats);
        Assert.Contains("plugin references", bars.PluginFormats[0].CountLabel);
    }

    [Fact]
    public void NativeCprExcludesUnverifiedPluginMarkers()
    {
        var project = new ProjectSnapshot("crash.bak", "Backup", "CPR",
            null, null, EvidenceLevel.Candidate,
            [Track("a", "Audio", EvidenceLevel.Candidate)],
            [new PluginReference("offset", "Unknown", null, "CPR token", null,
                EvidenceLevel.Candidate)], [], []);
        var bars = ProjectChartService.Build(project);
        Assert.Single(bars.TrackTypes);
        Assert.Empty(bars.PluginFormats);
        Assert.Contains("candidate", bars.TrackTypes[0].CountLabel);
    }

    private static TrackInfo Track(string id, string kind, EvidenceLevel level) =>
        new(id, id, kind, null, null, null, null, null, level);
}
