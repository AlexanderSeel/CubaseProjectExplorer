using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprAutomationTrackFieldTests
{
    // Deliberately non-musical wire values, including high-bit IDs and NaN bits.
    private static byte[] Body(ushort revision, int count = 2)
    {
        if (revision == 2) return [0x80, 0x01, 0xFF, 0xFE];
        var stride = revision == 0 ? 8 : 10;
        var bytes = new byte[8 + count * stride];
        BinaryPrimitives.WriteInt32BigEndian(bytes, count);
        for (var index = 0; index < count; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4 + index * stride), 0xFEDCBA98);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8 + index * stride),
                index % 2 == 0 ? 0x7FC01234U : 0x80000000U);
            if (revision == 1)
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12 + index * stride), 0x8001);
        }
        bytes[^4] = 0x80; bytes[^3] = 0x01;
        bytes[^2] = 0xFF; bytes[^1] = 0xFE;
        return bytes;
    }

    private static CprClassRecordCandidate Record(int bytes, ushort? revision, long offset = 1000) =>
        new("MAutomationTrack", offset, offset, bytes, 0, null, "inline-class-name",
            uint.MaxValue, 0, revision, []);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RevisionGatesPreserveAnonymousFieldsAtExactOffsets(ushort revision)
    {
        var body = Body(revision);
        var original = body.ToArray();
        var archive = new byte[body.Length + 9];
        body.CopyTo(archive, 9);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body.Length, revision, 1009), archive, 1000);
        var expectedOffsets = revision switch
        {
            0 => new[] { 0, 4, 8, 12, 16, 20, 22 },
            1 => new[] { 0, 4, 8, 12, 14, 18, 22, 24, 26 },
            _ => new[] { 0, 2 }
        };
        Assert.False(result.Partial);
        Assert.Equal(expectedOffsets.Select(value => 1009L + value), result.Fields.Select(field => field.FileOffset));
        Assert.Equal(body.Length, result.Fields.Sum(field => field.ByteCount));
        Assert.Equal("32769", result.Fields[^2].Value);
        Assert.Equal("65534", result.Fields[^1].Value);
        if (revision < 2)
        {
            Assert.Equal("i32-be", result.Fields[0].StorageType);
            Assert.Equal("2", result.Fields[0].Value);
            Assert.Contains(result.Fields, field => field.StorageType == "u32-be" && field.Value == "4275878552");
            Assert.Contains(result.Fields, field => field.StorageType == "f32-be" && field.RawHex == "7FC01234");
            Assert.Contains(result.Fields, field => field.StorageType == "f32-be" && field.RawHex == "80000000");
        }
        Assert.All(result.Fields, field =>
        {
            Assert.Equal(EvidenceLevel.Candidate, field.Evidence);
            Assert.Equal(Convert.ToHexString(body.AsSpan((int)(field.FileOffset - 1009), field.ByteCount)), field.RawHex);
            Assert.Contains($"serializer revision {revision}", field.Source);
            Assert.Contains("no parameter target, mode or owner is inferred", field.Source);
        });
        Assert.Equal(original, body);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EmptyLegacyTablesStillRequireBothTrailingWords(ushort revision)
    {
        var body = Body(revision, 0);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body.Length, revision), body, 1000);
        Assert.Equal(new[] { "0", "32769", "65534" }, result.Fields.Select(field => field.Value));
        Assert.False(result.Partial);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryShorterPrefixIsRejectedEvenWithNoRetentionBudget(ushort revision)
    {
        var full = Body(revision);
        for (var size = 0; size < full.Length; size++)
            foreach (var budget in new[] { 0, 256 })
            {
                var body = full[..size];
                var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(size, revision), body, 1000,
                    maximumFields: budget);
                Assert.Empty(result.Fields);
                Assert.False(result.Partial);
            }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(3)]
    public void UnsupportedOrOversizedCountsCannotPublishAPartialTable(int count)
    {
        foreach (ushort revision in new ushort[] { 0, 1 })
        {
            var body = Body(revision);
            BinaryPrimitives.WriteInt32BigEndian(body, count);
            var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body.Length, revision), body, 1000);
            Assert.Empty(result.Fields);
            Assert.False(result.Partial);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RetentionKeepsOnlyLeadingFieldsAndDoesNotReadInheritedBytes(ushort revision)
    {
        var body = Body(revision);
        var full = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body.Length, revision), body, 1000);
        var withOpaqueTail = body.Concat(new byte[] { 0x90, 0xFF, 0, 0xAB }).ToArray();
        for (var budget = 0; budget <= full.Fields.Count + 1; budget++)
        {
            var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(withOpaqueTail.Length, revision),
                withOpaqueTail, 1000, maximumFields: budget);
            Assert.Equal(full.Fields.Take(budget), result.Fields);
            Assert.Equal(budget < full.Fields.Count, result.Partial);
        }
    }

    [Fact]
    public void LargeLegacyTableHonorsThePerRecordFieldBudget()
    {
        var body = Body(1, 100);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body.Length, 1), body, 1000);
        Assert.Equal(256, result.Fields.Count);
        Assert.True(result.Partial);
        Assert.All(result.Fields, field => Assert.InRange(field.FileOffset, 1000, 1852));
        Assert.Equal("body+852", result.Fields[^1].Field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(65535)]
    public void UnknownRevisionsStayOpaque(int? revision)
    {
        var body = Body(2);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body.Length, (ushort?)revision), body, 1000);
        Assert.Empty(result.Fields);
        Assert.False(result.Partial);
    }

    private static byte[] Number(ulong value, bool wide = false)
    {
        var bytes = new byte[wide ? 8 : 4];
        if (wide) BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
        return bytes;
    }

    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();

    private static byte[] Chunk(string name, byte[] body, bool wide) =>
        Join(Encoding.ASCII.GetBytes(name), Number((uint)body.Length, wide), body);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task InventoryRetainsFieldsWithoutInventingAutomationLanesOrTargets(bool wide, ushort revision)
    {
        var body = Body(revision);
        var name = Encoding.ASCII.GetBytes("MAutomationTrack\0");
        var record = Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide), Number((uint)name.Length),
            name, [(byte)(revision >> 8), (byte)revision], Number((uint)body.Length, wide), body);
        var root = Chunk("ROOT", Join(Number(12), "Arrangement1"u8.ToArray(),
            Number(12), "PArrangement"u8.ToArray()), wide);
        var chunks = Join(root, Chunk("ARCH", record, wide));
        var source = Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"), Number((uint)chunks.Length, wide),
            "NUND"u8.ToArray(), chunks);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, source);
            var read = await new CprStructureInspector().InspectAsync(path);
            Assert.True(read.IsSuccess, read.Error?.Message);
            var inventory = Assert.Single(read.Value!.ArchiveInventories!);
            var candidate = Assert.Single(inventory.Records, item => item.ClassName == "MAutomationTrack");
            Assert.Equal(revision == 0 ? 7 : revision == 1 ? 9 : 2, candidate.RawFields!.Count);
            Assert.False(inventory.Partial);
            Assert.All(candidate.RawFields, field => Assert.Equal(
                Convert.ToHexString(source.AsSpan((int)field.FileOffset, field.ByteCount)), field.RawHex));
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Empty(snapshot.Value!.AutomationLanes ?? []);
            Assert.Equal(source, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }
}
