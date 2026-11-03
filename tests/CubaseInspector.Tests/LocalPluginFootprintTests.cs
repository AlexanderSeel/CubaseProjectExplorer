using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public class LocalPluginFootprintTests
{
    [Fact]
    public void CountsSharedBundleOnlyOnce_AndExcludesWeakEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-footprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "moduleinfo.json"), new byte[101]);
            Directory.CreateDirectory(Path.Combine(root, "Contents"));
            File.WriteAllBytes(Path.Combine(root, "Contents", "plugin.vst3"), new byte[499]);
            var confirmed = new PluginReference("A", "Synth", "Maker", "VST3", null, EvidenceLevel.Confirmed);
            var candidate = confirmed with { Evidence = EvidenceLevel.Candidate };
            var checks = new[]
            {
                new PluginCheck(confirmed, PluginStatus.InstalledByClassId, root),
                new PluginCheck(confirmed, PluginStatus.InstalledByClassId, root),
                new PluginCheck(confirmed, PluginStatus.InstalledByName, root),
                new PluginCheck(candidate, PluginStatus.InstalledByClassId, root)
            };
            var result = LocalPluginFootprintService.Analyze(checks);
            Assert.True(result.Complete);
            Assert.Equal(600, result.CompleteLogicalBytes);
            Assert.Single(result.Bundles);
            Assert.Equal(2, result.Bundles[0].Files);
            Assert.Equal(2, result.Bundles[0].ConfirmedReferences);
            Assert.Equal(2, result.ExcludedReferences);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidClassMatchPathIsExcludedWithoutThrowing()
    {
        var plugin = new PluginReference("A", "Synth", null, "VST3", null, EvidenceLevel.Confirmed);
        var report = LocalPluginFootprintService.Analyze([
            new PluginCheck(plugin, PluginStatus.InstalledByClassId, "invalid\0path")
        ]);
        Assert.False(report.Complete);
        Assert.Equal(0, report.ExactReferences);
        Assert.Equal(1, report.ExcludedReferences);
        Assert.Empty(report.Bundles);
    }

    [Fact]
    public void LinkedChildrenNeverCountAsCompleteBundleSize()
    {
        var root = Directory.CreateTempSubdirectory("vst-reparse-");
        var external = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "internal.bin"), new byte[42]);
            File.WriteAllBytes(external, new byte[250]);
            try { File.CreateSymbolicLink(Path.Combine(root.FullName, "linked.bin"), external); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                NotSupportedException or System.Security.SecurityException)
            { return; } // Some CI accounts have no symlink privilege.
            var reference = new PluginReference("A", "Synth", null, "VST3",
                null, EvidenceLevel.Confirmed);
            var report = LocalPluginFootprintService.Analyze([
                new PluginCheck(reference, PluginStatus.InstalledByClassId, root.FullName)]);
            Assert.False(report.Complete);
            Assert.Equal(0, report.CompleteLogicalBytes);
            Assert.False(report.Bundles[0].Complete);
            Assert.Equal(42, report.Bundles[0].LogicalBytes);
            Assert.Contains("Reparse", report.Bundles[0].Warning);
        }
        finally
        {
            root.Delete(true);
            File.Delete(external);
        }
    }

    [Fact]
    public void MissingBundleIsIncompleteAndExcludedFromTotal()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".vst3");
        var plugin = new PluginReference("A", "Synth", null, "VST3", null, EvidenceLevel.Confirmed);
        var result = LocalPluginFootprintService.Analyze([
            new PluginCheck(plugin, PluginStatus.InstalledByClassId, missing)
        ]);
        Assert.False(result.Complete);
        Assert.Equal(0, result.CompleteLogicalBytes);
        Assert.False(result.Bundles[0].Complete);
    }
}
