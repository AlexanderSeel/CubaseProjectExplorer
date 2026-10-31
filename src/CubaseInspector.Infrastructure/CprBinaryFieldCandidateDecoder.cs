using System.Buffers.Binary;
using System.Globalization;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Exposes anonymous scalar observations for narrowly bounded, binary-derived
/// layouts. These fields have no verified project ownership or musical units.
/// </summary>
public static class CprBinaryFieldCandidateDecoder
{
    private const string TempoSource =
        "Cubase15.exe reader 0x1431e0870 / writer 0x1431e08f0; " +
        "SHA-256 b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0; " +
        "MTempoEvent serialization version 0; big-endian candidate; " +
        "field semantics unverified; docs/research/CUBASE15_SERIALIZER_ANALYSIS.md";
    private const string TempoTrackSource =
        "Cubase15.exe reader 0x1431e2e00 / writer 0x1431e30f0; " +
        "SHA-256 b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0; " +
        "MTempoTrackEvent serialization version 3, single-entry 54-byte layout; " +
        "big-endian candidate; field semantics unverified; suffix attributes remain opaque; " +
        "docs/research/CUBASE15_SERIALIZER_ANALYSIS.md";

    public static IReadOnlyList<CprBinaryFieldCandidate> Read(CprClassRecordCandidate record,
        ReadOnlySpan<byte> archiveData, long archiveFileOffset)
    {
        ArgumentNullException.ThrowIfNull(record);
        var isTempoEvent = string.Equals(record.ClassName, "MTempoEvent", StringComparison.Ordinal) &&
            record.ClassVersion == 0 && record.BodyBytes == 24;
        var isTempoTrack = string.Equals(record.ClassName, "MTempoTrackEvent", StringComparison.Ordinal) &&
            record.ClassVersion == 3 && record.BodyBytes == 54;
        if ((!isTempoEvent && !isTempoTrack) || archiveData.Length < record.BodyBytes ||
            archiveFileOffset < 0 || archiveFileOffset > long.MaxValue - archiveData.Length ||
            record.BodyFileOffset < archiveFileOffset)
            return [];
        var relative = record.BodyFileOffset - archiveFileOffset;
        if (relative > archiveData.Length - record.BodyBytes) return [];
        var body = archiveData.Slice((int)relative, (int)record.BodyBytes);

        if (isTempoTrack)
        {
            // Only the exact single-entry fixture layout is supported. Neither
            // arbitrary arrays nor the trailing versioned attributes are decoded.
            if (BinaryPrimitives.ReadUInt32BigEndian(body[..4]) != 1) return [];
            return Array.AsReadOnly(new[]
            {
                Field("body+0", "u32-be", "1", record.BodyFileOffset, body[..4], TempoTrackSource),
                Field("body+4", "f32-be", BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32BigEndian(body.Slice(4, 4))).ToString("R", CultureInfo.InvariantCulture),
                    record.BodyFileOffset + 4, body.Slice(4, 4), TempoTrackSource),
                Field("body+8", "f64-be", BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64BigEndian(body.Slice(8, 8))).ToString("R", CultureInfo.InvariantCulture),
                    record.BodyFileOffset + 8, body.Slice(8, 8), TempoTrackSource),
                Field("body+16", "f64-be", BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64BigEndian(body.Slice(16, 8))).ToString("R", CultureInfo.InvariantCulture),
                    record.BodyFileOffset + 16, body.Slice(16, 8), TempoTrackSource),
                Field("body+24", "u16-be", BinaryPrimitives.ReadUInt16BigEndian(body.Slice(24, 2))
                    .ToString(CultureInfo.InvariantCulture), record.BodyFileOffset + 24, body.Slice(24, 2), TempoTrackSource),
                Field("body+26", "f32-be", BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32BigEndian(body.Slice(26, 4))).ToString("R", CultureInfo.InvariantCulture),
                    record.BodyFileOffset + 26, body.Slice(26, 4), TempoTrackSource),
                Field("body+30", "u8", body[30].ToString(CultureInfo.InvariantCulture),
                    record.BodyFileOffset + 30, body.Slice(30, 1), TempoTrackSource)
            });
        }

        // Keep the original bits, including NaN payloads, infinities and signed
        // zero. A readable numeric rendering alone cannot preserve all evidence.
        return Array.AsReadOnly(new[]
        {
            Field("body+0", "f32-be", BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32BigEndian(body[..4])).ToString("R", CultureInfo.InvariantCulture),
                record.BodyFileOffset, body[..4]),
            Field("body+4", "f64-be", BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64BigEndian(body.Slice(4, 8))).ToString("R", CultureInfo.InvariantCulture),
                record.BodyFileOffset + 4, body.Slice(4, 8)),
            Field("body+12", "f64-be", BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64BigEndian(body.Slice(12, 8))).ToString("R", CultureInfo.InvariantCulture),
                record.BodyFileOffset + 12, body.Slice(12, 8)),
            Field("body+20", "u32-be", BinaryPrimitives.ReadUInt32BigEndian(body.Slice(20, 4))
                .ToString(CultureInfo.InvariantCulture), record.BodyFileOffset + 20, body.Slice(20, 4))
        });
    }

    private static CprBinaryFieldCandidate Field(string field, string type, string value,
        long offset, ReadOnlySpan<byte> bytes, string source = TempoSource) => new(field, type, value,
            offset, bytes.Length, source, RawHex: Convert.ToHexString(bytes));
}
