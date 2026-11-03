using System.Collections.ObjectModel;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Bounded inventory of candidate archive class framing. A containing byte range
/// is not proof of object ownership, and bytes inside an opaque object may happen
/// to resemble a class header. No object state or musical semantics are decoded.
/// </summary>
public static class CprArchiveRecordScanner
{
    private const int MaxRecords = 250_000;
    private const int MaxRetainedRecords = 10_000;
    private const int MaxRanges = 4096;
    private const int MaxSymbols = 250_000;
    private const int MaxDepth = 48;

    public static CprArchiveRecordInventory Scan(string streamName, string? archiveClass,
        byte[] data, long archiveFileOffset, long declaredBytes, bool isRif2,
        CancellationToken ct = default, int maxRetainedRecords = MaxRetainedRecords,
        int maxRecords = MaxRecords, int maxUninterpretedRanges = MaxRanges,
        int maxSymbols = MaxSymbols)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (archiveFileOffset < 0 || archiveFileOffset > long.MaxValue - data.Length)
            throw new ArgumentOutOfRangeException(nameof(archiveFileOffset));
        if (declaredBytes < data.Length)
            throw new ArgumentOutOfRangeException(nameof(declaredBytes));
        if (maxRetainedRecords is < 0 or > MaxRetainedRecords)
            throw new ArgumentOutOfRangeException(nameof(maxRetainedRecords));
        if (maxRecords is < 0 or > MaxRecords)
            throw new ArgumentOutOfRangeException(nameof(maxRecords));
        if (maxUninterpretedRanges is < 0 or > MaxRanges)
            throw new ArgumentOutOfRangeException(nameof(maxUninterpretedRanges));
        if (maxSymbols is < 0 or > MaxSymbols)
            throw new ArgumentOutOfRangeException(nameof(maxSymbols));
        ct.ThrowIfCancellationRequested();

        var headers = new CprArchiveClassHeaderReader();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var records = new List<CprClassRecordCandidate>();
        var ranges = new List<CprUninterpretedRange>();
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var frames = new Stack<Frame>();
        var position = 0;
        var upper = data.Length;
        var depth = 0;
        long? parent = null;
        long unknownBytes = 0;
        var total = 0;
        var steps = 0;
        if (declaredBytes > data.Length)
            reasons.Add("Archive byte scan budget: the declared payload was not read completely.");

        while (true)
        {
            if ((steps++ & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (position >= upper)
            {
                if (frames.Count == 0) break;
                var frame = frames.Pop();
                (position, upper, depth, parent) = (frame.Resume, frame.End, frame.Depth, frame.Parent);
                continue;
            }
            if (total >= maxRecords)
            {
                reasons.Add("Record count limit reached; remaining scanned bytes were not interpreted.");
                AddRemainingUnknown();
                break;
            }
            if (!headers.TryRead(data, position, upper, isRif2, maxSymbols,
                    out var parsed, out var prefixLimit, out var symbolLimit))
            {
                if (symbolLimit)
                {
                    reasons.Add("Class symbol limit reached; remaining scanned bytes were not interpreted.");
                    AddRemainingUnknown();
                    break;
                }
                if (prefixLimit)
                    reasons.Add("Base-class prefix chain limit reached; longer chains were not interpreted.");
                AddUnknown(position, 1);
                position++;
                continue;
            }

            total++;
            counts[parsed.ClassName] = counts.GetValueOrDefault(parsed.ClassName) + 1;
            var headerOffset = archiveFileOffset + position;
            if (records.Count < maxRetainedRecords)
                records.Add(new CprClassRecordCandidate(parsed.ClassName, headerOffset,
                    archiveFileOffset + parsed.BodyStart, parsed.BodyEnd - parsed.BodyStart,
                    depth, parent, parsed.Encoding, parsed.Token, parsed.SymbolOffset,
                    parsed.Version, parsed.PrefixClasses));
            else
                reasons.Add("Record detail retention limit reached; class totals include additional candidates.");

            if (parsed.BodyStart < parsed.BodyEnd && depth < MaxDepth)
            {
                frames.Push(new Frame(upper, parsed.BodyEnd, depth, parent));
                (position, upper, depth, parent) =
                    (parsed.BodyStart, parsed.BodyEnd, depth + 1, headerOffset);
            }
            else
            {
                if (parsed.BodyStart < parsed.BodyEnd)
                {
                    reasons.Add("Nesting depth limit reached; deeper bodies remain opaque.");
                    AddUnknown(parsed.BodyStart, parsed.BodyEnd - parsed.BodyStart);
                }
                position = parsed.BodyEnd;
            }
        }

        return new CprArchiveRecordInventory(streamName, archiveClass, archiveFileOffset,
            declaredBytes, data.Length, total, reasons.Count != 0,
            new ReadOnlyDictionary<string, int>(counts), records.AsReadOnly(), unknownBytes,
            ranges.AsReadOnly(), reasons.Order(StringComparer.Ordinal).ToArray(),
            (isRif2 ? "RIF2 64-bit" : "RIFF 32-bit") +
            " candidate class framing; references resolve recognized symbols or bounded earlier direct declarations; " +
            "future, outside-buffer, chained and malformed reference targets remain opaque",
            headers.SymbolsFound, headers.GetDeclarations(archiveFileOffset));

        void AddRemainingUnknown()
        {
            AddUnknown(position, upper - position);
            while (frames.TryPop(out var remaining))
                AddUnknown(remaining.Resume, remaining.End - remaining.Resume);
        }

        void AddUnknown(int start, int count)
        {
            if (count <= 0) return;
            unknownBytes += count;
            var fileOffset = archiveFileOffset + start;
            if (ranges.Count > 0 && ranges[^1].FileOffset + ranges[^1].ByteCount == fileOffset)
                ranges[^1] = ranges[^1] with { ByteCount = checked(ranges[^1].ByteCount + count) };
            else if (ranges.Count < maxUninterpretedRanges)
                ranges.Add(new CprUninterpretedRange(fileOffset, count));
            else
                reasons.Add("Uninterpreted-range retention limit reached; the byte total includes omitted ranges.");
        }
    }

    private readonly record struct Frame(int End, int Resume, int Depth, long? Parent);
}
