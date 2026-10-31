using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class NativeMidiBrowserTests
{
    private static IndexedMidiNoteCandidate Note(long partOffset, string project = "one.cpr", int count = 1) =>
        new(project, "Project", count, 100 + count, 120.125, 131.92000000000007, 36, 0,
            EvidenceLevel.Candidate, "Unverified", "RIFF packed MMidiPart", "Keys", "Same name", 5, partOffset);

    [Fact]
    public void SameNamedPartsAndDifferentProjectsRemainDistinct()
    {
        var groups = NativeMidiBrowser.Group([Note(10), Note(20), Note(10, "two.cpr"), Note(10, count: 2)]);
        Assert.Equal(3, groups.Count);
        Assert.Equal(4, groups.Sum(group => group.NoteCount));
        Assert.Contains(groups, group => group.NoteCount == 2);
        Assert.All(groups, group => Assert.Equal("Track label: Keys", group.Context));
    }

    [Fact]
    public void PreviewPreservesRawValuesAndDisclosesUnknownUnits()
    {
        var group = Assert.Single(NativeMidiBrowser.Group([Note(10)]));
        var preview = NativeMidiBrowser.Preview(group)!;
        Assert.Contains("units unverified", preview.TimeUnit);
        Assert.Equal(120.125, preview.Start);
        Assert.Equal(131.92000000000007, Assert.Single(preview.VisibleNotes).Duration);
        Assert.Equal(0, preview.VisibleNotes[0].Velocity);
        Assert.DoesNotContain("000000000", NativeMidiNoteGroup.Format(preview.VisibleNotes[0].Duration));
    }

    [Fact]
    public void MixedEncodingsWithinOneCandidatePartStayInOneGroup()
    {
        var group = Assert.Single(NativeMidiBrowser.Group([Note(10), Note(10, count: 2) with
            { SourceLayout = "RIFF MMidiNote/typed-attributes" }]));
        Assert.Equal(2, group.NoteCount);
        Assert.Contains("RIFF packed MMidiPart", group.SourceLayout);
        Assert.Contains("typed-attributes", group.SourceLayout);
    }

    [Fact]
    public void PartsWithSharedEnclosingTrackOffsetArePresentedAsOneTrackGroup()
    {
        var parts = NativeMidiBrowser.Group([Note(10), Note(20), Note(30) with { CandidateTrackOffset = 9 }]);
        var tracks = NativeMidiBrowser.GroupByPossibleTrack(parts);

        Assert.Equal(2, tracks.Count);
        var sharedTrack = Assert.Single(tracks, track => track.CandidateTrackOffset == 5);
        Assert.Equal(2, sharedTrack.PartCount);
        Assert.Equal(2, sharedTrack.NoteCount);
        Assert.Contains("one saved track record", sharedTrack.Context);
        Assert.Single(Assert.Single(tracks, track => track.CandidateTrackOffset == 9).Parts);
    }

    [Fact]
    public void MissingTrackOffsetsStayInAnExplicitUnassignedGroup()
    {
        var parts = NativeMidiBrowser.Group([Note(10) with { CandidateTrackOffset = null }, Note(20) with { CandidateTrackOffset = null }]);
        var track = Assert.Single(NativeMidiBrowser.GroupByPossibleTrack(parts));

        Assert.Null(track.CandidateTrackOffset);
        Assert.Equal(2, track.PartCount);
        Assert.Equal("Unassigned MIDI parts", track.DisplayName);
        Assert.Contains("No track link", track.Context);
    }

    [Fact]
    public void TrackRecordWithoutReadableNameDoesNotExposeByteOffsetInBrowserLabel()
    {
        var parts = NativeMidiBrowser.Group([Note(10) with { CandidateTrackName = null }]);
        var track = Assert.Single(NativeMidiBrowser.GroupByPossibleTrack(parts));

        Assert.Equal("Unnamed track group", track.DisplayName);
        Assert.Equal("Track label not found", Assert.Single(track.Parts).Context);
        Assert.DoesNotContain("0x", track.DisplayName);
    }
}
