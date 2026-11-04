using System.Buffers.Binary;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Opaque primitive-property subset of 0x14456D470 / 0x1445DCE10. Does not
/// instantiate the serialized type or interpret property names and values.
/// </summary>
internal static class CprAttributeContainerBoundaryReader
{
    private const int MaximumProperties = 256;
    private const int MaximumBytes = 64 * 1024;
    private const int MaximumWireStringBytes = 50_000;
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public static bool TrySkip(ReadOnlySpan<byte> data, ref int position, out string? failure)
    {
        failure = "Container requires a 32-digit hexadecimal type token and a bounded primitive property table.";
        if (position < 0 || position > data.Length) return false;
        // Work on a bounded local cursor; failure never advances the caller.
        var body = data.Slice(position, Math.Min(MaximumBytes, data.Length - position));
        var cursor = 0;
        if (!TryString(body, ref cursor, out var typeToken) || !IsTypeToken(typeToken) ||
            cursor > body.Length - 4) return false;
        var count = BinaryPrimitives.ReadInt32BigEndian(body.Slice(cursor, 4));
        cursor += 4;
        if (count is < 0 or > MaximumProperties)
        {
            failure = "Container property count is unsupported or exceeds the 256-item limit.";
            return false;
        }
        for (var index = 0; index < count; index++)
        {
            if (!TryString(body, ref cursor, out _) || cursor > body.Length - 2)
            {
                failure = "Container property header exceeds the available bytes or 64 KiB container budget.";
                return false;
            }
            var type = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(cursor, 2));
            cursor += 2;
            if (type is 1 or 4)
            {
                if (cursor > body.Length - 8)
                {
                    failure = "Container scalar exceeds the available bytes or 64 KiB container budget.";
                    return false;
                }
                cursor += 8;
            }
            else if (type == 8)
            {
                if (!TryString(body, ref cursor, out _))
                {
                    failure = "Container string exceeds its supported length, available bytes or 64 KiB container budget.";
                    return false;
                }
            }
            else
            {
                failure = $"Container property type 0x{type:X4} is unsupported; no later property boundary was inferred.";
                return false;
            }
        }
        position += cursor;
        failure = null;
        return true;
    }

    private static bool TryString(ReadOnlySpan<byte> data, ref int cursor, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (cursor > data.Length - 4) return false;
        var length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(cursor, 4));
        cursor += 4;
        if (length is < 0 or > MaximumWireStringBytes || length > data.Length - cursor) return false;
        value = data.Slice(cursor, length);
        cursor += length;
        return true;
    }

    private static bool IsTypeToken(ReadOnlySpan<byte> value)
    {
        // 0x14458AAC0 recognizes a UTF-8 BOM at either end. No string is
        // allocated, decoded through a codepage, or used as a plug-in identity.
        if (value.Length > 3 && value.EndsWith(Utf8Bom)) value = value[..^3];
        else if (value.Length > 3 && value.StartsWith(Utf8Bom)) value = value[3..];
        if (value.Length > 0 && value[^1] == 0) value = value[..^1];
        if (value.Length != 32) return false;
        foreach (var digit in value)
            if (digit is not (>= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'F' or
                >= (byte)'a' and <= (byte)'f')) return false;
        return true;
    }
}
