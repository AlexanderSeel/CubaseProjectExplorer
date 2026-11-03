using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectOutlineTests
{
    [Fact]
    public void ConfirmedTrackPluginOwnershipProducesReliableInsertBranch()
    {
        var snap = new ProjectSnapshot("fixture.dawproject", "Fixture", "DAWproject", "15",
            null, EvidenceLevel.Confirmed,
            [new TrackInfo("t1", "Lead", "Audio", "c1", null, null, null, null)],
            [new PluginReference("fx-a", "Delay", null, "VST3", "t1", EvidenceLevel.Confirmed,
                Role: "FX", Slot: 2)], [], []);
        var root = ProjectOutlineBuilder.Build(snap);
        var group = Assert.Single(root.Children);
        var track = Assert.Single(group.Children);
        var device = Assert.Single(track.Children);
        Assert.Contains("Delay", device.Label);
        Assert.Contains("slot 2", device.Label);
    }

    [Fact]
    public void StructuredParentTrackHierarchyPreservesNestedDevices()
    {
        var snap = new ProjectSnapshot("nested.dawproject", "Nested", "DAWproject", null,
            null, EvidenceLevel.Confirmed,
            [
                new TrackInfo("folder", "Folder", "Folder", null, null, null, null, null),
                new TrackInfo("child", "Lead", "Audio", null, null, null, null, null,
                    ParentTrackId: "folder"),
                new TrackInfo("grandchild", "Sub", "Audio", null, null, null, null, null,
                    ParentTrackId: "child")
            ],
            [new PluginReference("fx", "Delay", null, "VST3", "grandchild",
                EvidenceLevel.Confirmed)], [], []);
        var group = Assert.Single(ProjectOutlineBuilder.Build(snap).Children);
        var folder = Assert.Single(group.Children);
        var lead = Assert.Single(folder.Children);
        var grandchild = Assert.Single(lead.Children);
        Assert.Contains("Sub", grandchild.Label);
        Assert.Contains("Delay", Assert.Single(grandchild.Children).Label);
    }

    [Fact]
    public void CyclicOrUnknownParentsDoNotHideTracks()
    {
        var snap = new ProjectSnapshot("bad.dawproject", "Bad", "DAWproject", null,
            null, EvidenceLevel.Confirmed,
            [
                new TrackInfo("a", "Alpha", "Audio", null, null, null, null, null,
                    ParentTrackId: "b"),
                new TrackInfo("b", "Beta", "Audio", null, null, null, null, null,
                    ParentTrackId: "a"),
                new TrackInfo("c", "Orphan", "Audio", null, null, null, null, null,
                    ParentTrackId: "missing")
            ], [], [], []);
        var group = Assert.Single(ProjectOutlineBuilder.Build(snap).Children);
        var labels = new List<string>();
        void Walk(ProjectOutlineNode node)
        {
            if (node.Kind == "Track") labels.Add(node.Label);
            foreach (var child in node.Children) Walk(child);
        }
        foreach (var root in group.Children) Walk(root);
        Assert.Equal(3, labels.Count);
        Assert.Contains(labels, label => label.Contains("Alpha"));
        Assert.Contains(labels, label => label.Contains("Beta"));
        Assert.Contains(labels, label => label.Contains("Orphan"));
    }

    [Fact]
    public void CandidateCprRecordsAreNeverLinkedToPluginMarkers()
    {
        var snap = new ProjectSnapshot("crash.bak", "Crash", "CPR", null, null,
            EvidenceLevel.Candidate,
            [new TrackInfo("candidate", "Audio 1", "Audio", null, null,
                null, null, null, EvidenceLevel.Candidate)],
            [new PluginReference("marker", "Reverb?", null, "binary", "candidate",
                EvidenceLevel.Candidate)], [], []);
        var root = ProjectOutlineBuilder.Build(snap);
        Assert.Equal(2, root.Children.Count);
        Assert.Contains("Unverified", root.Children[0].Label);
        Assert.Empty(Assert.Single(root.Children[0].Children).Children);
        Assert.Equal("Unassigned plugin/marker", Assert.Single(root.Children[1].Children).Kind);
    }

    [Fact]
    public void DeclaredStaticTimelineItemsAppearInProjectOutline()
    {
        var project = new ProjectSnapshot("song.dawproject", "Song", "DAWproject", null,
            null, EvidenceLevel.Confirmed, [], [], [], [], TimelineItems:
            [new ProjectTimelineItem("Marker", "Chorus", 16, null, null, null)]);
        var timeline = Assert.Single(ProjectOutlineBuilder.Build(project).Children);
        Assert.Contains("clips and markers", timeline.Label);
        Assert.Contains("Chorus", Assert.Single(timeline.Children).Label);
    }

    [Fact]
    public void DeclaredMediaReferencesAreVisibleWithoutInventingInstallation()
    {
        var snap = new ProjectSnapshot("song.dawproject", "Song", "DAWproject", null,
            null, EvidenceLevel.Confirmed, [], [], [], [], MediaReferences:
            [new ProjectMediaReference("samples/vocal.wav", false, 2, 44100, 3)]);
        var group = Assert.Single(ProjectOutlineBuilder.Build(snap).Children);
        Assert.Contains("existence not checked", group.Label);
        var media = Assert.Single(group.Children);
        Assert.Contains("samples/vocal.wav", media.Label);
        Assert.Contains("44100 Hz", media.Label);
    }

    [Fact]
    public void UnknownPluginParentStaysUnassignedForConfirmedData()
    {
        var snap = new ProjectSnapshot("fixture.dawproject", "Project", "DAWproject",
            null, null, EvidenceLevel.Confirmed, [],
            [new PluginReference("fx-a", "Orphan", null, "VST3", "missing",
                EvidenceLevel.Confirmed)], [], []);
        var root = ProjectOutlineBuilder.Build(snap);
        Assert.Single(root.Children);
        Assert.Contains("Unassigned", Assert.Single(root.Children).Label);
    }
}
