using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprIoObservationPresentationTests
{
    [Fact]
    public void DisplaysNativeObservationsWithSourceOffsetsAndDomainLabels()
    {
        var project = new ProjectSnapshot("session.cpr", "Session", "CPR candidate", "Cubase 12", null,
            EvidenceLevel.Candidate, [], [], [], [],
            NativeAudioMediaCandidates: [new CprAudioMediaReferenceCandidate("Audio/kick.wav", 0x123,
                "Drums", 0x40, "MAudioEvent")],
            NativeMixerObservations: [new CprMixerObservation("Drums", -3, null, false, null,
                0x40, "PArrangement", CandidateFields: [new("Volume/AnchorValue", "f64", "-3", 0x80)])],
            NativeMetadataCandidates: [new CprMetadataCandidate("Tempo event BPM", "120", 0x99,
                "typed f64", "Unverified candidate")]);

        var rows = CprIoObservationPresentation.Build(project);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, row => row.Contains("Audio path") && row.Contains("0x123"));
        Assert.Contains(rows, row => row.Contains("Mixer values") && row.Contains("-3 dB") && row.Contains("0x40"));
        Assert.Contains(rows, row => row.Contains("Project field") && row.Contains("0x99"));
    }

    [Fact]
    public void LimitsNativeObservationRowsAndDoesNotPromoteStructuredOrNonCprData()
    {
        var candidate = new ProjectSnapshot("session.bak", "Session", "CPR candidate", null, null,
            EvidenceLevel.Candidate, [], [], [], [],
            NativeAudioMediaCandidates: Enumerable.Range(0, 5)
                .Select(index => new CprAudioMediaReferenceCandidate($"audio-{index}.wav", index,
                    null, null, "MAudioEvent")).ToArray());
        var confirmed = candidate with { FilePath = "session.dawproject", Evidence = EvidenceLevel.Confirmed };
        var candidateOtherFormat = candidate with { FilePath = "session.bin" };

        Assert.Equal(2, CprIoObservationPresentation.Build(candidate, 2).Count);
        Assert.Empty(CprIoObservationPresentation.Build(confirmed));
        Assert.Empty(CprIoObservationPresentation.Build(candidateOtherFormat));
    }
}
