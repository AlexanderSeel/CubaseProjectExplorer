using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// One archive's bounded candidate class grammar, shared by inventory and
/// arrangement traversal. A recognized header is not a deserialized object.
/// Successful probes atomically retain symbol revisions and recognition points;
/// failed probes leave the state unchanged.
/// </summary>
internal sealed class CprArchiveClassHeaderReader
{
    private const int MaxPrefixClasses = 16;
    private readonly Dictionary<ulong, Symbol> _symbols = new();
    private readonly Dictionary<ulong, int> _symbolAvailability = new();

    public int SymbolsFound => _symbols.Count;

    public IReadOnlyList<CprClassDeclarationCandidate> GetDeclarations(long archiveFileOffset) =>
        Array.AsReadOnly(_symbols.OrderBy(entry => entry.Key)
            .Select(entry => new CprClassDeclarationCandidate(entry.Value.Name, entry.Value.Version,
                entry.Key, archiveFileOffset + checked((long)entry.Key),
                AvailableAtFileOffset: archiveFileOffset + _symbolAvailability[entry.Key])).ToArray());

    public bool TryRead(byte[] bytes, int start, int limit, bool wide, int maxSymbols,
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
        if (firstToken != inlineToken && firstToken != prefixToken)
        {
            if ((firstToken & referenceBit) == 0) return false;
            var target = firstToken & ~referenceBit;
            // Reject random scalar bytes before allocating probe state. Lazy
            // lookup never follows references, future offsets or unread bytes.
            if (!_symbols.ContainsKey(target))
            {
                if (target >= (ulong)start || target > (ulong)(bytes.Length - width)) return false;
                var declarationToken = Number(bytes, (int)target, wide);
                if (declarationToken != inlineToken && declarationToken != prefixToken) return false;
            }
        }
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
            if (!Lookup(symbolArchiveOffset, out cls))
            {
                if (symbolArchiveOffset >= (ulong)start ||
                    !TryDirectDeclaration(bytes, symbolArchiveOffset, start, wide, _symbols, pending, out cls) ||
                    !Register(symbolArchiveOffset, cls))
                {
                    symbolLimit = symbolLimitReached;
                    return false;
                }
            }
            encoding = "interned-class-reference";
        }
        else return false;
        if (p > limit - width) return false;
        var length = Number(bytes, p, wide);
        var bodyStart = p + width;
        if (length > (ulong)(limit - bodyStart)) return false;
        var bodyEnd = bodyStart + (int)length;

        // Failed probes must never poison the symbol table for later candidates.
        foreach (var entry in pending)
        {
            _symbols.Add(entry.Key, entry.Value);
            _symbolAvailability.Add(entry.Key, bodyStart);
        }
        parsed = new ParsedRecord(cls.Name, bodyStart, bodyEnd, encoding, token,
            symbolArchiveOffset, cls.Version, prefixes.AsReadOnly());
        return true;

        bool Lookup(ulong id, out Symbol symbol) =>
            pending.TryGetValue(id, out symbol!) || _symbols.TryGetValue(id, out symbol!);

        bool Register(ulong id, Symbol symbol)
        {
            if (Lookup(id, out var existing)) return existing == symbol;
            if (_symbols.Count + pending.Count >= maxSymbols)
            {
                symbolLimitReached = true;
                return false;
            }
            pending.Add(id, symbol);
            return true;
        }
    }

    // Cubase's class reader can seek to a declaration absent from its table.
    // This bounded variant reads direct declarations only, without recursively
    // chasing references. A -2 target names that exact base-class symbol; it
    // never creates another object body or changes the referencing body's size.
    private static bool TryDirectDeclaration(byte[] bytes, ulong target, int referenceStart, bool wide,
        Dictionary<ulong, Symbol> knownSymbols, Dictionary<ulong, Symbol> pending, out Symbol symbol)
    {
        symbol = default!;
        var width = wide ? 8 : 4;
        if (target > (ulong)(bytes.Length - width)) return false;
        var p = (int)target;
        var inlineToken = wide ? ulong.MaxValue : uint.MaxValue;
        var prefixToken = inlineToken - 1;
        var referenceBit = wide ? 0x8000000000000000UL : 0x80000000UL;
        var prefixes = 0;
        while (p <= referenceStart - width)
        {
            var token = Number(bytes, p, wide);
            p += width;
            if (token == inlineToken || token == prefixToken)
            {
                if (!TryName(bytes, ref p, referenceStart, out var declared)) return false;
                if (symbol is null) symbol = declared;
                if (token == prefixToken)
                {
                    if (++prefixes > MaxPrefixClasses) return false;
                    continue;
                }
            }
            else
            {
                // A declaration chain may end in an already known class,
                // but a reference token itself is never a declaration target.
                if (symbol is null || (token & referenceBit) == 0 ||
                    !pending.ContainsKey(token & ~referenceBit) &&
                    !knownSymbols.ContainsKey(token & ~referenceBit)) return false;
            }

            if (p > referenceStart - width) return false;
            var length = Number(bytes, p, wide);
            return length <= (ulong)(bytes.Length - p - width);
        }
        return false;
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
    internal sealed record ParsedRecord(string ClassName, int BodyStart, int BodyEnd,
        string Encoding, ulong Token, ulong SymbolOffset, ushort Version,
        IReadOnlyList<string> PrefixClasses);
}
