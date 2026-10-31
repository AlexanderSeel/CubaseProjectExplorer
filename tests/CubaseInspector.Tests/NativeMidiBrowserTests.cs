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
        Assert.All(groups, group => Assert.Equal("Possible track: Keys", group.Context));
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
}
