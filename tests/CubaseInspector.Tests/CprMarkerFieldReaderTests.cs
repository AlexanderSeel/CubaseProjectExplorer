using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprMarkerFieldReaderTests
{
    private static byte[] Body(byte[] text, int? declared = null, byte[]? suffix = null)
    {
        var bytes = new byte[8 + text.Length + (suffix?.Length ?? 0)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, declared ?? text.Length);
        text.CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4 + text.Length), uint.MaxValue);
        suffix?.CopyTo(bytes, 8 + text.Length);
        return bytes;
    }

    private static CprClassRecordCandidate Record(byte[] body, ushort? revision = 0, long offset = 1000) =>
        new("MMarkerEvent", offset, offset, body.Length, 0, null, "inline-class-name",
            uint.MaxValue, 0, revision, []);

    [Theory]
    [InlineData("48656C6C6F00", "ascii-string-bytes", "Hello")]
    [InlineData("00", "ascii-string-bytes", "")]
    [InlineData("EFBBBFCEBC00", "utf8-string-bytes", "μ")]
    [InlineData("CEBC00EFBBBF", "utf8-string-bytes", "μ")]
    [InlineData("CEBC00", "opaque-string-bytes", null)]
    [InlineData("FF00", "opaque-string-bytes", null)]
    [InlineData("C300EFBBBF", "opaque-string-bytes", null)]
    [InlineData("EFBBBFCEBC", "opaque-string-bytes", null)]
    [InlineData("41004200", "opaque-string-bytes", null)]
    [InlineData("410A4200", "opaque-string-bytes", null)]
    public void RetainsExactStringBytesAndFollowingScalarWithoutGuessingLegacyEncoding(
        string hex, string type, string? text)
    {
        var body = Body(Convert.FromHexString(hex), suffix: [0xFE, 0xFF, 0x88]);
        var source = body.ToArray();
        var archive = new byte[body.Length + 5];
        body.CopyTo(archive, 5);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body, offset: 1005), archive, 1000);
        Assert.False(result.Partial);
        Assert.Equal(3, result.Fields.Count);
        Assert.Equal(type, result.Fields[1].StorageType);
        Assert.Equal(text ?? $"Uninterpreted string payload ({hex.Length / 2} bytes)", result.Fields[1].Value);
        Assert.Equal(hex, result.Fields[1].RawHex);
        Assert.Equal("4294967295", result.Fields[2].Value);
        Assert.Equal(1005 + 4 + hex.Length / 2, result.Fields[2].FileOffset);
        Assert.Equal(body.Length - 3, result.Fields.Sum(field => field.ByteCount));
        Assert.All(result.Fields, field =>
        {
            Assert.Equal(EvidenceLevel.Candidate, field.Evidence);
            Assert.StartsWith("body+", field.Field);
            Assert.Contains("field semantics unverified", field.Source);
            Assert.Contains("inherited MEvent body remains opaque", field.Source);
            Assert.Contains("0x1426fc400", field.Source);
            Assert.Equal(Convert.ToHexString(archive.AsSpan((int)(field.FileOffset - 1000), field.ByteCount)), field.RawHex);
        });
        Assert.Equal(source, body);
    }

    [Fact]
    public void ZeroLengthStringHasNoInventedPayloadField()
    {
        var body = Body([]);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body), body, 1000);
        Assert.False(result.Partial);
        Assert.Equal(new[] { "body+0", "body+4" }, result.Fields.Select(field => field.Field));
        Assert.Equal(new[] { "0", "4294967295" }, result.Fields.Select(field => field.Value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(50_001)]
    [InlineData(100)]
    public void InvalidLengthsCannotCreateAPrefixOrResynchronizeToEmbeddedMarkers(int length)
    {
        var body = Body(Encoding.ASCII.GetBytes("MMarkerEvent\0"), length);
        foreach (var budget in new[] { 0, 3 })
        {
            var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body), body, 1000, maximumFields: budget);
            Assert.Empty(result.Fields);
            Assert.False(result.Partial);
        }
    }

    [Fact]
    public void TruncatedStringAndScalarCannotPublishPartialFields()
    {
        var full = Body(Encoding.ASCII.GetBytes("Marker\0"));
        for (var length = 0; length < full.Length; length++)
        {
            var body = full[..length];
            var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body), body, 1000);
            Assert.Empty(result.Fields);
            Assert.False(result.Partial);
        }
    }

    [Theory]
    [InlineData(4096, false, 3)]
    [InlineData(4097, true, 2)]
    [InlineData(50_000, true, 2)]
    public void LargeStringsKeepScalarOffsetsWithoutAllocatingUnboundedRawHex(int length, bool partial, int fieldCount)
    {
        var text = Enumerable.Repeat((byte)'A', length).ToArray();
        text[^1] = 0;
        var body = Body(text);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body), body, 1000);
        Assert.Equal(fieldCount, result.Fields.Count);
        Assert.Equal(partial, result.Partial);
        Assert.Equal(1004 + length, result.Fields[^1].FileOffset);
        Assert.Equal("4294967295", result.Fields[^1].Value);
        Assert.All(result.Fields, field => Assert.InRange(field.ByteCount, 1, 4096));
        if (partial) Assert.All(result.Fields, field => Assert.Contains("4096-byte raw-string retention limit", field.Source));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BudgetRetainsOnlyCompleteLeadingFieldsAndDisclosesOmissions(int budget)
    {
        var body = Body(Encoding.ASCII.GetBytes("A\0"));
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body), body, 1000, maximumFields: budget);
        Assert.Equal(budget, result.Fields.Count);
        Assert.Equal(budget < 3, result.Partial);
        Assert.All(result.Fields, field => Assert.Equal(
            Convert.ToHexString(body.AsSpan((int)(field.FileOffset - 1000), field.ByteCount)), field.RawHex));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(65535)]
    public void UnknownAndFutureMarkerRevisionsStayOpaque(int? revision)
    {
        var body = Body([]);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body, (ushort?)revision), body, 1000);
        Assert.Empty(result.Fields);
        Assert.False(result.Partial);
    }

    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Number(ulong value, bool wide = false)
    {
        var bytes = new byte[wide ? 8 : 4];
        if (wide) BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
        return bytes;
    }
    private static byte[] Chunk(string name, byte[] body, bool wide) =>
        Join(Encoding.ASCII.GetBytes(name), Number((uint)body.Length, wide), body);

    private static byte[] Project(bool wide, byte[] body)
    {
        var name = "MMarkerEvent\0"u8.ToArray();
        var record = Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide), Number((uint)name.Length),
            name, [0, 0], Number((uint)body.Length, wide), body);
        var root = Chunk("ROOT", Join(Number(12), "Arrangement1"u8.ToArray(),
            Number(12), "PArrangement"u8.ToArray()), wide);
        var data = Join(root, Chunk("ARCH", record, wide));
        return Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"), Number((uint)data.Length, wide),
            "NUND"u8.ToArray(), data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InventoryRetainsPrefixAndSourceOffsetsWithoutPromotingMusicalState(bool wide)
    {
        var body = Body(Join(Encoding.UTF8.GetBytes("Verse μ\0"), [0xEF, 0xBB, 0xBF]), suffix: [0x44, 0x33, 0x22]);
        var source = Project(wide, body);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, source);
            var read = await new CprStructureInspector().InspectAsync(path);
            Assert.True(read.IsSuccess, read.Error?.Message);
            var inventory = Assert.Single(read.Value!.ArchiveInventories!);
            var candidate = Assert.Single(inventory.Records, item => item.ClassName == "MMarkerEvent");
            Assert.Equal(3, candidate.RawFields!.Count);
            Assert.Equal("Verse μ", candidate.RawFields[1].Value);
            Assert.All(candidate.RawFields, field => Assert.Equal(
                Convert.ToHexString(source.AsSpan((int)field.FileOffset, field.ByteCount)), field.RawHex));
            Assert.False(inventory.Partial);
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Equal(EvidenceLevel.Candidate, snapshot.Value!.Evidence);
            Assert.Null(snapshot.Value.Bpm);
            Assert.Null(snapshot.Value.TimeSignatureNumerator);
            Assert.Null(snapshot.Value.TimeSignatureDenominator);
            Assert.Equal(source, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawStringRetentionLimitMakesInventoryPartialWithAnExplicitReason(bool wide)
    {
        var source = Project(wide, Body(Enumerable.Repeat((byte)'A', 4097).ToArray()));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, source);
            var read = await new CprStructureInspector().InspectAsync(path);
            Assert.True(read.IsSuccess, read.Error?.Message);
            var inventory = Assert.Single(read.Value!.ArchiveInventories!);
            var candidate = Assert.Single(inventory.Records, item => item.ClassName == "MMarkerEvent");
            Assert.Equal(2, candidate.RawFields!.Count);
            Assert.True(inventory.Partial);
            Assert.Contains(inventory.LimitReasons, reason => reason.Contains("4096 bytes per marker string"));
            Assert.All(candidate.RawFields, field => Assert.Contains("string payload omitted", field.Source));
            Assert.Equal(source, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }
}
