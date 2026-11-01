using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

public sealed record CprMidiPartNoteScan(IReadOnlyList<CprRif2MidiNoteCandidate> Notes,
    int BytesScanned, bool Partial);

/// <summary>
/// Bounded observations of packed notes inside an already bounded RIFF/RIF2 MMidiPart.
/// Layout evidence: BandPass Records' MIT Cubase 13 parser at commit 6d5d82ac.
/// This independently written scanner preserves raw timing and velocity, including
/// zero velocity and negative positions. It does not trim, round or export notes.
/// </summary>
public static class CprMidiPartNoteScanner
{
    public const int MaximumScanBytes = 4 * 1024 * 1024;
    public const int MaximumNotes = 10_000;
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private const string Source = "RIFF MMidiPart packed-note candidate; layout research: " +
        "bandpassrecords/daw-project-manager@6d5d82ac352ddb83212e1240612e24a252d03d5c. " +
        "Exact discriminator and complete 25-byte note suffix: Cubase15.exe readers 0x141bc6e90 / 0x141bb4690, " +
        "factory 0x141bb14b0 (b4a9c748 SHA-256 build). " +
        "Timing units, part placement and musical ownership remain unverified.";

    public static CprMidiPartNoteScan Scan(ReadOnlySpan<byte> body, long bodyFileOffset,
        int maximumNotes = MaximumNotes, int maximumBytes = MaximumScanBytes,
        CancellationToken cancellationToken = default, bool isRif2 = false,
        IReadOnlyList<CprUninterpretedRange>? excludedRecords = null)
    {
        if (bodyFileOffset < 0 || bodyFileOffset > long.MaxValue - body.Length)
            throw new ArgumentOutOfRangeException(nameof(bodyFileOffset));
        if (maximumNotes is < 0 or > MaximumNotes) throw new ArgumentOutOfRangeException(nameof(maximumNotes));
        if (maximumBytes is < 0 or > MaximumScanBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        cancellationToken.ThrowIfCancellationRequested();
        var scanned = Math.Min(body.Length, maximumBytes);
        var notes = new List<CprRif2MidiNoteCandidate>();
        var data = body[..scanned];
        if (!TryName(data, out var partName, out var nameEnd))
            return new(notes, scanned, scanned < body.Length);
        var offset = nameEnd;
        var excludedIndex = 0;
        var steps = 0;
        if (excludedRecords is { Count: > 0 })
        {
            var lo = 0; var hi = excludedRecords.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                var range = excludedRecords[mid];
                if (range.FileOffset + range.ByteCount <= bodyFileOffset) lo = mid + 1;
                else hi = mid;
            }
            excludedIndex = lo;
        }
        while (offset <= data.Length - 57)
        {
            if ((steps++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            while (excludedRecords is not null && excludedIndex < excludedRecords.Count &&
                   excludedRecords[excludedIndex].FileOffset + excludedRecords[excludedIndex].ByteCount <= bodyFileOffset + offset)
                excludedIndex++;
            if (excludedRecords is not null && excludedIndex < excludedRecords.Count &&
                excludedRecords[excludedIndex].FileOffset <= bodyFileOffset + offset)
            {
                var range = excludedRecords[excludedIndex++];
                offset = (int)Math.Clamp(range.FileOffset + range.ByteCount - bodyFileOffset, offset + 1, data.Length);
                continue;
            }
            if (notes.Count >= maximumNotes) return new(notes.AsReadOnly(), scanned, true);
            // Status + f64 position + channel/pitch/velocity + u16 flags +
            // u32 attribute count. Each scalar attribute is tag4 + type2 + value8.
            var header = data.Slice(offset, 18);
            var rawCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(14, 4));
            if (header[0] is not (0x80 or 0x90) || header[9] > 15 || header[10] > 127 ||
                header[11] > 127 || rawCount is < 1 or > 16)
            { offset++; continue; }
            var count = (int)rawCount;
            var durationOffset = offset + 18 + count * 14;
            if (durationOffset > data.Length - 25) { offset++; continue; }
            if (excludedRecords is not null && excludedIndex < excludedRecords.Count &&
                excludedRecords[excludedIndex].FileOffset < bodyFileOffset + durationOffset + 25)
            { offset++; continue; }
            var attributesValid = true;
            for (var index = 0; index < count; index++)
            {
                var attribute = data.Slice(offset + 18 + index * 14, 14);
                var type = BinaryPrimitives.ReadUInt16BigEndian(attribute.Slice(4, 2));
                if (type is not (1 or 4) || attribute[..4].IndexOfAnyExceptInRange((byte)32, (byte)126) >= 0)
                { attributesValid = false; break; }
            }
            var position = ReadDouble(header.Slice(1, 8));
            var duration = ReadDouble(data.Slice(durationOffset, 8));
            if (!attributesValid || !double.IsFinite(position) || !double.IsFinite(duration) ||
                Math.Abs(position) >= 1e9 || duration is < 0 or >= 1e8)
            { offset++; continue; }
            notes.Add(new CprRif2MidiNoteCandidate(bodyFileOffset + offset, position,
                duration, header[10], header[11], Source.Replace("RIFF", isRif2 ? "RIF2" : "RIFF"),
                isRif2 ? "RIF2 packed MMidiPart" : "RIFF packed MMidiPart",
                CandidatePartName: partName, Channel: header[9],
                RawFlags: BinaryPrimitives.ReadUInt16BigEndian(header.Slice(12, 2))));
            offset = durationOffset + 25;
        }
        return new(notes.AsReadOnly(), scanned, scanned < body.Length);
    }

    private static double ReadDouble(ReadOnlySpan<byte> data) =>
        BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data));

    public static bool TryReadRecord(ReadOnlySpan<byte> body, long bodyFileOffset,
        bool isRif2, out CprRif2MidiNoteCandidate candidate, ushort? classVersion = null,
        ushort? eventClassVersion = null)
    {
        candidate = default!;
        // This musical-field matcher supports only the fixture-backed modern
        // combination. Older layouts remain anonymous raw-field observations.
        // Span-only callers can match a shape; the project inspector additionally
        // requires the archive's explicit, unambiguous MMidiEvent declaration.
        if (classVersion is ushort revision && revision != 1) return false;
        if (eventClassVersion is ushort baseRevision && baseRevision != 2) return false;
        if (bodyFileOffset < 0 || bodyFileOffset > long.MaxValue - body.Length ||
            !CprMidiNoteLayout.TryLocate(body, 2, 1, out var wireLayout) ||
            body[8] > 15 || body[9] > 127 || body[10] > 127) return false;
        var position = ReadDouble(body[..8]);
        var duration = ReadDouble(body.Slice(wireLayout.TailOffset, 8));
        if (!double.IsFinite(position) || !double.IsFinite(duration) || Math.Abs(position) >= 1e9 ||
            duration is < 0 or >= 1e8) return false;
        var layout = (isRif2 ? "RIF2" : "RIFF") +
            (isRif2 && body.Length == 112 ? " MMidiNote/112-byte" : " MMidiNote/typed-attributes");
        candidate = new CprRif2MidiNoteCandidate(bodyFileOffset, position, duration, body[9], body[10],
            "MMidiNote candidate with bounded typed attributes; class/body framing, timing units and ownership require per-version comparison. " +
            "Layout sources: BandPass Records Cubase parser@6d5d82ac; local Cubase 14/15 bytes; Cubase15.exe MMidiEvent reader 0x141BB0390, MMidiNote reader 0x141BB4690 (b4a9c748 SHA-256 build).",
            layout, Channel: body[8], RawFlags: BinaryPrimitives.ReadUInt16BigEndian(body.Slice(11, 2)));
        return true;
    }

    internal static bool TryName(ReadOnlySpan<byte> data, out string? name, out int next)
    {
        name = null;
        next = 0;
        if (data.Length < 4) return false;
        var length = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (length is < 1 or > 4096 || length > data.Length - 4) return false;
        var value = data.Slice(4, (int)length);
        if (value.EndsWith(Utf8Bom)) value = value[..^3];
        if (value.Length == 0 || value[^1] != 0) return false;
        try { name = new UTF8Encoding(false, true).GetString(value[..^1]); }
        catch (DecoderFallbackException) { return false; }
        if (name.Any(char.IsControl)) return false;
        next = 4 + (int)length;
        return true;
    }
}
