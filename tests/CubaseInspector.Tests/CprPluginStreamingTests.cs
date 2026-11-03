using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprPluginStreamingTests
{
    private const string UidA = "56535473796C3173796C656E74683100";
    private const string UidB = "297BA567D83144E1AE921DEF07B41156";

    private static byte[] Token(string value)
    {
        var chars = Encoding.UTF8.GetBytes(value + "\0");
        return [(byte)chars.Length, .. chars];
    }

    private static byte[] Record(string uid, string name)
    {
        var bytes = new List<byte>();
        bytes.AddRange("Plugin UID\0"u8.ToArray());
        bytes.AddRange(new byte[22]);
        bytes.AddRange(Token(uid));
        bytes.AddRange(new byte[3]);
        bytes.AddRange(Token("Plugin Name"));
        bytes.AddRange(new byte[5]);
        bytes.AddRange(Token(name));
        return bytes.ToArray();
    }

    [Fact]
    public async Task RecoversMetadataThatStraddlesABufferBoundaryWithoutDuplicates()
    {
        var first = Record(UidA, "StudioEQ");
        var second = Record(UidB, "Hive");
        var offset = 256 * 1024 - 5;
        var bytes = new byte[offset + first.Length + 8 + second.Length + 20];
        first.CopyTo(bytes, offset);
        second.CopyTo(bytes, offset + first.Length + 8);
        using var stream = new MemoryStream(bytes);
        var result = await CprPluginStreamScanner.ScanAsync(stream);
        Assert.False(result.Partial);
        Assert.Equal(2, result.Plugins.Count);
        Assert.Equal("StudioEQ", result.Plugins[0].Name);
        Assert.Equal("Hive", result.Plugins[1].Name);
        Assert.Equal((long)offset, result.Plugins[0].SourceByteOffset);
        Assert.Contains($"0x{offset:X}", result.Plugins[0].Role);
        Assert.Contains($"0x{offset + first.Length + 8:X}", result.Plugins[1].Role);
    }

    [Fact]
    public async Task FindsPluginUidMetadataBeyondFormer32MiBCeiling()
    {
        var offset = 32 * 1024 * 1024 + 47;
        var bytes = new byte[offset + 256];
        var record = Record(UidB, "Omnisphere");
        record.CopyTo(bytes, offset);
        using var stream = new MemoryStream(bytes);
        var result = await CprPluginStreamScanner.ScanAsync(stream);
        var entry = Assert.Single(result.Plugins);
        Assert.Equal(UidB, entry.Id);
        Assert.Equal("Omnisphere", entry.Name);
        Assert.Contains($"0x{offset:X}", entry.Role);
        Assert.False(result.Partial);
        Assert.Equal(bytes.Length, result.BytesRead);
    }

    [Fact]
    public async Task ReportsPartialCoverageWhenBudgetIsReached()
    {
        var bytes = new byte[9000];
        var record = Record(UidA, "Sylenth1");
        record.CopyTo(bytes, 400);
        record.CopyTo(bytes, 7000);
        using var stream = new MemoryStream(bytes);
        var result = await CprPluginStreamScanner.ScanAsync(stream, maxBytes: 4096);
        Assert.True(result.Partial);
        Assert.False(result.TokenCapReached);
        Assert.Equal(4096, result.BytesRead);
        Assert.Single(result.Plugins);
    }

    [Fact]
    public void BaseOffsetIsCarriedToUnresolvedAndUidOnlyEntries()
    {
        var uidOnly = new List<byte>();
        uidOnly.AddRange("Plugin UID\0"u8.ToArray());
        uidOnly.AddRange(new byte[22]);
        uidOnly.AddRange(Token(UidA));
        var token = Assert.Single(CprPluginMetadataReader.Extract(uidOnly.ToArray(), baseOffset: 900000));
        Assert.Equal(UidA, token.Id);
        Assert.Contains("0xDBBA0", token.Role);
        var unknown = Assert.Single(CprPluginMetadataReader.Extract(
            "Plugin UID\0unrecognized payload"u8.ToArray(), baseOffset: 900000));
        Assert.Equal("offset-DBBA0", unknown.Id);
    }
}
