using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
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
    private const int MaxPrefixClasses = 16;

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

        var symbols = new Dictionary<ulong, Symbol>();
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
            if (!TryRecord(data, position, upper, isRif2, symbols, maxSymbols,
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
            " candidate class framing; references resolve only previously recognized declarations",
            symbols.Count);

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

    private static bool TryRecord(byte[] bytes, int start, int limit, bool wide,
        Dictionary<ulong, Symbol> symbols, int maxSymbols,
        out ParsedRecord parsed, out bool prefixLimit, out bool symbolLimit)
    {
        parsed = default!;
        prefixLimit = false;
        symbolLimit = false;
        var width = wide ? 8 : 4;
        if (start > limit - width) return false;
        var inlineToken = wide ? ulong.MaxValue : uint.MaxValue;
        var prefixToken = inlineToken - 1;
        var referenceBit = wide ? 0x8000000000000000UL : 0x80000000UL;
        var firstToken = Number(bytes, start, wide);
        if (firstToken != inlineToken && firstToken != prefixToken &&
            ((firstToken & referenceBit) == 0 || !symbols.ContainsKey(firstToken & ~referenceBit)))
            return false;
        var p = start;
        var pending = new Dictionary<ulong, Symbol>();
        var symbolLimitReached = false;
        var prefixes = new List<string>();
        while (p <= limit - width && Number(bytes, p, wide) == prefixToken)
        {
            if (prefixes.Count >= MaxPrefixClasses)
            {
                prefixLimit = true;
                return false;
            }
            var symbolOffset = (ulong)p;
            p += width;
            // The native -2 branch reads a full u32-length class name followed
            // by its u16 serialization version in both container modes.
            if (!TryName(bytes, ref p, limit, out var symbol) || !Register(symbolOffset, symbol))
            {
                symbolLimit = symbolLimitReached;
                return false;
            }
            prefixes.Add(symbol.Name);
        }

        if (p > limit - width) return false;
        var token = Number(bytes, p, wide);
        var classOffset = (ulong)p;
        p += width;
        Symbol cls;
        string encoding;
        ulong symbolArchiveOffset;
        if (token == inlineToken)
        {
            if (!TryName(bytes, ref p, limit, out cls) || !Register(classOffset, cls))
            {
                symbolLimit = symbolLimitReached;
                return false;
            }
            symbolArchiveOffset = classOffset;
            encoding = "inline-class-name";
        }
        else if ((token & referenceBit) != 0 && token != prefixToken)
        {
            symbolArchiveOffset = token & ~referenceBit;
            if (!Lookup(symbolArchiveOffset, out cls)) return false;
            encoding = "interned-class-reference";
        }
        else return false;
        if (p > limit - width) return false;
        var length = Number(bytes, p, wide);
        var bodyStart = p + width;
        if (length > (ulong)(limit - bodyStart)) return false;
        var bodyEnd = bodyStart + (int)length;

        // Failed probes must never poison the symbol table for later candidates.
        foreach (var entry in pending) symbols.Add(entry.Key, entry.Value);
        parsed = new ParsedRecord(cls.Name, bodyStart, bodyEnd, encoding, token,
            symbolArchiveOffset, cls.Version, prefixes.AsReadOnly());
        return true;

        bool Lookup(ulong id, out Symbol symbol) =>
            pending.TryGetValue(id, out symbol!) || symbols.TryGetValue(id, out symbol!);

        bool Register(ulong id, Symbol symbol)
        {
            if (Lookup(id, out var existing)) return existing == symbol;
            if (symbols.Count + pending.Count >= maxSymbols)
            {
                symbolLimitReached = true;
                return false;
            }
            pending.Add(id, symbol);
            return true;
        }
    }

    private static bool TryName(byte[] bytes, ref int position, int end, out Symbol symbol)
    {
        symbol = default!;
        if (position > end - 4) return false;
        var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position, 4));
        if (length is < 2 or > 100 || length > end - position - 6) return false;
        var nameStart = position + 4;
        if (bytes[nameStart + (int)length - 1] != 0) return false;
        for (var i = nameStart; i < nameStart + length - 1; i++)
            if (bytes[i] is < 32 or > 126) return false;
        var versionOffset = nameStart + (int)length;
        symbol = new Symbol(Encoding.ASCII.GetString(bytes, nameStart, (int)length - 1),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(versionOffset, 2)));
        position = versionOffset + 2;
        return true;
    }

    private static ulong Number(byte[] bytes, int position, bool wide) => wide
        ? BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(position, 8))
        : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position, 4));

    private sealed record Symbol(string Name, ushort Version);
    private sealed record ParsedRecord(string ClassName, int BodyStart, int BodyEnd,
        string Encoding, ulong Token, ulong SymbolOffset, ushort Version,
        IReadOnlyList<string> PrefixClasses);
    private readonly record struct Frame(int End, int Resume, int Depth, long? Parent);
}
