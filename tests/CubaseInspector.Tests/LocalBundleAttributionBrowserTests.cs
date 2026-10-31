using CubaseInspector.Core;
using Xunit;
namespace CubaseInspector.Tests;
public sealed class LocalBundleAttributionBrowserTests
{
    [Fact]
    public void FiltersBundlesAndAttributionsTogetherByProjectOrPlugin()
    {
        var a = new LocalBundleFootprint("Bundle A.vst3", 500, 1, true, null, 2);
        var b = new LocalBundleFootprint("Bundle B.vst3", 0, 0, false, "Access denied", 1);
        var report = new LocalPluginFootprintReport([a, b], 500, 3, 0, false, "Read-only");
        var rows = new[] {
            new LocalBundleAttribution(a, [new("Score.dawproject", "Compressor",
                "0123456789ABCDEF0123456789ABCDEF", 2)]),
            new LocalBundleAttribution(b, [new("Mix.dawproject", "EQ",
                "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 1)])
        };
        var filtered = LocalBundleAttributionBrowser.Filter(report, rows,
            "compressor", "Largest", false);
        Assert.Same(a, Assert.Single(filtered).Bundle);
        Assert.Same(a, Assert.Single(LocalBundleAttributionBrowser.Filter(
            report, rows, "Score.dawproject", "Path", false)).Bundle);
        Assert.Same(b, Assert.Single(LocalBundleAttributionBrowser.Filter(
            report, rows, "Access denied", "Path", true)).Bundle);
        Assert.Empty(LocalBundleAttributionBrowser.Filter(report, rows,
            "Score.dawproject", "Path", true));
        Assert.Equal([a.Path, b.Path], LocalBundleAttributionBrowser.Filter(
            report, rows, "", "Largest", false).Select(row => row.Bundle.Path).ToArray());
    }

    [Fact]
    public void MissingAttributionKeepsBundleVisible()
    {
        var b = new LocalBundleFootprint("No references.vst3", 5, 1, true, null, 0);
        var report = new LocalPluginFootprintReport([b], 5, 0, 0, true, "");
        var row = Assert.Single(LocalBundleAttributionBrowser.Filter(
            report, [], "", "Path", false));
        Assert.Empty(row.Uses);
        Assert.Equal(b.Path, row.Bundle.Path);
    }
}
