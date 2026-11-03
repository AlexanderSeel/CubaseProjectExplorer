using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class StudioIoPreflightTests
{
    [Fact]
    public void VerifiedLogicalDestinationsAreShownWithoutPhysicalInstallationClaims()
    {
        var p = new ProjectSnapshot("project.dawproject", "Studio", "DAWproject",
            null, null, EvidenceLevel.Confirmed,
            [new TrackInfo("a", "Bass", "Audio", "bass", "bus", null, null, null),
             new TrackInfo("b", "Group", "Group", "bus", null, null, null, null),
             new TrackInfo("c", "Keys", "Instrument", "keys", "external", null, null, null)],
            [], [], []);
        var report = StudioIoPreflightService.Analyze(p);
        Assert.Equal(3, report.LogicalRoutes.Count);
        Assert.True(report.LogicalRoutes.Single(r=>r.TrackName=="Bass").DestinationInParsedChannels);
        Assert.False(report.LogicalRoutes.Single(r=>r.TrackName=="Keys").DestinationInParsedChannels);
        Assert.Contains("Unknown", report.PhysicalAudioStatus);
        Assert.Contains("Unknown", report.PhysicalMidiStatus);
        Assert.Single(report.MidiOrInstrumentTracks);
        Assert.Contains("NOT physical", StudioIoPreflightService.Markdown(report));
    }

    [Fact]
    public void CprCandidateIOMappingsRemainEmptyAndUnavailable()
    {
        var p = new ProjectSnapshot("crash.bak", "Crash", "CPR",
            null, null, EvidenceLevel.Candidate,
            [new TrackInfo("a", "Track 1", "Audio", "mystery", null,
                null, null, null, EvidenceLevel.Candidate)], [], [], []);
        var report = StudioIoPreflightService.Analyze(p);
        Assert.Empty(report.LogicalRoutes);
        Assert.Empty(report.MidiOrInstrumentTracks);
        Assert.Contains("cannot establish", string.Join(" ",report.Warnings));
    }
}
