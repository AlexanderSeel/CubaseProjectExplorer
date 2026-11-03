using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LegacyCubaseGenerationTests
{
    [Theory]
    [InlineData("Cubase SX Version 1.06", "SX / SL", 1, true)]
    [InlineData("Cubase SX Version 3.1.1", "SX / SL", 3, true)]
    [InlineData("Cubase SL Version 2.0", "SX / SL", 2, true)]
    [InlineData("Cubase Version 4.0.0", "4–10.x", 4, true)]
    [InlineData("Cubase Version 5.1.1", "4–10.x", 5, true)]
    [InlineData("Cubase Pro 10.5.2", "4–10.x", 10, true)]
    [InlineData("Cubase Version 11.0.0", "11+", 11, false)]
    [InlineData("Nuendo Version 3.0", "pre-11", 3, true)]
    public void ClassifiesSavedByHintsWithoutPromotingMetadata(string raw,
        string expected, int major, bool legacy)
    {
        var version = CubaseGenerationClassifier.Classify(raw);
        Assert.Contains(expected, version.Generation);
        Assert.Equal(major, version.SavedByMajorVersion);
        Assert.Equal(legacy, version.IsBefore11);
        Assert.Contains("Candidate", version.Evidence);
    }

    [Fact]
    public void AmbiguousVersionThreeDoesNotPretendToBeCubaseSx()
    {
        var info = CubaseGenerationClassifier.Classify("Cubase Version 3.7");
        Assert.Contains("ambiguous", info.Generation);
        Assert.False(info.IsBefore11);
        Assert.DoesNotContain("SX / SL", info.Generation);
        Assert.Null(CubaseGenerationClassifier.Classify(null).SavedByMajorVersion);
        Assert.Null(CubaseGenerationClassifier.Classify("random text 5").SavedByMajorVersion);
    }

    private static byte[] Be32(uint value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        return data;
    }
    private static byte[] Bytes(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    private static byte[] Chunk(string id, byte[] payload) =>
        Bytes(Encoding.ASCII.GetBytes(id), Be32((uint)payload.Length), payload);
    private static byte[] Root(string stream, string cls)
    {
        var a = Encoding.ASCII.GetBytes(stream);
        var b = Encoding.ASCII.GetBytes(cls);
        return Chunk("ROOT", Bytes(Be32((uint)a.Length), a, Be32((uint)b.Length), b));
    }

    private static byte[] FullRecord(string klass, byte[] body)
    {
        var name = Encoding.ASCII.GetBytes(klass + "\0");
        var version = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(version, 1);
        return Bytes(Be32(0xFFFFFFFF), Be32((uint)name.Length), name, version,
            Be32((uint)body.Length), body);
    }

    [Theory]
    [InlineData("Cubase SX", "Version 3.1.1")]
    [InlineData("Cubase", "Version 9.5.0")]
    public async Task ReadsLegacyCandidateTrackWhileKeepingIdentityUnverified(
        string application, string version)
    {
        var versionPayload = Bytes(Encoding.ASCII.GetBytes(application + "\0"),
            Be32(12), Encoding.ASCII.GetBytes(version + "\0"));
        var name = Bytes(Be32(4), Encoding.ASCII.GetBytes("Vox\0"));
        var track = FullRecord("MAudioTrackEvent", FullRecord("MListNode", name));
        var chunks = Bytes(Root("Version", "PAppVersion"), Chunk("ARCH", versionPayload),
            Root("Arrangement1", "PArrangement"), Chunk("ARCH", track));
        var payload = Bytes("RIFF"u8.ToArray(), Be32((uint)chunks.Length),
            "NUND"u8.ToArray(), chunks);
        var dir = Directory.CreateTempSubdirectory("legacy-track-");
        var file = Path.Combine(dir.FullName, "Track.cpr");
        try
        {
            await File.WriteAllBytesAsync(file, payload);
            var parsed = await new CprEvidenceReader().ReadAsync(file);
            Assert.True(parsed.IsSuccess, parsed.Error?.Message);
            var reference = Assert.Single(parsed.Value!.Tracks);
            Assert.Equal("Vox", reference.Name);
            Assert.Equal("Audio", reference.Kind);
            Assert.Equal(EvidenceLevel.Candidate, reference.Evidence);
            Assert.Null(reference.ChannelId);
            Assert.Empty(parsed.Value.Plugins);
            Assert.Equal(EvidenceLevel.Candidate, parsed.Value.Evidence);
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData("Cubase SX", "Version 3.1.1", "SX / SL")]
    [InlineData("Cubase SL", "Version 2.0", "SX / SL")]
    [InlineData("Cubase", "Version 7.5.0", "4–10.x")]
    [InlineData("Cubase Pro", "Version 10.5.0", "4–10.x")]
    public async Task ReadsLegacyCprHeaderAndVersionCandidateEndToEnd(
        string application, string version, string expectedEra)
    {
        // Synthetic RIFF/NUND structure, NOT a genuine SX source fixture.
        var versionPayload = Bytes(Encoding.ASCII.GetBytes(application + "\0"),
            Be32(12), Encoding.ASCII.GetBytes(version + "\0"));
        var chunks = Bytes(Root("Version", "PAppVersion"),
            Chunk("ARCH", versionPayload));
        var projectBytes = Bytes("RIFF"u8.ToArray(), Be32((uint)chunks.Length),
            "NUND"u8.ToArray(), chunks);
        var dir = Directory.CreateTempSubdirectory("legacy-cpr-");
        var path = Path.Combine(dir.FullName, "legacy.cpr");
        try
        {
            await File.WriteAllBytesAsync(path, projectBytes);
            Assert.True(CprFileIdentity.HasCprHeader(path));
            var structural = await new CprStructureInspector().InspectAsync(path);
            Assert.True(structural.IsSuccess, structural.Error?.Message);
            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(EvidenceLevel.Candidate, result.Value!.Evidence);
            Assert.Empty(result.Value.Plugins);
            Assert.Empty(result.Value.Tracks);
            Assert.Contains(expectedEra,
                CubaseGenerationClassifier.Classify(result.Value.CreatorVersion).Generation);
            Assert.Contains(result.Value.Warnings,
                warning => warning.Contains("saved-by", StringComparison.OrdinalIgnoreCase));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task ReadsSxMidiListNodeNameAsCandidateFromDocumentedMarkerShape()
    {
        // Synthetic record based on the SX2/SX3 repository's MListNode hypothesis;
        // verifies bounded extraction only and is not a genuine Cubase fixture.
        var nodePayload = Bytes([0x80, 0x00, 0x01, 0x7C], Be32(9), Be32(5), Encoding.ASCII.GetBytes("Lead\0"));
        var track = FullRecord("MMidiTrackEvent", FullRecord("MListNode", nodePayload));
        var chunks = Bytes(Root("Arrangement1", "PArrangement"), Chunk("ARCH", track));
        var projectBytes = Bytes("RIFF"u8.ToArray(), Be32((uint)chunks.Length), "NUND"u8.ToArray(), chunks);
        var dir = Directory.CreateTempSubdirectory("sx-candidate-name-");
        var path = Path.Combine(dir.FullName, "sx.cpr");
        try
        {
            await File.WriteAllBytesAsync(path, projectBytes);
            var parsed = await new CprStructureInspector().InspectAsync(path);
            Assert.True(parsed.IsSuccess, parsed.Error?.Message);
            var hint = Assert.Single(parsed.Value!.Arrangement!.TrackHints);
            Assert.Equal("Lead", hint.Name);
            Assert.Contains("SX2/SX3", hint.NameSource);
        }
        finally { dir.Delete(true); }
    }
}
