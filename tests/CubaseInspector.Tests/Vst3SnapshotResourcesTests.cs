using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class Vst3SnapshotResourcesTests
{
    private const string Cid = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void CidNamedImageDoesNotInventPluginClassOrLoadability()
    {
        var temp = Directory.CreateTempSubdirectory("vst-resource-");
        try
        {
            var directory = Path.Combine(temp.FullName, "Contents", "Resources", "Snapshots");
            Directory.CreateDirectory(directory);
            var image = Path.Combine(directory, Cid + "_snapshot.png");
            File.WriteAllBytes(image, [137, 80, 78, 71, 13, 10, 26, 10]);
            File.WriteAllBytes(Path.Combine(directory, Cid + "_snapshot_2.0x.png"), [1, 2]);
            var bundle = new InstalledPlugin("Bundled effects", temp.FullName, "VST3", []);
            var reference = new PluginReference(Cid, "Example", null, "Vst3Plugin", "track", EvidenceLevel.Confirmed);
            var resources = Vst3SnapshotResources.Build([bundle]);
            Assert.Equal(image, Vst3SnapshotResources.Find([reference], resources));
            Assert.Null(Vst3SnapshotResources.Find([reference with { Evidence = EvidenceLevel.Candidate }], resources));
            Assert.Null(Vst3SnapshotResources.Find([reference with { Id = "id123" }], resources));
            var project = new ProjectSnapshot("test", "Test", "DAWproject", "15", null, EvidenceLevel.Confirmed, [], [reference], [], []);
            Assert.Equal(PluginStatus.Unverified, Assert.Single(new PluginScannerService().Check(project, [bundle])).Status);
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public void MultipleBundlesAndNonstandardNamesRemainUnresolved()
    {
        var temp = Directory.CreateTempSubdirectory("vst-resource-ambiguous-");
        try
        {
            var bundles = new List<InstalledPlugin>();
            foreach (var name in new[] { "A", "B" })
            {
                var root = Path.Combine(temp.FullName, name);
                var directory = Path.Combine(root, "Contents", "Resources", "Snapshots");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, Cid + "_snapshot.png"), [1]);
                File.WriteAllBytes(Path.Combine(directory, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF_snapshot_bad.png"), [1]);
                bundles.Add(new InstalledPlugin(name, root, "VST3"));
            }
            Assert.Empty(Vst3SnapshotResources.Build(bundles));
        }
        finally { temp.Delete(true); }
    }
}
