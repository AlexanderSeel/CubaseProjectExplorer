using CubaseInspector.Core;
using Xunit;
namespace CubaseInspector.Tests;
public sealed class LocalBundleAttributionTests
{
    private const string Cid = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void SharedBundleAttributionCountsBytesOnceAcrossTwoProjects()
    {
        var root = Directory.CreateTempSubdirectory("vst-attribution-");
        try
        {
            var pluginPath = Path.Combine(root.FullName, "Shared.vst3");
            File.WriteAllBytes(pluginPath, new byte[123]);
            var reference = new PluginReference(Cid, "Shared", "Maker",
                "VST3", "track-1", EvidenceLevel.Confirmed);
            var klass = new InstalledPluginClass(Cid, "Shared", "Maker", "1", "Audio Module");
            var checks = new[] {
                new ProjectBundleCheck("ProjectA.dawproject", new PluginCheck(reference,
                    PluginStatus.InstalledByClassId, pluginPath, klass)),
                new ProjectBundleCheck("ProjectA.dawproject", new PluginCheck(reference,
                    PluginStatus.InstalledByClassId, pluginPath, klass)),
                new ProjectBundleCheck("ProjectB.dawproject", new PluginCheck(reference,
                    PluginStatus.InstalledByClassId, pluginPath, klass))
            };
            var footprint = LocalPluginFootprintService.Analyze(checks.Select(x => x.Check));
            var row = Assert.Single(LocalBundleAttributionService.Build(footprint, checks));
            Assert.Equal(123, footprint.CompleteLogicalBytes);
            Assert.Equal(2, row.ProjectCount);
            Assert.Equal(1, row.ClassCount);
            Assert.Equal(3, row.Uses.Sum(x => x.ExportedReferences));
            Assert.Equal(2, row.Uses.Count);
            var csv = LocalBundleAttributionService.Csv([row]);
            Assert.Contains("ProjectB.dawproject", csv);
            // The shared bundle's logical bytes are emitted once, not once per project row.
            Assert.Equal(1, csv.Split('\n').Count(line => line.Contains(",123,")));

        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void RefusesCandidateAndMismatchedClassEvenIfAStatusIsFabricated()
    {
        var id = new PluginReference(Cid, "=Cmd", "Maker", "VST3", "t",
            EvidenceLevel.Confirmed);
        var klass = new InstalledPluginClass(
            "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "Other", null, null, "Audio Module");
        var footprint = new LocalPluginFootprintReport(
            [new LocalBundleFootprint(Path.GetTempPath(), 0, 0, false, "Unknown", 3)],
            0, 3, 0, false, "Not measured");
        var entries = new[] {
            new ProjectBundleCheck("X", new PluginCheck(id, PluginStatus.InstalledByName,
                Path.GetTempPath(), klass)),
            new ProjectBundleCheck("X", new PluginCheck(id with {
                Evidence = EvidenceLevel.Candidate }, PluginStatus.InstalledByClassId,
                Path.GetTempPath(), klass)),
            new ProjectBundleCheck("X", new PluginCheck(id, PluginStatus.InstalledByClassId,
                Path.GetTempPath(), klass))
        };
        var row = Assert.Single(LocalBundleAttributionService.Build(footprint, entries));
        Assert.Empty(row.Uses);
        Assert.Contains("size unknown", row.Display);
        Assert.Contains(",,", LocalBundleAttributionService.Csv([row]));
    }
}
