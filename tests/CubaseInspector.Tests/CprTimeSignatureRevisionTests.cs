using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprTimeSignatureRevisionTests
{
    private static byte[] Body(ushort revision)
    {
        // Synthetic wire values deliberately do not describe a musical meter.
        var bytes = new byte[revision > 1 ? 24 : 20];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, 0x0102030405060708);
        ushort[] words = [0, 32768, 65535, 1, 7, 65534];
        for (var index = 0; index < words.Length; index++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8 + index * 2), words[index]);
        return bytes;
    }

    private static CprClassRecordCandidate Record(int bytes, ushort? revision, long offset = 1000) =>
        new("MTimeSignatureEvent", offset, offset, bytes, 0, null, "inline-class-name",
            uint.MaxValue, 0, revision, []);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SharedPrefixPreservesRawBitsAndOffsetsBeforeTheAttributeGate(ushort revision)
    {
        var body = Body(revision);
        var original = body.ToArray();
        var archive = new byte[body.Length + 9];
        body.CopyTo(archive, 9);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(Record(body.Length, revision, 1009), archive, 1000);

        Assert.False(result.Partial);
        Assert.Equal(7, result.Fields.Count);
        Assert.Equal(new[] { "72623859790382856", "0", "32768", "65535", "1", "7", "65534" },
            result.Fields.Select(field => field.Value));
        Assert.Equal(20, result.Fields.Sum(field => field.ByteCount));
        Assert.Equal("u64-be", result.Fields[0].StorageType);
        Assert.All(result.Fields.Skip(1), field => Assert.Equal("u16-be", field.StorageType));
        Assert.All(result.Fields, field =>
        {
            Assert.Equal(Convert.ToHexString(archive.AsSpan((int)(field.FileOffset - 1000), field.ByteCount)), field.RawHex);
            Assert.Equal(EvidenceLevel.Candidate, field.Evidence);
            Assert.StartsWith("body+", field.Field);
            Assert.Contains($"serializer revision {revision}", field.Source);
            Assert.Contains("semantics unverified", field.Source);
            Assert.Contains("named attributes remain opaque", field.Source);
        });
        Assert.Equal(original, body);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RetentionBudgetKeepsOnlyLeadingCompleteFieldsAndReportsOmission(ushort revision)
    {
        var body = Body(revision);
        for (var budget = 0; budget <= 8; budget++)
        {
            var result = CprBinaryFieldCandidateDecoder.ReadBounded(
                Record(body.Length, revision), body, 1000, maximumFields: budget);
            Assert.Equal(Math.Min(7, budget), result.Fields.Count);
            Assert.Equal(budget < 7, result.Partial);
            if (budget > 0) Assert.Equal(1000, result.Fields[0].FileOffset);
            if (budget > 1) Assert.Equal(1008 + 2 * (Math.Min(7, budget) - 2), result.Fields[^1].FileOffset);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IncompletePrefixCannotPublishFieldsEvenUnderAnExhaustedBudget(ushort revision)
    {
        for (var length = 0; length < 20; length++)
        {
            var body = Body(revision)[..length];
            foreach (var budget in new[] { 0, 7 })
            {
                var result = CprBinaryFieldCandidateDecoder.ReadBounded(
                    Record(body.Length, revision), body, 1000, maximumFields: budget);
                Assert.Empty(result.Fields);
                Assert.False(result.Partial);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(65535)]
    public void UnknownAndFutureRevisionsStayOpaque(int? revision)
    {
        var body = Body(2);
        var result = CprBinaryFieldCandidateDecoder.ReadBounded(
            Record(body.Length, (ushort?)revision), body, 1000);
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

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task InventoryExposesAnonymousPrefixWithoutPromotingProjectMeter(bool wide, ushort revision)
    {
        var body = Body(revision);
        var name = Encoding.ASCII.GetBytes("MTimeSignatureEvent\0");
        var record = Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide), Number((uint)name.Length),
            name, [(byte)(revision >> 8), (byte)revision], Number((uint)body.Length, wide), body);
        var root = Chunk("ROOT", Join(Number(12), "Arrangement1"u8.ToArray(),
            Number(12), "PArrangement"u8.ToArray()), wide);
        var data = Join(root, Chunk("ARCH", record, wide));
        var source = Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"), Number((uint)data.Length, wide),
            "NUND"u8.ToArray(), data);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, source);
            var read = await new CprStructureInspector().InspectAsync(path);
            Assert.True(read.IsSuccess, read.Error?.Message);
            var inventory = Assert.Single(read.Value!.ArchiveInventories!);
            var candidate = Assert.Single(inventory.Records, item => item.ClassName == "MTimeSignatureEvent");
            Assert.Equal(revision, candidate.ClassVersion);
            Assert.Equal(7, candidate.RawFields!.Count);
            Assert.False(inventory.Partial);
            Assert.All(candidate.RawFields, field => Assert.Equal(
                Convert.ToHexString(source.AsSpan((int)field.FileOffset, field.ByteCount)), field.RawHex));
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Null(snapshot.Value!.TimeSignatureNumerator);
            Assert.Null(snapshot.Value.TimeSignatureDenominator);
            Assert.Equal(source, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }
}
