using CubaseInspector.Core;
using Xunit;
namespace CubaseInspector.Tests;
public sealed class LocalBundleFootprintBrowserTests
{
    private static readonly LocalPluginFootprintReport Example = new([
        new("C:/Plugins/Alpha.vst3", 90, 1, true, null, 2),
        new("C:/Plugins/Beta.vst3", 500, 3, true, null, 1),
        new("C:/Plugins/Gamma.vst3", 15, 1, false, "Access denied", 5)
    ], 590, 8, 0, false, "Read-only");

    [Fact]
    public void FiltersCaseInsensitiveWarningAndIncomplete()
    {
        var result = LocalBundleFootprintBrowser.Filter(Example, "DENIED", "Path", true);
        Assert.Single(result);
        Assert.EndsWith("Gamma.vst3", result[0].Path);
        Assert.Empty(LocalBundleFootprintBrowser.Filter(Example, "Beta", "Path", true));
    }

    [Fact]
    public void LargestPlacesUnknownSizeLastAndReferenceSortUsesCounts()
    {
        var bySize = LocalBundleFootprintBrowser.Filter(Example, null, "Largest", false);
        Assert.EndsWith("Beta.vst3", bySize[0].Path);
        Assert.False(bySize[^1].Complete);
        var byCount = LocalBundleFootprintBrowser.Filter(Example, "", "Most references", false);
        Assert.EndsWith("Gamma.vst3", byCount[0].Path);
    }
}
