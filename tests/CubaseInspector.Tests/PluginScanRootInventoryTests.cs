using CubaseInspector.Core;
using Xunit;
namespace CubaseInspector.Tests;
public sealed class PluginScanRootInventoryTests
{
    [Fact]
    public void RejectsUnqualifiedRegistryPath()
    {
        Assert.Empty(PluginScanRootInventory.Merge([("relative/path", "Registry")]));
    }

    [Fact]
    public void LinkedScanRootIsListedButNotScanned()
    {
        var temp = Directory.CreateTempSubdirectory("scan-root-links-");
        try
        {
            var link = Path.Combine(temp.FullName, "link");
            try { Directory.CreateSymbolicLink(link, temp.FullName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                NotSupportedException or System.Security.SecurityException)
            { return; } // Symlink creation is privilege-restricted in some environments.
            var info = Assert.Single(PluginScanRootInventory.Merge([(link, "User")]));
            Assert.True(info.Exists);
            Assert.True(info.Linked);
            Assert.False(info.IsScannable);
            Assert.Contains("skipped", info.Display);
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public void KeepsMultipleRootSourcesWithoutScanningMissingDirectories()
    {
        var folder = Directory.CreateTempSubdirectory("scan-roots-");
        try
        {
            var roots = PluginScanRootInventory.Merge([
                (folder.FullName, "Default"), (folder.FullName, "User folder"),
                (Path.Combine(folder.FullName, "missing"), "Registry")
            ]);
            Assert.Equal(2, roots.Count);
            var found = Assert.Single(roots, x => x.Exists);
            Assert.True(found.IsScannable);
            Assert.Contains("Default", found.Source);
            Assert.Contains("User folder", found.Source);
            Assert.Contains(roots, x => !x.Exists && x.Display.Contains("skipped"));
        }
        finally { folder.Delete(true); }
    }
}
