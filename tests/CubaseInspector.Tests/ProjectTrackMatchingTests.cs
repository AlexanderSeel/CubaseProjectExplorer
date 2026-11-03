using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectTrackMatchingTests
{
    private static TrackInfo Track(string id, string name, string? channel = null, string? destination = null) =>
        new(id, name, "Audio", channel, destination, 1, false, false);

    private static ProjectSnapshot Snapshot(string path, params TrackInfo[] tracks) =>
        new(path, "Example", "DAWproject", "Cubase 15", 120, EvidenceLevel.Confirmed, tracks, [], [], []);

    [Fact]
    public void ReusedExportIdDoesNotEstablishCrossProjectIdentity()
    {
        var before = Snapshot("a.dawproject", Track("id1", "Retrologue"));
        var after = Snapshot("b.dawproject", Track("id1", "Massive"));
        var changes = new ProjectDiff().Compare(before, after);
        Assert.Contains(changes, change => change.Property == "Added");
        Assert.Contains(changes, change => change.Property == "Removed");
        Assert.DoesNotContain(changes, change => change.Property == "Name");
    }

    [Fact]
    public void MappingNormalizesOwnersParentsDestinationsAndRoutesTogether()
    {
        var before = Snapshot("a.dawproject", Track("a", "Bass", "ca", "ma"), Track("m", "Master", "ma"));
        var after = Snapshot("b.dawproject", Track("a", "New track"), Track("b", "Bass", "cb", "mb"), Track("n", "Master", "mb"));
        var plugin = new PluginReference("class", "EQ", null, "Vst3Plugin", "a", EvidenceLevel.Confirmed, Slot: 0);
        before = before with
        {
            Tracks = [before.Tracks[0] with { ParentTrackId = "m" }, before.Tracks[1]],
            Plugins = [plugin], Routing = [new("ca", "ma", "Output")]
        };
        after = after with
        {
            Tracks = [after.Tracks[0], after.Tracks[1] with { ParentTrackId = "n" }, after.Tracks[2]],
            Plugins = [plugin with { TrackId = "b" }], Routing = [new("cb", "mb", "Output")]
        };
        var changes = new ProjectDiff().Compare(before, after, [new(0, 1), new(1, 2)]);
        var added = Assert.Single(changes);
        Assert.Equal("New track", added.Entity);
        Assert.Equal("Added", added.Property);
    }

    [Fact]
    public void ExplicitUnmatchedChoiceDoesNotFallBackToNamesOrIds()
    {
        var before = Snapshot("a.dawproject", Track("id1", "Bass"));
        var after = Snapshot("b.dawproject", Track("id1", "Bass"));
        var changes = new ProjectDiff().Compare(before, after, [new(0, null)]);
        Assert.Contains(changes, change => change.Property == "Added");
        Assert.Contains(changes, change => change.Property == "Removed");
    }

    [Fact]
    public void DuplicateNamesNeedExplicitOneToOneDecisions()
    {
        var before = Snapshot("a.dawproject", Track("a", "Bass"), Track("b", "Bass"));
        var after = Snapshot("b.dawproject", Track("a", "Bass"), Track("b", "Bass"));
        Assert.All(ProjectTrackMatching.Suggest(before, after), suggestion => Assert.Null(suggestion.AfterIndex));
        Assert.Empty(new ProjectDiff().Compare(before, after, [new(0, 1), new(1, 0)]));
        Assert.Throws<ArgumentException>(() => new ProjectDiff().Compare(before, after, [new(0, 0), new(1, 0)]));
        Assert.Throws<ArgumentException>(() => new ProjectDiff().Compare(before, after, [new(0, 0)]));
        Assert.Throws<ArgumentException>(() => new ProjectDiff().Compare(before, after, [new(0, 9), new(1, null)]));
    }

    [Fact]
    public async Task SavedDecisionsSurviveRestartAndAreScopedToPairAndParsedData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cpe-matches-" + Guid.NewGuid().ToString("N"));
        var database = Path.Combine(directory, "library.db");
        var before = Snapshot("a.dawproject", Track("id1", "Old name"));
        var after = Snapshot("b.dawproject", Track("id2", "New name"));
        try
        {
            using (var store = new LibraryDocumentStore(database))
                await store.SaveTrackMatchesAsync(before, after, [new(0, 0)]);
            using (var store = new LibraryDocumentStore(database))
            {
                var saved = await store.GetTrackMatchesAsync(before, after);
                Assert.NotNull(saved);
                Assert.Equal(new TrackMatchDecision(0, 0), Assert.Single(saved.Decisions));
                Assert.Equal("User-specified track matches", saved.Provenance);
                Assert.NotEqual(default, saved.SavedUtc);
                var reverse = await store.GetTrackMatchesAsync(after, before);
                Assert.NotNull(reverse);
                Assert.Equal(new TrackMatchDecision(0, 0), Assert.Single(reverse.Decisions));
                Assert.Null(await store.GetTrackMatchesAsync(before, after with { FilePath = "other.dawproject" }));
                Assert.Null(await store.GetTrackMatchesAsync(before with { Bpm = 130 }, after));
                Assert.Null(await store.GetTrackMatchesAsync(before, after with
                {
                    Tracks = [after.Tracks[0] with { Pan = .25 }]
                }));
                // A rejected overwrite must leave the valid saved mapping intact.
                await Assert.ThrowsAsync<ArgumentException>(() => store.SaveTrackMatchesAsync(before, after, [new(0, 7)]));
                Assert.NotNull(await store.GetTrackMatchesAsync(before, after));
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SwappingPairInvertsPartialMappingAndPreservesUnmatchedTracks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cpe-matches-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory, "library.db"));
            var before = Snapshot("a.dawproject", Track("a", "Old"), Track("b", "Removed"));
            var after = Snapshot("b.dawproject", Track("b", "Added"), Track("c", "Renamed"), Track("d", "Also added"));
            await store.SaveTrackMatchesAsync(before, after, [new(0, 1), new(1, null)]);
            var saved = await store.GetTrackMatchesAsync(after, before);
            Assert.NotNull(saved);
            Assert.Equal([new TrackMatchDecision(0, null), new(1, 0), new(2, null)], saved.Decisions);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
