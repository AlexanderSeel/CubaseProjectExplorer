using System.Text;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Extracts candidate Cubase plug-in names and UIDs from the bounded native CPR
/// "Plugin UID" property sequence. This is a token reader, not a validated plugin
/// instance/track topology decoder. Cubase may encode VST2 and VST3 UIDs alike.
/// Layout research: Cubase Project Plugins (MIT), src/reader.rs, plus public
/// Steinberg forum examples of "Original Plugin Name" after renamed instruments.
/// </summary>
public static class CprPluginMetadataReader
{
    private static readonly byte[] Marker = "Plugin UID\0"u8.ToArray();
    public const int MaxInstances = 10_000;
    public const int MaxWindow = 768;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <param name="baseOffset">Original file offset of the first supplied byte.</param>
    /// <param name="markerStartLimit">Only start parsing markers before this position;
    /// the remaining bytes are read-only lookahead to support streaming.</param>
    /// <param name="maxResults">Remaining safe token budget across scan windows.</param>
    public static IReadOnlyList<PluginReference> Extract(ReadOnlySpan<byte> bytes,
        long baseOffset = 0, int? markerStartLimit = null, int maxResults = MaxInstances)
    {
        var result = new List<PluginReference>();
        var position = 0;
        var startLimit = Math.Clamp(markerStartLimit ?? bytes.Length, 0, bytes.Length);
        while (position < startLimit && result.Count < Math.Min(maxResults, MaxInstances))
        {
            var relative = bytes[position..].IndexOf(Marker);
            if (relative < 0) break;
            var offset = position + relative;
            if (offset >= startLimit) break;
            position = offset + Marker.Length;
            var sourceOffset = checked(baseOffset + offset);
            var end = Math.Min(bytes.Length, offset + MaxWindow);
            // Never associate metadata from a *different* Plugin UID record.
            var next = bytes[position..end].IndexOf(Marker);
            if (next >= 0) end = position + next;
            var region = bytes[..end];
            PluginReference candidate;
            if (TryParse(region, offset, out var id, out var name, out var originalName))
                candidate = new PluginReference(id!, originalName ?? name!, null,
                    "CPR UID (format unverified)", null, EvidenceLevel.Candidate,
                    Role: originalName is not null
                        ? $"Cubase Plugin UID and typed Plugin Name fields at 0x{sourceOffset:X}; original name retained (renamed display: {name})"
                        : $"Cubase Plugin UID and typed Plugin Name fields at 0x{sourceOffset:X}", SourceByteOffset: sourceOffset);
            else if (TryUidOnly(region, offset) is { } storedUid)
                candidate = new PluginReference(storedUid, "Name not decoded", null,
                    "CPR UID (name not decoded)", null, EvidenceLevel.Candidate,
                    Role: $"Cubase Plugin UID field at 0x{sourceOffset:X}; no typed Plugin Name recovered",
                    SourceByteOffset: sourceOffset);
            else
                candidate = new PluginReference($"offset-{sourceOffset:X}",
                    $"Unresolved plugin UID @ 0x{sourceOffset:X}", null,
                    "CPR UID (unresolved)", null, EvidenceLevel.Candidate,
                    Role: "Plugin UID marker found; typed property layout not recognized", SourceByteOffset: sourceOffset);
            result.Add(candidate);
        }
        return result;
    }

    private static string? TryUidOnly(ReadOnlySpan<byte> bytes, int markerOffset)
    {
        // A missing/renamed Plugin Name field must not discard an otherwise
        // recognisable stored UID. The UID-only result remains Candidate evidence.
        var first = markerOffset + Marker.Length;
        var expected = first + 22;
        if (TryToken(bytes, expected, out var typed, out _) &&
            Vst3ClassIdentity.Canonical(typed) is { } known)
            return known;
        var limit = Math.Min(bytes.Length, first + 80);
        for (var i = first; i < limit; i++)
        {
            if (TryToken(bytes, i, out var value, out _) &&
                Vst3ClassIdentity.Canonical(value) is { } cid)
                return cid;
        }
        return null;
    }

    private static bool TryParse(ReadOnlySpan<byte> bytes, int offset, out string? id,
        out string? name, out string? original)
    {
        id = name = original = null;
        // Cubase 4–14 plugin property structure: marker, 22 bytes of framing,
        // length-prefixed UID, 3-byte framing, length-prefixed "Plugin Name",
        // 5-byte framing, length-prefixed display name, 3-byte framing, optional
        // "Original Plugin Name" property for renamed instrument tracks.
        var cursor = offset + Marker.Length + 22;
        if (TryToken(bytes, cursor, out var uid, out var next) &&
            Vst3ClassIdentity.Canonical(uid) is { } cid &&
            TryToken(bytes, next + 3, out var key, out var nameKeyEnd) &&
            key == "Plugin Name" &&
            TryToken(bytes, nameKeyEnd + 5, out var display, out var displayEnd) &&
            IsPlausibleName(display))
        {
            id = cid;
            name = display;
            TryOriginalName(bytes, displayEnd + 3, out original);
            return true;
        }
        // Some versions move framing fields. Scan only within THIS property
        // window for a nearby UID followed by the typed Plugin Name property.
        // The fallback remains candidate-only and never guesses a VST format.
        var scanStart = offset + Marker.Length;
        var scanEnd = Math.Min(bytes.Length, offset + 256);
        for (var p = scanStart; p < scanEnd; p++)
        {
            if (!TryToken(bytes, p, out var maybeGuid, out var afterGuid) ||
                Vst3ClassIdentity.Canonical(maybeGuid) is not { } guid) continue;
            for (var q = afterGuid; q <= Math.Min(afterGuid + 12, scanEnd); q++)
            {
                if (!TryToken(bytes, q, out var field, out var fieldEnd) ||
                    field != "Plugin Name") continue;
                for (var n = fieldEnd; n <= Math.Min(fieldEnd + 12, scanEnd); n++)
                {
                    if (!TryToken(bytes, n, out var value, out var valueEnd) ||
                        !IsPlausibleName(value)) continue;
                    id = guid;
                    name = value;
                    // The name may be a renamed instrument track; prefer the
                    // original plugin name only when a typed key is present.
                    for (var t = valueEnd; t <= Math.Min(valueEnd + 12, scanEnd); t++)
                    {
                        TryOriginalName(bytes, t, out original);
                        if (original is not null) break;
                    }
                    return true;
                }
            }
        }
        return false;
    }

    private static void TryOriginalName(ReadOnlySpan<byte> bytes, int offset,
        out string? original)
    {
        original = null;
        if (!TryToken(bytes, offset, out var field, out var valueStart) ||
            field != "Original Plugin Name") return;
        // Recorded layouts use 5 framing bytes before the original name.
        for (var p = valueStart; p <= Math.Min(valueStart + 8, bytes.Length - 1); p++)
            if (TryToken(bytes, p, out var value, out _) && IsPlausibleName(value))
            {
                original = value;
                return;
            }
    }

    private static bool TryToken(ReadOnlySpan<byte> bytes, int offset,
        out string value, out int next)
    {
        value = "";
        next = offset;
        if (offset < 0 || offset >= bytes.Length) return false;
        var length = bytes[offset];
        if (length is < 2 or > 192 || length > bytes.Length - offset - 1) return false;
        var token = bytes.Slice(offset + 1, length);
        var zero = token.IndexOf((byte)0);
        if (zero >= 0) token = token[..zero];
        if (token.IsEmpty || token.IndexOf((byte)0) >= 0) return false;
        try { value = StrictUtf8.GetString(token); }
        catch (DecoderFallbackException) { return false; }
        if (value.Any(char.IsControl)) return false;
        next = offset + length + 1;
        return true;
    }

    private static bool IsPlausibleName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 140 &&
        value.Any(char.IsLetter) && value.Trim() == value &&
        !value.Equals("Plugin Name", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("Original Plugin Name", StringComparison.OrdinalIgnoreCase);
}
