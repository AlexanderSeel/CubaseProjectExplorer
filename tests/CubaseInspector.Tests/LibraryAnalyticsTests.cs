using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LibraryAnalyticsTests
{
    private static ProjectLibraryEntry Entry(string path, EvidenceLevel level, double? bpm,
        long? size, DateTimeOffset? modified, TrackInfo[] tracks, PluginReference[] plugins)
    {
        var p = new ProjectSnapshot(path, Path.GetFileNameWithoutExtension(path), "Any",
            null, bpm, level, tracks, plugins, [], []);
        return new ProjectLibraryEntry(p, size, modified);
    }

    [Fact]
    public void MixedExportsKeepConfirmedAndCandidateMetricsSeparate()
    {
        var time = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        var confirmed = Entry("mix.dawproject", EvidenceLevel.Confirmed, 128, 2048, time,
            [new TrackInfo("one", "Bass", "Audio", null, null, null, null, null)],
            [new PluginReference("id", "Compressor", "ACME", "VST3", "one", EvidenceLevel.Confirmed),
             new PluginReference("candidate", "Possible", null, "Binary", null, EvidenceLevel.Candidate)]);
        var candidate = Entry("backup.bak", EvidenceLevel.Candidate, null, null, null,
            [new TrackInfo("hint", "Track?", "Audio", null, null, null, null, null, EvidenceLevel.Candidate)],
            [new PluginReference("marker", "Maybe", null, "Unknown", null, EvidenceLevel.Candidate)]);
        var unknown = Entry("other.cpr", EvidenceLevel.Unknown, null, 0, null, [], []);
        var stats = LibraryAnalyticsService.Summarize([confirmed, candidate, unknown]);

        Assert.Equal(3, stats.Readable);
        Assert.Equal(1, stats.Confirmed);
        Assert.Equal(1, stats.Candidate);
        Assert.Equal(1, stats.UnknownEvidence);
        Assert.Equal(2, stats.UnknownTempo);
        Assert.Equal(1, stats.ConfirmedTracks);
        Assert.Equal(1, stats.CandidateTracks);
        Assert.Equal(1, stats.ConfirmedPluginReferences);
        Assert.Equal(2, stats.UnverifiedPluginMarkers);
        Assert.Equal(2, stats.FilesWithKnownSize);
        Assert.Equal(2048, stats.KnownSizeBytes);
        Assert.Equal(time, stats.LatestProjectFileModifiedUtc);
        Assert.Equal(3, stats.Formats.Count);
        Assert.Equal(100, stats.Formats.Sum(b => b.Percentage), 5);
        Assert.Single(stats.ConfirmedPluginVendors);
        Assert.Equal("ACME", stats.ConfirmedPluginVendors[0].Label);
    }

    [Fact]
    public void NoReadableProjectsHaveNoFalseZeroPercentages()
    {
        var summary = LibraryAnalyticsService.Summarize([]);
        Assert.Empty(summary.Formats);
        Assert.Empty(summary.Evidence);
        Assert.Empty(summary.ConfirmedPluginVendors);
        Assert.Null(summary.LatestProjectFileModifiedUtc);
        Assert.Equal(0, summary.Readable);
    }

    [Fact]
    public void CandidateOnlyLibraryCannotInventManufacturersOrConfirmedTracks()
    {
        var p = Entry("crash.cpr", EvidenceLevel.Candidate, null, null, null,
            [new TrackInfo("hint", "Audio?", "Audio", null, null, null, null, null, EvidenceLevel.Candidate)],
            [new PluginReference("m", "Marker", "FakeVendor", "Binary", null, EvidenceLevel.Candidate)]);
        var stats = LibraryAnalyticsService.Summarize([p]);
        Assert.Empty(stats.ConfirmedPluginVendors);
        Assert.Equal(0, stats.ConfirmedPluginReferences);
        Assert.Equal(0, stats.ConfirmedTracks);
        Assert.Equal(1, stats.CandidateTracks);
    }
}
