using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectAutomationTests
{
    [Fact]
    public async Task DatabaseRoundTripRetainsLaneOwnershipAndSourceBounds()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-automation-");
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory.FullName, "library.db"));
            var project = ProjectAutomation.Normalize(Project(Track("owner", "Audio", 10, 1000), Track("lane", "Automation", 100, 200)));
            await store.SaveProjectInspectionAsync(project);
            var restored = Assert.Single(await store.GetProjectsAsync()).Snapshot!;
            Assert.Equal(project.AutomationLanes, restored.AutomationLanes);
            Assert.Single(restored.Tracks);
        }
        finally { directory.Delete(true); }
    }

    private static TrackInfo Track(string id, string kind, long start, long end) =>
        new(id, id, kind, null, null, null, null, null, EvidenceLevel.Candidate,
            kind == "Automation" ? "MAutomationTrackEvent" : "MAudioTrackEvent", start, SourceEndOffset: end);

    private static ProjectSnapshot Project(params TrackInfo[] tracks) =>
        new("Session.cpr", "Session", "CPR", "15", null, EvidenceLevel.Candidate, tracks, [], [], []);

    [Fact]
    public void LanesUseTheSmallestUnambiguousEnclosingTrackAndNeverBecomeStandaloneTracks()
    {
        var project = Project(Track("folder", "Folder", 10, 1000), Track("audio", "Audio", 100, 500),
            Track("lane", "Automation", 200, 300));
        var result = ProjectAutomation.Normalize(project);
        Assert.Equal(new[] { "folder", "audio" }, result.Tracks.Select(track => track.Id));
        var lane = Assert.Single(result.AutomationLanes!);
        Assert.Equal("audio", lane.OwnerTrackId);
        Assert.Equal(EvidenceLevel.Candidate, lane.Evidence);
        Assert.Equal(300, lane.SourceEndOffset);
        Assert.Contains("exact enclosing CPR track byte extent", lane.RelationshipSource);
        Assert.Same(result, ProjectAutomation.Normalize(result));
        Assert.DoesNotContain(StudioIoDiagramBuilder.Build(project).Nodes, node => node.Kind == "Automation");
    }

    [Fact]
    public void EqualExtentsMissingOwnersAndInvalidBoundsStayUnassignedWithoutNameMatching()
    {
        var project = Project(Track("one", "Audio", 10, 1000), Track("two", "Audio", 10, 1000),
            Track("ambiguous", "Automation", 200, 300), Track("outside", "Automation", 2000, 2100),
            Track("invalid", "Automation", 500, 400), Track("negative", "Automation", -100, 10));
        var result = ProjectAutomation.Normalize(project);
        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal(4, result.AutomationLanes!.Count);
        Assert.All(result.AutomationLanes!, lane => Assert.Null(lane.OwnerTrackId));
    }

    [Fact]
    public void ExportedParentIdsAreUsedWithoutNativeContainmentInference()
    {
        var owner = Track("audio", "Audio", 10, 100) with { Evidence = EvidenceLevel.Confirmed };
        var automation = Track("lane", "Automation", 300, 400) with
            { Evidence = EvidenceLevel.Confirmed, ParentTrackId = "audio" };
        var lane = Assert.Single(ProjectAutomation.Normalize(Project(owner, automation)).AutomationLanes!);
        Assert.Equal("audio", lane.OwnerTrackId);
        Assert.Equal("Declared parent track ID in project export", lane.RelationshipSource);
    }

    [Fact]
    public void DuplicateOwnerIdsCannotEstablishAnAutomationParent()
    {
        var result = ProjectAutomation.Normalize(Project(Track("same", "Audio", 10, 1000),
            Track("same", "Audio", 100, 900), Track("lane", "Automation", 200, 300)));
        Assert.Null(Assert.Single(result.AutomationLanes!).OwnerTrackId);
    }
}
