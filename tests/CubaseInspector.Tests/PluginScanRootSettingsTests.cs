using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;
public class PluginScanRootSettingsTests
{
    [Fact]
    public void SavesAndReadsDeduplicatedFolder()
    {
        var folder = Directory.CreateTempSubdirectory("vst-scan-test-");
        var config = Path.Combine(folder.FullName, "roots.json");
        try
        {
            PluginScanRootSettings.Save([folder.FullName, folder.FullName], config);
            Assert.Single(PluginScanRootSettings.Load(config));
            Assert.True(PluginScanRootSettings.Read(config).IsWritable);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void RetainsTemporarilyMissingFoldersForEditingAndFutureScans()
    {
        var root = Directory.CreateTempSubdirectory("vst-offline-");
        var config = Path.Combine(root.FullName, "settings.json");
        var offline = Path.Combine(root.FullName, "drive-unavailable");
        try
        {
            PluginScanRootSettings.Save([offline], config);
            var loaded = PluginScanRootSettings.Read(config);
            Assert.True(loaded.IsWritable);
            Assert.Equal(offline, Assert.Single(loaded.Folders));
            Assert.False(Directory.Exists(offline));
        }
        finally { root.Delete(true); }
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("{\"version\":99,\"folders\":[]}")]
    [InlineData("{\"version\":1,\"folders\":[false]}")]
    public void CorruptAndUnknownVersionConfigMustNeverBeOverwritten(string invalid)
    {
        var folder = Directory.CreateTempSubdirectory("vst-corrupt-");
        var config = Path.Combine(folder.FullName, "settings.json");
        try
        {
            File.WriteAllText(config, invalid);
            var load = PluginScanRootSettings.Read(config);
            Assert.False(load.IsWritable);
            Assert.NotNull(load.Warning);
            Assert.Empty(load.Folders);
            Assert.Throws<InvalidOperationException>(() =>
                PluginScanRootSettings.Save([folder.FullName], config));
            Assert.Equal(invalid, File.ReadAllText(config));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void IgnoresRelativeOrInvalidPathValues()
    {
        Assert.Empty(PluginScanRootSettings.Normalize(["relative/path", "invalid\0folder"]));
    }
}
