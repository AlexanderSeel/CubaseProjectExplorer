using System.Buffers.Binary;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Wire offsets from the separate MMidiEvent and MMidiNote callbacks. These
/// offsets do not establish musical units, track ownership or release support.
/// </summary>
internal readonly record struct CprMidiNoteLayout(int TailOffset, int AttributeCount, bool SkippedObject = false)
{
    public static bool TryLocate(ReadOnlySpan<byte> body, ushort eventRevision,
        ushort noteRevision, out CprMidiNoteLayout layout) =>
        TryLocate(body, eventRevision, noteRevision, 0, null, out layout, out _);

    public static bool TryLocate(ReadOnlySpan<byte> body, ushort eventRevision,
        ushort noteRevision, long bodyFileOffset, CprMidiEventPrefixReader? prefixReader,
        out CprMidiNoteLayout layout, out string? failure)
    {
        layout = default;
        failure = null;
        var skippedObject = false;
        int prefixBytes, count;
        if (noteRevision > 1) return false;
        if (eventRevision == 2 && prefixReader is not null)
        {
            if (!prefixReader.TryRead(body, bodyFileOffset, out prefixBytes, out skippedObject, out failure)) return false;
            count = (int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(13, 4));
        }
        else if (!TryReadPrefix(body, eventRevision, out prefixBytes, out count)) return false;
        var tailBytes = noteRevision == 0 ? 24 : 25;
        if (body.Length != prefixBytes + tailBytes)
        {
            failure = "Typed MMidiNote suffix does not exactly match its declared revision and attribute boundaries.";
            return false;
        }
        layout = new(prefixBytes, count, skippedObject);
        return true;
    }

    public static bool TryReadPrefix(ReadOnlySpan<byte> body, ushort eventRevision,
        out int prefixBytes, out int count)
    {
        prefixBytes = 0;
        count = 0;
        if (eventRevision > 2) return false;
        prefixBytes = eventRevision switch { 0 => 11, 1 => 13, _ => 17 };
        if (body.Length < prefixBytes) return false;
        if (eventRevision == 2)
        {
            var rawCount = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(13, 4));
            if (rawCount > 16) return false;
            count = (int)rawCount;
        }
        prefixBytes += count * 14;
        if (body.Length < prefixBytes) return false;
        for (var index = 0; index < count; index++)
        {
            var attribute = body.Slice(17 + index * 14, 14);
            // Only the scalar subset has a known fixed stride. Cubase also
            // supports objects, strings and containers; leave those opaque.
            if (attribute[..4].IndexOfAnyExceptInRange((byte)32, (byte)126) >= 0 ||
                BinaryPrimitives.ReadUInt16BigEndian(attribute.Slice(4, 2)) is not (1 or 4)) return false;
        }
        return true;
    }

}

/// <summary>Unambiguous named revision and the first point it was recognized.</summary>
internal readonly record struct CprClassRevisionEvidence(ushort Revision, long AvailableAtFileOffset)
{
    public ushort? RevisionAt(long bodyFileOffset) =>
        bodyFileOffset >= AvailableAtFileOffset ? Revision : null;

    public static CprClassRevisionEvidence? Find(IReadOnlyList<CprClassDeclarationCandidate>? declarations,
        string className)
    {
        ushort? revision = null;
        var firstAvailable = long.MaxValue;
        if (declarations is null) return null;
        foreach (var declaration in declarations)
        {
            if (!string.Equals(declaration.ClassName, className, StringComparison.Ordinal)) continue;
            if (declaration.AvailableAtFileOffset is not long available) return null;
            if (revision is ushort previous && previous != declaration.ClassVersion) return null;
            revision = declaration.ClassVersion;
            firstAvailable = Math.Min(firstAvailable, available);
        }
        // Missing or conflicting evidence must never become a guessed v0.
        return revision is ushort value ? new(value, firstAvailable) : null;
    }
}
