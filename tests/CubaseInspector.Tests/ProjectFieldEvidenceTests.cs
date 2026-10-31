using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectFieldEvidenceTests
{
    [Fact]
    public void ConfirmedZeroAndFalseRemainPresentValues()
    {
        var volume = ProjectFieldEvidence<double>.Confirmed(0, "DAWproject channel volume");
        var mute = ProjectFieldEvidence<bool>.Confirmed(false, "DAWproject mute");
        Assert.True(volume.HasValue);
        Assert.True(volume.IsConfirmedValue);
        Assert.True(mute.HasValue);
        Assert.True(mute.IsConfirmedValue);
        Assert.Equal(0, volume.Value);
        Assert.False(mute.Value);
    }

    [Theory]
    [InlineData(ProjectFieldState.Absent)]
    [InlineData(ProjectFieldState.NotExported)]
    [InlineData(ProjectFieldState.Unknown)]
    [InlineData(ProjectFieldState.Malformed)]
    public void UnavailableNeverMasqueradesAsARealZero(ProjectFieldState state)
    {
        var value = ProjectFieldEvidence<double>.Unavailable(state, "No decoded field");
        Assert.False(value.HasValue);
        Assert.False(value.IsConfirmedValue);
        Assert.Equal(EvidenceLevel.Unknown, value.Evidence);
    }

    [Fact]
    public void CandidateReportExplicitlyDisclosesUndecodedFields()
    {
        var project = new ProjectSnapshot(
            "example.cpr", "Example", "CPR", null, null, EvidenceLevel.Candidate,
            [], [], [], ["Native reader is candidate-only"]);
        var report = new ReportService().ToMarkdown(project);
        Assert.Contains("Field availability", report);
        Assert.Contains("not decoded", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tempo: Unknown", report);
        Assert.DoesNotContain("BPM: 0", report);
    }

    [Fact]
    public void CoverageDoesNotInventNativeMixerRoutingOrInstances()
    {
        var native = new ProjectSnapshot("test.cpr", "Test", "CPR", null, null,
            EvidenceLevel.Candidate, [], [], [], []);
        var coverage = ProjectFieldCoverage.Evaluate(native);
        Assert.Equal(ProjectFieldState.Unknown, coverage["Tempo"]);
        Assert.Equal(ProjectFieldState.Unknown, coverage["Routing"]);
        Assert.Equal(ProjectFieldState.Unknown, coverage["Insert slot"]);
        Assert.Equal(ProjectFieldState.Unknown, coverage["Plugin references"]);
    }

    [Fact]
    public void ExportedFalseAndZeroCountAsPresent()
    {
        var track = new TrackInfo("t1", "Audio", "Audio", null, null, 0, false, false);
        var exported = new ProjectSnapshot("example.dawproject", "Test", "DAWproject",
            "1", 120, EvidenceLevel.Confirmed, [track], [], [], []);
        var coverage = ProjectFieldCoverage.Evaluate(exported);
        Assert.Equal(ProjectFieldState.Present, coverage["Tempo"]);
        Assert.Equal(ProjectFieldState.Present, coverage["Mixer volume"]);
        Assert.Equal(ProjectFieldState.Present, coverage["Mute / solo"]);
        Assert.Equal(ProjectFieldState.NotExported, coverage["Insert slot"]);
    }

    [Fact]
    public void CompactSummaryDoesNotCallCprTokensVerifiedPlugins()
    {
        var candidate = new ProjectSnapshot("song.cpr", "Song", "CPR", null, null,
            EvidenceLevel.Candidate, [], [], [], []);
        var summary = new ReportService().ShortSummary(candidate);
        Assert.Contains("stored plugin UID/name markers", summary);
        Assert.Contains("routing: not decoded", summary);
        Assert.DoesNotContain("exported plugin refs", summary);
    }

    [Fact]
    public void CandidateWithValueIsNotConfirmed()
    {
        var field = ProjectFieldEvidence<int>.Unverified(42, "CPR candidate token", 16384);
        Assert.Equal(ProjectFieldState.Candidate, field.State);
        Assert.Equal(16384, field.SourceOffset);
        Assert.True(field.HasValue);
        Assert.False(field.IsConfirmedValue);
        Assert.Throws<ArgumentException>(() =>
            ProjectFieldEvidence<int>.Unavailable(ProjectFieldState.Present, "invalid"));
    }

    [Fact]
    public void RecognizedTrackClassIsShownSeparatelyFromUnverifiedRelationships()
    {
        var track = new TrackInfo("r1", "Bass", "Audio", null, null, null, null, null,
            EvidenceLevel.Candidate, "MAudioTrackEvent", 1234);

        Assert.Contains("Audio information found", EvidencePresentation.TrackLabel(track));
        Assert.Contains("MAudioTrackEvent", EvidencePresentation.TrackExplanation(track));
        Assert.Contains("parent hierarchy", EvidencePresentation.TrackExplanation(track));
        Assert.DoesNotContain("Unverified CPR record", EvidencePresentation.TrackLabel(track));
    }

    [Fact]
    public void StoredPluginFieldsAreShownAsFoundWithoutClaimingIdentityOrOwner()
    {
        var plugin = new PluginReference("uid", "Compressor", null, "CPR UID (format unverified)",
            null, EvidenceLevel.Candidate, Role: "Cubase Plugin UID and typed Plugin Name fields at 0x1234");

        Assert.Contains("Plug-in info found", EvidencePresentation.PluginLabel(plugin));
        Assert.Contains("may need review", EvidencePresentation.PluginLabel(plugin));
        Assert.Contains("APluginSlot", EvidencePresentation.PluginExplanation(plugin));
        Assert.DoesNotContain("Unverified CPR record", EvidencePresentation.PluginLabel(plugin));
    }
}
