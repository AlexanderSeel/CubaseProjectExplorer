using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprTempoTableTests
{
    private static byte[] Body(int count, ushort revision)
    {
        var bytes = new byte[4 + count * 22 + (revision == 0 ? 0 : 6) + (revision >= 2 ? 4 : 0) +
            (revision == 3 ? count * 4 : 0)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, count);
        for (var index = 0; index < count; index++)
        {
            var row = bytes.AsSpan(4 + index * 22, 22);
            BinaryPrimitives.WriteInt32BigEndian(row, BitConverter.SingleToInt32Bits(index + 0.5f));
            BinaryPrimitives.WriteInt64BigEndian(row[4..], BitConverter.DoubleToInt64Bits(-12.25 + index));
            BinaryPrimitives.WriteUInt64BigEndian(row[12..], index == 0 ? 0x7FF8000000001234 : 0x8000000000000000);
            BinaryPrimitives.WriteUInt16BigEndian(row[20..], (ushort)(0x8000 + index));
        }
        if (revision > 0)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4 + count * 22), BitConverter.SingleToInt32Bits(123.5f));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8 + count * 22), 1);
        }
        return bytes;
    }

    private static CprClassRecordCandidate Record(byte[] body, ushort revision, long offset = 1000) =>
        new("MTempoTrackEvent", offset, offset, body.Length, 0, null, "inline-class-name", uint.MaxValue,
            0, revision, []);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReadsCountedRowsAndRevisionSuffixAsAnonymousRawBits(ushort revision)
    {
        var body = Body(3, revision);
        var archive = new byte[body.Length + 9];
        body.CopyTo(archive, 9);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body, revision, 1009), archive, 1000);
        Assert.False(result.Partial);
        Assert.Equal(revision == 0 ? 13 : 15, result.Fields.Count);
        Assert.Equal("3", result.Fields[0].Value);
        Assert.Equal("-12.25", result.Fields[2].Value);
        Assert.Equal("NaN", result.Fields[3].Value);
        Assert.Equal("7FF8000000001234", result.Fields[3].RawHex);
        Assert.Equal("8000000000000000", result.Fields[7].RawHex);
        Assert.All(result.Fields, field =>
        {
            Assert.Equal(EvidenceLevel.Candidate, field.Evidence);
            Assert.Equal(Convert.ToHexString(archive.AsSpan((int)(field.FileOffset - 1000), field.ByteCount)), field.RawHex);
            Assert.Contains("semantics unverified", field.Source);
            Assert.Contains($"serializer revision {revision}", field.Source);
            Assert.Contains("suffix attributes remain opaque", field.Source);
            Assert.DoesNotContain("BPM", field.Field);
        });
        Assert.Equal(4 + 3 * 22 + (revision == 0 ? 0 : 6), result.Fields.Sum(field => field.ByteCount));
        if (revision > 0)
        {
            Assert.Equal("123.5", result.Fields[^2].Value);
            Assert.Equal("1", result.Fields[^1].Value);
            Assert.Equal("0001", result.Fields[^1].RawHex);
            Assert.Equal("u16-be", result.Fields[^1].StorageType);
            Assert.Equal(1009 + 4 + 3 * 22, result.Fields[^2].FileOffset);
        }
    }

    [Theory]
    [InlineData(63, false, 255)]
    [InlineData(64, true, 253)]
    [InlineData(1000, true, 253)]
    public void PerRecordBudgetKeepsWholeRowsAndDisclosesOmittedEntries(int count, bool partial, int fields)
    {
        var body = Body(count, 3);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body, 3), body, 1000);
        Assert.Equal(partial, result.Partial);
        Assert.Equal(fields, result.Fields.Count);
        Assert.Equal(count.ToString(), result.Fields[0].Value);
        Assert.Contains($"retained fixed entries {Math.Min(count, 63)} of {count}", result.Fields[0].Source);
        if (partial) Assert.Equal(1000 + 4 + 62 * 22 + 20, result.Fields[^1].FileOffset);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(4, 1, true)]
    [InlineData(5, 5, true)]
    [InlineData(9, 9, true)]
    [InlineData(10, 10, true)]
    [InlineData(11, 11, false)]
    public void CallerBudgetCannotJumpOverOmittedRowsOrHideATruncatedSuffix(int budget, int fields, bool partial)
    {
        var body = Body(2, 3);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body, 3), body, 1000, maximumFields: budget);
        Assert.Equal(fields, result.Fields.Count);
        Assert.Equal(partial, result.Partial);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(3)]
    public void InvalidOrTruncatedCountsCannotPublishEvenTheFirstRow(int declaredCount)
    {
        var body = Body(2, 3)[..^12]; // A third fixed row does not fit the containing body.
        BinaryPrimitives.WriteInt32BigEndian(body, declaredCount);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body, 3), body, 1000);
        Assert.Empty(result.Fields);
        Assert.False(result.Partial);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(257)]
    public void RejectsInvalidRetentionBudgets(int budget)
    {
        var body = Body(1, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => CprBinaryFieldCandidateDecoder.ReadBounded(
            Record(body, 3), body, 1000, maximumFields: budget));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RevisionOneAndLaterRequireTheEntireFixedSuffix(int missingBytes)
    {
        var body = Body(2, 1)[..^(missingBytes + 1)];
        Assert.Empty(CprBinaryFieldCandidateDecoder.Read(Record(body, 1), body, 1000));
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
    private static byte[] Number(ulong value, bool wide = false)
    {
        var bytes = new byte[wide ? 8 : 4];
        if (wide) BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
        return bytes;
    }
    private static byte[] Chunk(string name, byte[] body, bool wide) =>
        Join(Encoding.ASCII.GetBytes(name), Number((uint)body.Length, wide), body);
    private static byte[] Root(string name, bool wide) => Chunk("ROOT",
        Join(Number((uint)name.Length), Encoding.ASCII.GetBytes(name), Number(12), "PArrangement"u8.ToArray()), wide);
    private static byte[] SerializedRecord(byte[] body, ushort revision, bool wide) =>
        Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide), Number(17), "MTempoTrackEvent\0"u8.ToArray(),
            [(byte)(revision >> 8), (byte)revision], Number((uint)body.Length, wide), body);
    private static byte[] Project(bool wide, params byte[][] chunks)
    {
        var data = Join(chunks);
        return Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"), Number((uint)data.Length, wide), "NUND"u8.ToArray(), data);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public async Task InventoryKeepsMultiEntryFieldsWithoutAssigningBpm(bool wide, ushort revision)
    {
        var data = Project(wide, Root("Arrangement1", wide), Chunk("ARCH", SerializedRecord(Body(3, revision), revision, wide), wide));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, data);
            var read = await new CprStructureInspector().InspectAsync(path);
            Assert.True(read.IsSuccess, read.Error?.Message);
            var inventory = Assert.Single(read.Value!.ArchiveInventories!);
            var record = Assert.Single(inventory.Records, item => item.Depth == 0);
            // Signed-zero bits can resemble an empty referenced class header in
            // the separate byte inventory. They must not acquire tempo fields.
            Assert.All(inventory.Records.Where(item => item.Depth > 0), item => Assert.Empty(item.RawFields!));
            Assert.Equal(revision == 0 ? 13 : 15, record.RawFields!.Count);
            Assert.False(inventory.Partial);
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Null(snapshot.Value!.Bpm);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RawFieldBudgetAppliesAcrossArchivesWithoutDroppingRecordHeaders()
    {
        var record = SerializedRecord(Body(32, 3), 3, true);
        var archive = Join(Enumerable.Repeat(record, 400).ToArray());
        var data = Project(true, Root("First", true), Chunk("ARCH", archive, true),
            Root("Second", true), Chunk("ARCH", archive, true));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, data);
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var inventories = result.Value!.ArchiveInventories!;
            Assert.Equal(800, inventories.Sum(item => item.Records.Count));
            Assert.Equal(100_000, inventories.SelectMany(item => item.Records).Sum(item => item.RawFields!.Count));
            Assert.False(inventories[0].Partial);
            Assert.True(inventories[1].Partial);
            Assert.Contains(inventories[1].LimitReasons, reason => reason.Contains("Raw-field retention budget"));
            Assert.Contains(result.Value.Warnings, warning => warning.Contains("raw-field limits"));
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }
}
