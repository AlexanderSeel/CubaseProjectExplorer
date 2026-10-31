using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class MidiPianoRollPreviewTests
{
    [Fact]
    public void ProjectsFlatNoteEventsWithExplicitBeatTimebase()
    {
        var project = Project([new ProjectMidiNote("midi", 1.5, 0.5, 0, 60, 0.8, null, "beats")]);

        var result = MidiPianoRollPreviewer.Build(project, "midi");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("beats", result.Value!.TimeUnit);
        Assert.Equal(1.5, result.Value.Start);
        Assert.Equal(2.0, result.Value.End);
        Assert.Equal(60, result.Value.LowestKey);
        Assert.Equal(60, result.Value.HighestKey);
        Assert.Single(result.Value.VisibleNotes);
    }

    [Fact]
    public void DoesNotFlattenClipNotesOrGuessMissingTimebase()
    {
        var nested = Project([new ProjectMidiNote("midi", 0, 1, 0, 60, null, null, "beats", true)]);
        var unknown = Project([new ProjectMidiNote("midi", 0, 1, 0, 60, null, null)]);

        Assert.Equal("MIDI_ROLL_CLIP_NESTED", MidiPianoRollPreviewer.Build(nested, "midi").Error?.Code);
        Assert.Equal("MIDI_ROLL_TIMEBASE_UNKNOWN", MidiPianoRollPreviewer.Build(unknown, "midi").Error?.Code);
    }

    [Fact]
    public void DecimatesLargeTracksToBoundedEvenlySpacedView()
    {
        var notes = Enumerable.Range(0, MidiPianoRollPreviewer.MaximumVisibleNotes + 17)
            .Select(index => new ProjectMidiNote("midi", index, 0.5, 0, index % 128, 0.5, null, "seconds"))
            .ToArray();

        var preview = MidiPianoRollPreviewer.Build(Project(notes), "midi").Value!;

        Assert.Equal(notes.Length, preview.TotalNoteCount);
        Assert.Equal(MidiPianoRollPreviewer.MaximumVisibleNotes, preview.VisibleNotes.Count);
        Assert.True(preview.IsDecimated);
    }

    private static ProjectSnapshot Project(IReadOnlyList<ProjectMidiNote> notes) =>
        new("C:/session.dawproject", "session", "DAWproject", "15", 120, EvidenceLevel.Confirmed,
            [new TrackInfo("midi", "MIDI", "MIDI", "channel", null, null, null, null)], [], [], [], MidiNotes: notes);
}
