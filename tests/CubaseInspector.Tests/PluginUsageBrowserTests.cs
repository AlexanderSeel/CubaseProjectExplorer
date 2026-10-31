using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class PluginUsageBrowserTests
{
    private static PluginUsage Usage(string name, string? vendor, int projects, int instances,
        DateTimeOffset? modified = null, string format = "VST3") =>
        new(name, "id-" + name, format, vendor, projects, instances, modified,
            Enumerable.Range(0, projects).Select(i => "project-" + i + "/" + name + ".dawproject").ToArray());

    [Fact]
    public void FiltersAndRanksByExplicitIndexedReferenceCount()
    {
        var source = new[] { Usage("A", "Vendor", 1, 4), Usage("B", "Vendor", 3, 3),
            Usage("C", null, 2, 8) };
        Assert.Equal(new[] { "B", "C", "A" },
            PluginUsageBrowser.Filter(source, new PluginUsageQuery()).Select(p => p.Name));
        Assert.Equal(new[] { "A", "C", "B" },
            PluginUsageBrowser.Filter(source, new PluginUsageQuery(Sort: "Fewest projects"))
                .Select(p => p.Name));
        Assert.Equal("C", PluginUsageBrowser.Filter(source,
            new PluginUsageQuery(Sort: "Most instances"))[0].Name);
        Assert.Equal("A", Assert.Single(PluginUsageBrowser.Filter(source,
            new PluginUsageQuery(SingleProjectOnly: true))).Name);
    }

    [Fact]
    public void SearchMatchesNameExportIdVendorAndScopedProjectPaths()
    {
        var source = new[] { Usage("EQ", null, 1, 1), Usage("Limiter", "Audio Company", 2, 2) };
        Assert.Single(PluginUsageBrowser.Filter(source, new PluginUsageQuery(Search: "id-EQ")));
        Assert.Single(PluginUsageBrowser.Filter(source, new PluginUsageQuery(Search: "company")));
        Assert.Single(PluginUsageBrowser.Filter(source, new PluginUsageQuery(Search: "/limiter")));
        Assert.Empty(PluginUsageBrowser.Filter(source, new PluginUsageQuery(Search: "Missing")));
    }

    [Fact]
    public void UnknownVendorAndMissingDateAreNotConflatedWithKnownOrUnused()
    {
        var t = new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);
        var source = new[] { Usage("Unknown", null, 1, 1), Usage("Recent", "Maker", 2, 3, t) };
        Assert.Equal(new[] { "All", "Maker", "Unknown" }, PluginUsageBrowser.Vendors(source));
        Assert.Equal(new[] { "All", "VST3" }, PluginUsageBrowser.Formats(source));
        Assert.Equal("Unknown", Assert.Single(PluginUsageBrowser.Filter(source,
            new PluginUsageQuery(Vendor: "Unknown"))).Name);
        Assert.Equal("Recent", PluginUsageBrowser.Filter(source,
            new PluginUsageQuery(Sort: "Latest project file modified"))[0].Name);
    }
}
