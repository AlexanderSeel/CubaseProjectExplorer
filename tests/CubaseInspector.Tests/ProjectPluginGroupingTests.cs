using CubaseInspector.Core;
using Xunit;
namespace CubaseInspector.Tests;
public class ProjectPluginGroupingTests
{
    [Fact]
    public void GroupsDuplicateConfirmedReferencesAndPreservesTrackSlot()
    {
        var p = new PluginReference("ABC", "Equalizer", "Vendor", "VST3", "t", EvidenceLevel.Confirmed, Slot: 1);
        var project = Snapshot([p, p with { Slot = 3 }], EvidenceLevel.Confirmed);
        var group = Assert.Single(ProjectPluginGrouping.Group(project));
        Assert.Equal(2, group.Observations);
        Assert.Contains("Lead", group.Uses[0].Location);
        Assert.Contains("insert slot 3", group.Uses[1].Location);
        Assert.All(group.Uses, use => Assert.True(use.CanNavigateToTrack));
        Assert.Equal("t", group.Uses[0].DeclaredTrackId);
    }
    [Fact]
    public void CandidateSameNameGroupsReferencesButNeverClaimsInstances()
    {
        const string id = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var a = new PluginReference(id, "Synth", null, "CPR UID (format unverified)",
            null, EvidenceLevel.Candidate, SourceByteOffset: 12);
        var b = a with { SourceByteOffset = 42 };
        var group = Assert.Single(ProjectPluginGrouping.Group(Snapshot([a,b], EvidenceLevel.Candidate)));
        Assert.Contains("2 project references", group.CountLabel);
        Assert.Equal(42, group.Uses[1].Reference.SourceByteOffset);
        Assert.All(group.Uses, use => Assert.False(use.CanNavigateToTrack));
    }
    [Fact]
    public void MissingTrackAndCandidateProjectCannotBeNavigated()
    {
        var plugin = new PluginReference("A", "Synth", "Maker", "VST3", "missing",
            EvidenceLevel.Confirmed);
        var missing = Assert.Single(ProjectPluginGrouping.Group(Snapshot([plugin],
            EvidenceLevel.Confirmed)));
        Assert.False(missing.Uses[0].CanNavigateToTrack);
        Assert.Contains("Location not found", missing.Uses[0].Location);
        var candidate = Assert.Single(ProjectPluginGrouping.Group(Snapshot(
            [plugin with { TrackId = "t" }], EvidenceLevel.Candidate)));
        Assert.False(candidate.Uses[0].CanNavigateToTrack);
    }

    private static ProjectSnapshot Snapshot(IReadOnlyList<PluginReference> plugins, EvidenceLevel evidence) =>
        new("example.cpr", "Example", "test", null, null, evidence,
            [new TrackInfo("t","Lead","Audio","c",null,null,null,null)],
            plugins, [], []);
}
