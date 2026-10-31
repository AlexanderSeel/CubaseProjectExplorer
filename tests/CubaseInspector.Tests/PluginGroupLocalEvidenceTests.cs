using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;
public class PluginGroupLocalEvidenceTests
{
    [Fact]
    public void CountsExactFilenameAndUnknownReferencesSeparately()
    {
        var a = new PluginReference("ID", "Synth", "Vendor", "VST3", "t", EvidenceLevel.Confirmed);
        var b = a with { Evidence = EvidenceLevel.Candidate };
        var result = PluginGroupLocalEvidenceService.Summarize([
            new PluginCheck(a, PluginStatus.InstalledByClassId, "C:/vst/a.vst3"),
            new PluginCheck(a, PluginStatus.InstalledByName, "C:/vst/a.vst3"),
            new PluginCheck(a, PluginStatus.Unverified, null),
            // A candidate is never upgraded by even an incorrect external check.
            new PluginCheck(b, PluginStatus.InstalledByClassId, "C:/vst/a.vst3"),
            new PluginCheck(a, PluginStatus.InstalledByClassId, null)
        ]);
        Assert.Equal(1, result.ExactCidReferences);
        Assert.Equal(1, result.FilenameHintReferences);
        Assert.Equal(3, result.UnverifiedReferences);
        Assert.Equal(5, result.Total);
        Assert.Contains("Not a plugin loadability", result.Summary);
    }

    [Fact]
    public void EmptyGroupDoesNotClaimInstallation()
    {
        var result = PluginGroupLocalEvidenceService.Summarize([]);
        Assert.Equal(0, result.Total);
        Assert.Contains("no displayed references", result.Summary);
    }
}
