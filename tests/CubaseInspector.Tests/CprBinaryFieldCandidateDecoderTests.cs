using System.Buffers.Binary;
using System.Globalization;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprBinaryFieldCandidateDecoderTests
{
    private static CprClassRecordCandidate Record(long bodyOffset = 1000) => new(
        "MTempoEvent", bodyOffset, bodyOffset, 24, 0, null,
        "inline-class-name", uint.MaxValue, 0, 0, []);

    private static byte[] Body()
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), BitConverter.SingleToInt32Bits(1.25f));
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(4, 8), BitConverter.DoubleToInt64Bits(1234.5));
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(12, 8), BitConverter.DoubleToInt64Bits(-0.0625));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), uint.MaxValue);
        return bytes;
    }

    [Fact]
    public void ReadsOnlyAnonymousTypedFieldsWithAbsoluteOffsetsAndRawBits()
    {
        var body = Body();
        var archive = new byte[40];
        body.CopyTo(archive, 7);
        var fields = CprBinaryFieldCandidateDecoder.Read(Record(1007), archive, 1000);

        Assert.Collection(fields,
            field => Check(field, "body+0", "f32-be", "1.25", 1007, "3FA00000"),
            field => Check(field, "body+4", "f64-be", "1234.5", 1011, "40934A0000000000"),
            field => Check(field, "body+12", "f64-be", "-0.0625", 1019, "BFB0000000000000"),
            field => Check(field, "body+20", "u32-be", "4294967295", 1027, "FFFFFFFF"));
    }

    [Fact]
    public void NumericRenderingUsesInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var fields = CprBinaryFieldCandidateDecoder.Read(Record(), Body(), 1000);
            Assert.Equal("1234.5", fields[1].Value);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void PreservesNonfinitePayloadsAndSignedZeroWithoutDiscardingFields()
    {
        var bytes = Body();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 0x7FC01234);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(4, 8), 0xFFF0000000000000);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(12, 8), 0x8000000000000000);
        var fields = CprBinaryFieldCandidateDecoder.Read(Record(), bytes, 1000);

        Assert.Equal(4, fields.Count);
        Assert.Equal("NaN", fields[0].Value);
        Assert.Equal("7FC01234", fields[0].RawHex);
        Assert.Equal("-Infinity", fields[1].Value);
        Assert.Equal("FFF0000000000000", fields[1].RawHex);
        Assert.Equal("8000000000000000", fields[2].RawHex);
    }

    [Fact]
    public void LayoutDoesNotDependOnInlineVersusReferencedClassEncoding()
    {
        var inline = Record();
        var reference = inline with { ClassEncoding = "interned-class-reference",
            ClassToken = 0x8000000000000000UL };
        Assert.Equal(CprBinaryFieldCandidateDecoder.Read(inline, Body(), 1000),
            CprBinaryFieldCandidateDecoder.Read(reference, Body(), 1000));
    }

    [Fact]
    public void UnsupportedClassVersionOrBodySizeProducesNoFields()
    {
        var record = Record();
        foreach (var unsupported in new[]
        {
            record with { ClassName = "mtempoevent" },
            record with { ClassName = "MTempoTrackEvent" },
            record with { ClassVersion = null },
            record with { ClassVersion = 1 },
            record with { BodyBytes = 23 },
            record with { BodyBytes = 25 }
        })
            Assert.Empty(CprBinaryFieldCandidateDecoder.Read(unsupported, Body(), 1000));
    }

    [Fact]
    public void ReadsNarrowSingleEntryTempoTrackLayoutAndLeavesAttributesOpaque()
    {
        var bytes = TempoTrackBody();
        var record = Record() with { ClassName = "MTempoTrackEvent", ClassVersion = 3, BodyBytes = 54 };
        var fields = CprBinaryFieldCandidateDecoder.Read(record, bytes, 1000);

        Assert.Collection(fields,
            field => Check(field, "body+0", "u32-be", "1", 1000, "00000001", "0x1431e2e00"),
            field => Check(field, "body+4", "f32-be", "-0.5", 1004, "BF000000", "0x1431e2e00"),
            field => Check(field, "body+8", "f64-be", "120", 1008, "405E000000000000", "0x1431e2e00"),
            field => Check(field, "body+16", "f64-be", "960", 1016, "408E000000000000", "0x1431e2e00"),
            field => Check(field, "body+24", "u16-be", "4660", 1024, "1234", "0x1431e2e00"),
            field => Check(field, "body+26", "f32-be", "3.25", 1026, "40500000", "0x1431e2e00"),
            field => Check(field, "body+30", "u8", "171", 1030, "AB", "0x1431e2e00"));
        Assert.Equal(31, fields.Sum(field => field.ByteCount));
        Assert.All(fields, field => Assert.Contains("attributes remain opaque", field.Source));
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(2U)]
    [InlineData(uint.MaxValue)]
    public void RejectsUnsupportedTempoTrackEntryCountsWithoutAllocatingArrays(uint count)
    {
        var bytes = TempoTrackBody();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), count);
        var record = Record() with { ClassName = "MTempoTrackEvent", ClassVersion = 3, BodyBytes = 54 };
        Assert.Empty(CprBinaryFieldCandidateDecoder.Read(record, bytes, 1000));
    }

    [Fact]
    public void RejectsUnsupportedTempoTrackVersionAndBodySizes()
    {
        var record = Record() with { ClassName = "MTempoTrackEvent", ClassVersion = 3, BodyBytes = 54 };
        foreach (var unsupported in new[]
        {
            record with { ClassVersion = 0 }, record with { ClassVersion = 2 },
            record with { ClassVersion = 4 }, record with { ClassVersion = null },
            record with { BodyBytes = 53 }, record with { BodyBytes = 55 },
            record with { BodyFileOffset = 1001 }
        })
            Assert.Empty(CprBinaryFieldCandidateDecoder.Read(unsupported, TempoTrackBody(), 1000));
    }

    [Theory]
    [InlineData(999, 1000, 24)]
    [InlineData(1001, 1000, 24)]
    [InlineData(1000, 1000, 23)]
    [InlineData(0, -1, 24)]
    [InlineData(long.MaxValue, long.MaxValue - 1, 24)]
    [InlineData(long.MinValue, 0, 24)]
    public void RejectsOutOfBoundsAndOverflowingOffsets(long bodyOffset, long archiveOffset, int size)
    {
        Assert.Empty(CprBinaryFieldCandidateDecoder.Read(Record(bodyOffset), new byte[size], archiveOffset));
    }

    private static byte[] TempoTrackBody()
    {
        var bytes = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), BitConverter.SingleToInt32Bits(-0.5f));
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(8, 8), BitConverter.DoubleToInt64Bits(120));
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(16, 8), BitConverter.DoubleToInt64Bits(960));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(24, 2), 0x1234);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(26, 4), BitConverter.SingleToInt32Bits(3.25f));
        bytes[30] = 0xAB;
        bytes.AsSpan(31).Fill(0xFE);
        return bytes;
    }

    private static void Check(CprBinaryFieldCandidate field, string name, string type,
        string value, long offset, string hex, string reader = "0x1431e0870")
    {
        Assert.Equal(name, field.Field);
        Assert.Equal(type, field.StorageType);
        Assert.Equal(value, field.Value);
        Assert.Equal(offset, field.FileOffset);
        Assert.Equal(hex, field.RawHex);
        Assert.Equal(hex.Length / 2, field.ByteCount);
        Assert.Equal(EvidenceLevel.Candidate, field.Evidence);
        Assert.Contains(reader, field.Source);
        Assert.Contains("b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0", field.Source);
        Assert.Contains("CUBASE15_SERIALIZER_ANALYSIS.md", field.Source);
        Assert.Contains("big-endian candidate", field.Source);
        Assert.Contains("semantics unverified", field.Source);
    }
}
