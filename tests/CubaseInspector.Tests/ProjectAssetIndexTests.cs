using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectAssetIndexTests
{
    [Fact]
    public void IndexesMediaUsageByTrackAndSummarizesMIDITracksForStructuredExports()
    {
        var project = new ProjectSnapshot("C:/Sessions/song.dawproject", "song", "DAWproject", "15", 120,
            EvidenceLevel.Confirmed,
            [new TrackInfo("audio-1", "Drums", "Audio", null, null, null, null, null),
             new TrackInfo("midi-1", "Bass MIDI", "Instrument/MIDI", null, null, null, null, null)],
            [], [], [], MidiNotes:
            [new ProjectMidiNote("midi-1", 0, 1, 1, 36, .8, .5, "beats"),
             new ProjectMidiNote("midi-1", 1, .5, 1, 48, .7, null, "beats")],
            MediaReferences: [new ProjectMediaReference("Samples/kick.wav", false, 2, 48000, 1.5, "audio-1")]);

        var index = ProjectAssetIndexer.Build(project);

        var media = Assert.Single(index.MediaUses);
        Assert.Equal("WAV", media.FileType);
        Assert.Equal("audio-1", media.TrackId);
        Assert.Equal("Drums", media.TrackName);
        Assert.False(media.External);
        var midi = Assert.Single(index.MidiTracks);
        Assert.Equal("Bass MIDI", midi.Name);
        Assert.Equal(2, midi.NoteCount);
        Assert.Equal((36, 48), (midi.LowestKey, midi.HighestKey));
        Assert.True(midi.CanExportStandardMidi);
    }

    [Fact]
    public void DoesNotPromoteCandidateNativeMarkersToMediaOrMIDIIndexes()
    {
        var project = new ProjectSnapshot("C:/Sessions/song.cpr", "song", "CPR", null, null,
            EvidenceLevel.Candidate, [new TrackInfo("candidate", "Possible MIDI", "MIDI", null, null, null, null, null,
                EvidenceLevel.Candidate)], [], [], [],
            MediaReferences: [new ProjectMediaReference("sample.wav", true, null, null, null)],
            MidiNotes: [new ProjectMidiNote("candidate", 0, 1, 0, 60, 1, 0, "beats")],
            NativeMidiNoteCandidates: [new CprRif2MidiNoteCandidate(0x120, 240, 120, 60, 97)],
            NativeAudioMediaCandidates: [new CprAudioMediaReferenceCandidate("Audio/kick.wav", 0x300,
                "Drums", 0x120, "MAudioEvent")]);

        var index = ProjectAssetIndexer.Build(project);

        Assert.Empty(index.MediaUses);
        Assert.Empty(index.MidiTracks);
        var candidate = Assert.Single(index.NativeMidiNoteCandidates!);
        Assert.Equal(EvidenceLevel.Candidate, candidate.Evidence);
        Assert.Equal(0x120, candidate.FileOffset);
        Assert.Contains("ownership", candidate.Warning, StringComparison.OrdinalIgnoreCase);
        var sampleCandidate = Assert.Single(index.NativeMediaReferenceCandidates!);
        Assert.Equal("Audio/kick.wav", sampleCandidate.DeclaredPath);
        Assert.Equal("Drums", sampleCandidate.CandidateTrackName);
        Assert.Empty(index.MediaUses);
    }

    [Fact]
    public void BlocksMIDIExportWhenNotesAreNestedOrTempoIsAutomated()
    {
        var nested = ProjectAssetIndexer.Build(Project([
            new ProjectMidiNote("midi", 0, 1, 0, 60, .7, null, "beats", true)]));
        Assert.False(Assert.Single(nested.MidiTracks).CanExportStandardMidi);
        Assert.Contains("inside clips", Assert.Single(nested.MidiTracks).ExportLimitation);

        var automated = Project([new ProjectMidiNote("midi", 0, 1, 0, 60, .7, null, "beats")]) with
        { HasTempoAutomation = true };
        Assert.Contains("tempo automation", Assert.Single(ProjectAssetIndexer.Build(automated).MidiTracks).ExportLimitation);
    }

    private static ProjectSnapshot Project(IReadOnlyList<ProjectMidiNote> notes) =>
        new("C:/Sessions/song.dawproject", "song", "DAWproject", "15", 120, EvidenceLevel.Confirmed,
            [new TrackInfo("midi", "Piano", "MIDI", null, null, null, null, null)], [], [], [], MidiNotes: notes);
}
