using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LibraryPresentationTests
{
    [Fact]
    public void EmptyQueryHasNoActiveFilterChips()
    {
        Assert.Empty(ProjectLibraryFilterSummary.Describe(new ProjectLibraryQuery()));
        Assert.Empty(ProjectLibraryFilterSummary.Describe(new ProjectLibraryQuery(SortBy: "Largest", ReverseSort: true)));
    }

    [Fact]
    public void ActiveChipsCountIndependentFacetsButDoNotHideUnknownBpmWithoutRange()
    {
        var q = new ProjectLibraryQuery(Search: "mix", FileType: "CPR", Evidence: "Candidate",
            VersionContains: "12", FolderContains: "Music", TrackType: "Audio",
            MinBpm: 100, MaxBpm: 160, IncludeUnknownBpm: false, WarningsOnly: true,
            ModifiedSinceUtc: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var chips = ProjectLibraryFilterSummary.Describe(q);
        Assert.Equal(11, chips.Count);
        Assert.Contains("Exclude unknown BPM", chips);
        Assert.Contains(chips, x => x.StartsWith("Modified from "));
        Assert.DoesNotContain(ProjectLibraryFilterSummary.Describe(q with {
            MinBpm = null, MaxBpm = null }), x => x == "Exclude unknown BPM");
    }

    [Fact]
    public void ComparisonRowsPreserveNullAndClassifyAmbiguousChanges()
    {
        var input = new ProjectChange[] {
            new("Track", "Lead", "Added", null, "Audio", "Unmatched"),
            new("Track", "Pad", "Removed", "Instrument", null, "Unmatched"),
            new("Track", "Bass", "Volume", "0.5", "0.7", "Exact ID"),
            new("Ambiguous", "Duplicate", "Match", "id", null, "Duplicate name/type")
        };
        var rows = ProjectChangePresentation.Filter(input, "All", "");
        Assert.Equal(new[] { "Added", "Removed", "Changed", "Review identity" },
            rows.Select(r => r.ChangeLabel));
        Assert.Equal("— (not supplied)", rows[0].BeforeText);
        Assert.Equal("— (not supplied)", rows[1].AfterText);
        Assert.Single(ProjectChangePresentation.Filter(input, "Track", "bass"));
        Assert.Single(ProjectChangePresentation.Filter(input, "Ambiguous", ""));
        Assert.Empty(ProjectChangePresentation.Filter(input, "Plugin", ""));
    }

    [Fact]
    public void ZeroCountsRemainZeroAndAreNotConflatedWithMissingValues()
    {
        var row = ProjectChangePresentation.Describe(
            new ProjectChange("Plugin", "Synth", "Instance count", "0", "1", "Confirmed"));
        Assert.Equal("0", row.BeforeText);
        Assert.Equal("1", row.AfterText);
    }
}
