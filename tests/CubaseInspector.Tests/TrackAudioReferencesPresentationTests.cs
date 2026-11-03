using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class TrackAudioReferencesPresentationTests
{
    [Fact]
    public void JoinsDeclaredReferencesByTrackIdAndNativePathsByOffsetWithNameOnlyFallback()
    {
        var track = new TrackInfo("track-1", "Guitar", "Audio", null, null, null, null, null,
            EvidenceLevel.Confirmed, SourceOffset: 100);
        var otherTrack = new TrackInfo("track-2", "Keys", "Instrument", null, null, null, null, null,
            EvidenceLevel.Confirmed, SourceOffset: 200);
        var project = new ProjectSnapshot("session.dawproject", "Session", "DAWproject", null, null,
            EvidenceLevel.Confirmed, [track, otherTrack], [], [], [],
            MediaReferences:
            [new("Audio/Guitar.wav", true, 2, 44100, 1.25, "track-1"),
             new("Audio/unassigned.flac", true, 2, 48000, null)],
            NativeAudioMediaCandidates:
            [new("Audio/native-offset.wav", 300, "Guitar", 100, "MAudioEvent"),
             new("Audio/native-name.wav", 400, "Guitar", null, "MAudioEvent"),
             new("Audio/native-other-track.wav", 500, "Keys", 200, "MAudioEvent"),
             new("Audio/native-orphan.wav", 600, null, null, "MAudioEvent")]);

        var result = TrackAudioReferencesPresentation.Build(project, track);

        Assert.Equal(3, result.Associated.Count);
        Assert.Contains(result.Associated, item => item.Path == "Audio/Guitar.wav" && item.Evidence == EvidenceLevel.Confirmed);
        Assert.Contains(result.Associated, item => item.Path == "Audio/native-offset.wav" && item.Association == "Exact enclosing track byte offset");
        Assert.Contains(result.Associated, item => item.Path == "Audio/native-name.wav" && item.Association.Contains("unverified"));
        Assert.Equal(2, result.Unassigned.Count);
        Assert.Contains(result.Unassigned, item => item.Path == "Audio/unassigned.flac");
        Assert.Contains(result.Unassigned, item => item.Path == "Audio/native-orphan.wav");
        Assert.DoesNotContain(result.Associated, item => item.Path == "Audio/native-other-track.wav");
    }

    [Fact]
    public void PreservesAStoredPathEvenWhenItsCurrentFileLocationIsUnknown()
    {
        var track = new TrackInfo("track-1", "Bass", "Audio", null, null, null, null, null);
        var expectedPath = "Z:/samples/not-installed/take-01.wav";
        var project = new ProjectSnapshot("session.cpr", "Session", "CPR", null, null,
            EvidenceLevel.Candidate, [track], [], [], [],
            NativeAudioMediaCandidates: [new(expectedPath, 120, "Bass", null, "MAudioEvent")]);

        var result = TrackAudioReferencesPresentation.Build(project, track);

        Assert.Equal(expectedPath, Assert.Single(result.Associated).Path);
    }

    [Fact]
    public void DuplicateTrackNamesDoNotAcquireNameOnlyPathOwnership()
    {
        var first = new TrackInfo("track-1", "Audio", "Audio", null, null, null, null, null);
        var second = new TrackInfo("track-2", "Audio", "Audio", null, null, null, null, null);
        var project = new ProjectSnapshot("session.cpr", "Session", "CPR", null, null,
            EvidenceLevel.Candidate, [first, second], [], [], [],
            NativeAudioMediaCandidates: [new("Audio/take.wav", 120, "Audio", null, "MAudioEvent")]);

        var result = TrackAudioReferencesPresentation.Build(project, first);

        Assert.Empty(result.Associated);
        Assert.Contains(result.Unassigned, item => item.Path == "Audio/take.wav" && item.Association == "Track association unavailable");
    }
}
