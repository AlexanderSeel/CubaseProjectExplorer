using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprPluginDiagnosticTests
{
    private static PluginReference Record(string id, string name, long offset) =>
        new(id, name, null, "CPR UID (format unverified)", null,
            EvidenceLevel.Candidate, Role: "source at 0x" + offset.ToString("X"),
            SourceByteOffset: offset);

    [Fact]
    public void FocusedReportMaintainsRawOffsetsAndCoverageStatus()
    {
        var data = new CprPluginStreamResult([
            Record("56535473796C3173796C656E74683100", "Sylenth1", 35000000),
            Record("297BA567D83144E1AE921DEF07B41156", "Name not decoded", 420),
            Record("offset-7FA", "Unresolved plugin UID", 2042)
        ], 33_554_432, true, false, CprPluginStreamScanner.DefaultMaxBytes);
        var report = CprPluginDiagnosticService.Build(data, 75_000_000, @"C:\Project\session.cpr");
        Assert.Equal(1, report.NamedUidTokens);
        Assert.Equal(1, report.UidOnlyTokens);
        Assert.Equal(1, report.UnresolvedMarkers);
        Assert.True(report.Partial);
        Assert.Equal(35000000L, report.Records[0].ByteOffset);
        var markdown = CprPluginDiagnosticService.Markdown(report);
        Assert.Contains("Complete scan: False", markdown);
        Assert.Contains("35000000", markdown);
        Assert.Contains("not validated", markdown);
    }

    [Fact]
    public void UnresolvedOnlyExportRetainsGlobalCountsAndEscapesCsvFields()
    {
        var data = new CprPluginStreamResult([
            Record("56535473796C3173796C656E74683100", "Sylenth1", 20),
            Record("297BA567D83144E1AE921DEF07B41156", "Name not decoded", 80),
            Record("offset-A1", "=DANGEROUS", 161)
        ], 500, false, false, CprPluginStreamScanner.DefaultMaxBytes);
        var report = CprPluginDiagnosticService.Build(data, 500, "song.cpr", true);
        Assert.Equal(1, report.NamedUidTokens);
        Assert.Equal(2, report.Records.Count);
        var csv = CprPluginDiagnosticService.Csv(report);
        Assert.DoesNotContain("Sylenth1", csv);
        Assert.Contains("'=DANGEROUS", csv);
        Assert.Contains("Name not decoded", csv);
    }
}
