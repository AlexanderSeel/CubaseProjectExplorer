using System.Buffers.Binary;
using System.Globalization;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Anonymous automation-track prefix only; inherited MTrack data stays opaque.</summary>
internal static class CprAutomationTrackFieldReader
{
    private const string Source =
        "Cubase15.exe MAutomationTrack reader 0x142251240 / writer 0x1422514d0; " +
        "SHA-256 b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0; " +
        "big-endian candidate; field semantics unverified; inherited MTrack body remains opaque; " +
        "no parameter target, mode or owner is inferred; " +
        "docs/research/CUBASE15_C_AUTOMATION_PREFIXES.md";

    public static CprBinaryFieldReadResult Read(ReadOnlySpan<byte> body, long fileOffset,
        ushort revision, int maximumFields)
    {
        if (revision > 2 || body.Length < 4) return new([], false);
        var count = 0;
        var columns = revision == 0 ? 2 : 3;
        var stride = revision == 0 ? 8 : 10;
        var totalFields = 2L;
        if (revision < 2)
        {
            // The signed count and complete fixed table plus two trailing u16s
            // must fit before any fields are retained, even at a zero budget.
            count = BinaryPrimitives.ReadInt32BigEndian(body);
            if (count < 0 || body.Length < 8 || count > (body.Length - 8) / stride)
                return new([], false);
            totalFields = 3L + (long)count * columns;
        }
        var retained = (int)Math.Min(totalFields, maximumFields);
        if (retained == 0) return new([], true);
        var fields = new CprBinaryFieldCandidate[retained];
        var source = Source + $"; serializer revision {revision}; fixed prefix only, not a complete object load.";
        for (var index = 0; index < retained; index++)
        {
            int offset, bytes;
            string type, value;
            if (revision < 2 && index == 0)
            {
                offset = 0;
                bytes = 4;
                type = "i32-be";
                value = count.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                var row = (index - 1) / columns;
                var column = (index - 1) % columns;
                var inTable = revision < 2 && row < count;
                offset = revision == 2 ? index * 2 : inTable ? 4 + row * stride + column * 4
                    : 4 + count * stride + (index - 1 - count * columns) * 2;
                bytes = inTable && column < 2 ? 4 : 2;
                type = bytes == 2 ? "u16-be" : column == 1 ? "f32-be" : "u32-be";
                var raw = body.Slice(offset, bytes);
                value = type switch
                {
                    "u16-be" => BinaryPrimitives.ReadUInt16BigEndian(raw).ToString(CultureInfo.InvariantCulture),
                    "u32-be" => BinaryPrimitives.ReadUInt32BigEndian(raw).ToString(CultureInfo.InvariantCulture),
                    _ => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(raw))
                        .ToString("R", CultureInfo.InvariantCulture)
                };
            }
            fields[index] = new($"body+{offset}", type, value, fileOffset + offset, bytes,
                source, RawHex: Convert.ToHexString(body.Slice(offset, bytes)));
        }
        return new(Array.AsReadOnly(fields), retained < totalFields);
    }
}
