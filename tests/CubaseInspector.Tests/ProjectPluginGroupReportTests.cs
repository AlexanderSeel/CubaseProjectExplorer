using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;
public sealed class ProjectPluginGroupReportTests
{
    [Fact]
    public void CandidateExportRetainsTokenOffsetsWithoutClaimingInstances()
    {
        var plugin = new PluginReference("ABC", "=Synth", "Maker", "CPR UID", null,
            EvidenceLevel.Candidate, SourceByteOffset: 123);
        var group = new ProjectPluginGroup("Candidate|CPR|ABC", "=Synth", "Maker",
            "CPR UID", EvidenceLevel.Candidate,
            [new ProjectPluginUse(plugin, "Candidate token at byte 123")]);
        var csv = ProjectPluginGroupReport.Csv([group]);
        Assert.Contains("\"'=Synth\"", csv);
        Assert.Contains(",123,", csv);
        var md = ProjectPluginGroupReport.Markdown([group], "Demo");
        Assert.Contains("NOT authenticated insert instances", md);
        Assert.Contains("candidate offset 123", md);
    }

    [Fact]
    public void EmptyReportIsExplicit()
    {
        Assert.Contains("Visible groups: 0", ProjectPluginGroupReport.Markdown([]));
        Assert.Single(ProjectPluginGroupReport.Csv([]).Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
