using System.Buffers.Binary;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Wire offsets from the separate MMidiEvent and MMidiNote callbacks. These
/// offsets do not establish musical units, track ownership or release support.
/// </summary>
internal readonly record struct CprMidiNoteLayout(int TailOffset, int AttributeCount)
{
    public static bool TryLocate(ReadOnlySpan<byte> body, ushort eventRevision,
        ushort noteRevision, out CprMidiNoteLayout layout)
    {
        layout = default;
        if (noteRevision > 1 || !TryReadPrefix(body, eventRevision, out var prefixBytes, out var count)) return false;
        var tailBytes = noteRevision == 0 ? 24 : 25;
        if (body.Length != prefixBytes + tailBytes) return false;
        layout = new(prefixBytes, count);
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
