using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public class LocalPluginFootprintExportTests
{
    [Fact]
    public void ExportsOnlyCompleteBytesAndEscapesUntrustedData()
    {
        var report = new LocalPluginFootprintReport([
            new LocalBundleFootprint("=synth|bundle\\x.vst3", 128, 2, true, null, 3),
            new LocalBundleFootprint("unfinished.vst3", 42, 1, false, "Access denied", 1)
        ], 128, 4, 2, false, "Read-only measurement; not reclaimable space.");
        var csv = LocalPluginFootprintExport.Csv(report);
        Assert.Contains("'=synth|bundle", csv);
        Assert.Contains(",128,2,true,", csv);
        Assert.Contains("\"unfinished.vst3\",,1,false", csv);
        Assert.DoesNotContain("unfinished.vst3\",42,", csv);
        var md = LocalPluginFootprintExport.Markdown(report);
        Assert.Contains("=synth\\|bundle", md);
        Assert.Contains("Unknown (incomplete)", md);
        Assert.Contains("not reclaimable", md);
    }
}
