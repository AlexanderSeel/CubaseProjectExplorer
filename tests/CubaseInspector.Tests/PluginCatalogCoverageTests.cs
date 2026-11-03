using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;
public class PluginCatalogCoverageTests
{
    private static PluginMetadataEntry Example => new("Q", "Maker", "Effect", null,
        null, null, null, null, null, [], [], [], "PluginDB CC0 catalog");

    [Fact]
    public void DistinguishesNameHintReviewedRejectedStaleAndMissing()
    {
        var entry = Example;
        var reviewed = new PluginCatalogDecision("key", "Q", "Maker", entry.Source, false);
        var rejected = reviewed with { Rejected = true };
        Assert.Equal(PluginCatalogCoverage.NoCatalog, PluginCatalogCoverage.State(false, null, null));
        Assert.Equal(PluginCatalogCoverage.Missing, PluginCatalogCoverage.State(true, null, null));
        Assert.Equal(PluginCatalogCoverage.NameHint, PluginCatalogCoverage.State(true, null, entry));
        Assert.Equal(PluginCatalogCoverage.Reviewed, PluginCatalogCoverage.State(true, reviewed, entry));
        Assert.Equal(PluginCatalogCoverage.Stale, PluginCatalogCoverage.State(true, reviewed, null));
        Assert.Equal(PluginCatalogCoverage.Rejected, PluginCatalogCoverage.State(true, rejected, null));
        Assert.True(PluginCatalogCoverage.Matches("Needs review", PluginCatalogCoverage.Stale));
        Assert.False(PluginCatalogCoverage.Matches("Needs review", PluginCatalogCoverage.Reviewed));
    }

    [Fact]
    public void CsvEscapesSpreadsheetTextAndDoesNotImplyVerifiedPluginIdentity()
    {
        var csv = PluginCatalogCoverage.Csv([
            new("=Cmd", "-Vendor", "CPR UID", "Candidate", 5, "Name hint",
                "Synth", "Maker", "PluginDB")
        ]);
        Assert.Contains("\"'=Cmd\"", csv);
        Assert.Contains("\"'-Vendor\"", csv);
        Assert.Contains(",5,", csv);
        Assert.Contains("ProjectEvidence", csv);
    }
}
