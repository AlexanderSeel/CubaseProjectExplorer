using CubaseInspector.Core;
using Xunit;
namespace CubaseInspector.Tests;

public sealed class VerifiedVst3ArtworkTests
{
    private const string Cid = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void UsesOnlyConfirmedExactClassLocalImage()
    {
        var temp = Directory.CreateTempSubdirectory("vst-artwork-");
        try
        {
            Directory.CreateDirectory(Path.Combine(temp.FullName, "Contents", "Resources"));
            const string relative = "Contents/Resources/screenshot.png";
            var file = Path.Combine(temp.FullName, "Contents", "Resources", "screenshot.png");
            File.WriteAllBytes(file, [137, 80, 78, 71, 13, 10, 26, 10]);
            var plugin = new PluginReference(Cid, "Synth", "Maker", "VST3",
                "track", EvidenceLevel.Confirmed);
            var klass = new InstalledPluginClass(Cid, "Synth", "Maker", "1", "Audio Module",
                relative);
            var match = new PluginCheck(plugin, PluginStatus.InstalledByClassId, temp.FullName, klass);
            Assert.Equal(file, VerifiedVst3Artwork.FindLocalSnapshot([match]));
            Assert.Equal(file, VerifiedVst3Artwork.FindLocalSnapshot([match with { Reference = plugin with { Format = "Vst3Plugin" } }]));
            Assert.Null(VerifiedVst3Artwork.FindLocalSnapshot([match with {
                Reference = plugin with { Evidence = EvidenceLevel.Candidate } }]));
            Assert.Null(VerifiedVst3Artwork.FindLocalSnapshot([match with {
                MatchedClass = klass with { Cid = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF" } }]));
            Assert.Null(VerifiedVst3Artwork.FindLocalSnapshot([match with {
                Status = PluginStatus.InstalledByName }]));
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public void FindsStandardCidResourceWithoutAdvertisedSnapshotPath()
    {
        var temp = Directory.CreateTempSubdirectory("vst-artwork-standard-");
        try
        {
            var directory = Path.Combine(temp.FullName, "Contents", "Resources", "Snapshots");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Cid + "_snapshot.png");
            File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10]);
            var klass = new InstalledPluginClass(Cid, "Synth", "Maker", "1", "Audio Module");
            var reference = new PluginReference(Cid, "Synth", "Maker", "Vst3Plugin", "track", EvidenceLevel.Confirmed);
            Assert.Equal(path, VerifiedVst3Artwork.FindLocalSnapshot([new(reference, PluginStatus.InstalledByClassId, temp.FullName, klass)]));
            Assert.Null(VerifiedVst3Artwork.FindClassSnapshot(temp.FullName, klass with { Cid = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF" }));
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public void RefusesTraversalAndOversizedPng()
    {
        var temp = Directory.CreateTempSubdirectory("vst-artwork-invalid-");
        try
        {
            var path = Path.Combine(temp.FullName, "oversize.png");
            using (var stream = File.Create(path)) stream.SetLength(8L * 1024 * 1024 + 1);
            Assert.Null(VerifiedVst3Artwork.ValidateSnapshot(temp.FullName, "oversize.png"));
            Assert.Null(VerifiedVst3Artwork.ValidateSnapshot(temp.FullName, "../outside.png"));
            Assert.Null(VerifiedVst3Artwork.ValidateSnapshot(temp.FullName, "missing.png"));
            Assert.Null(VerifiedVst3Artwork.ValidateSnapshot(temp.FullName, "readme.txt"));
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public void DifferentMatchedSnapshotsAreAmbiguous()
    {
        var temp = Directory.CreateTempSubdirectory("vst-artwork-ambiguous-");
        try
        {
            File.WriteAllBytes(Path.Combine(temp.FullName, "a.png"), [1, 2]);
            File.WriteAllBytes(Path.Combine(temp.FullName, "b.png"), [1, 2]);
            var plugin = new PluginReference(Cid, "Synth", "Maker", "VST3",
                "track", EvidenceLevel.Confirmed);
            var klass = new InstalledPluginClass(Cid, "Synth", "Maker", "1", "Audio Module",
                "a.png");
            var checks = new[] {
                new PluginCheck(plugin, PluginStatus.InstalledByClassId, temp.FullName, klass),
                new PluginCheck(plugin, PluginStatus.InstalledByClassId, temp.FullName,
                    klass with { SnapshotPath = "b.png" })
            };
            Assert.Null(VerifiedVst3Artwork.FindLocalSnapshot(checks));
        }
        finally { temp.Delete(true); }
    }
}
