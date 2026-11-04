using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Raw MMarkerEvent prefix only; inherited event data and marker semantics stay opaque.</summary>
internal static class CprMarkerFieldReader
{
    private const int MaximumWireStringBytes = 50_000;
    internal const int MaximumRetainedStringBytes = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];
    private const string Source =
        "Cubase15.exe MMarkerEvent reader 0x1426fc400 / writer 0x1426fc470; " +
        "string reader 0x14458aac0; " +
        "SHA-256 b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0; " +
        "serializer revision 0; big-endian candidate; field semantics unverified; " +
        "inherited MEvent body remains opaque; no marker ID, position or ownership is inferred; " +
        "docs/research/CUBASE15_C_MARKER_PREFIX.md";

    public static CprBinaryFieldReadResult Read(ReadOnlySpan<byte> body, long fileOffset, int maximumFields)
    {
        if (body.Length < 8) return new([], false);
        var stringBytes = BinaryPrimitives.ReadInt32BigEndian(body);
        // The complete string and following u32 must fit before any fields are
        // published. Never search for a plausible scalar after an invalid length.
        if (stringBytes < 0 || stringBytes > MaximumWireStringBytes || stringBytes > body.Length - 8)
            return new([], false);
        var hasString = stringBytes > 0;
        var retainString = hasString && stringBytes <= MaximumRetainedStringBytes;
        var available = retainString ? 3 : 2;
        var retained = Math.Min(available, maximumFields);
        var partial = retained < (hasString ? 3 : 2);
        if (retained == 0) return new([], partial);
        var source = hasString && !retainString
            ? Source + "; string payload omitted: exceeds the 4096-byte raw-string retention limit."
            : Source;
        var fields = new CprBinaryFieldCandidate[retained];
        fields[0] = Field(0, "i32-be", stringBytes.ToString(CultureInfo.InvariantCulture), body[..4], fileOffset, source);
        var next = 1;
        if (retainString && next < retained)
        {
            var raw = body.Slice(4, stringBytes);
            var (type, value) = RenderString(raw);
            fields[next++] = Field(4, type, value, raw, fileOffset, source);
        }
        if (next < retained)
        {
            var offset = 4 + stringBytes;
            var raw = body.Slice(offset, 4);
            fields[next] = Field(offset, "u32-be", BinaryPrimitives.ReadUInt32BigEndian(raw)
                .ToString(CultureInfo.InvariantCulture), raw, fileOffset, source);
        }
        return new(Array.AsReadOnly(fields), partial);
    }

    private static (string Type, string Value) RenderString(ReadOnlySpan<byte> raw)
    {
        // The native codec recognizes UTF-8 BOMs at either end. Unmarked high
        // bytes may use a legacy codepage; preserve those bytes without guessing.
        var text = raw;
        var utf8 = false;
        if (text.Length > 3 && text.EndsWith(Utf8Bom)) { text = text[..^3]; utf8 = true; }
        else if (text.Length > 3 && text.StartsWith(Utf8Bom)) { text = text[3..]; utf8 = true; }
        if (text.Length > 0 && text[^1] == 0)
        {
            text = text[..^1];
            if (utf8 || text.IndexOfAnyExceptInRange((byte)32, (byte)126) < 0)
            {
                try
                {
                    var value = StrictUtf8.GetString(text);
                    if (!value.Any(char.IsControl)) return (utf8 ? "utf8-string-bytes" : "ascii-string-bytes", value);
                }
                catch (DecoderFallbackException) { }
            }
        }
        return ("opaque-string-bytes", $"Uninterpreted string payload ({raw.Length} bytes)");
    }

    private static CprBinaryFieldCandidate Field(int offset, string type, string value,
        ReadOnlySpan<byte> raw, long fileOffset, string source) =>
        new($"body+{offset}", type, value, fileOffset + offset, raw.Length, source,
            RawHex: Convert.ToHexString(raw));
}
