using System.Buffers.Binary;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Exact MMidiEvent-v2 attribute boundaries in counted and typed records. Supported
/// nested objects remain opaque: skipping their framed bytes does not apply their
/// values or establish the final musical state of a note.
/// </summary>
internal sealed class CprMidiEventPrefixReader
{
    private const uint ScoreAttributes = 0x53634172;
    private const uint BezierAttribute = 0x624F7563;
    private readonly IReadOnlyDictionary<long, CprClassRecordCandidate> _headers;
    private readonly Dictionary<ulong, CprClassRecordCandidate> _objects;
    private readonly Dictionary<ulong, CprClassDeclarationCandidate> _declarations;
    private readonly CprClassRevisionEvidence? _curveBaseRevision;
    private readonly long _archiveFileOffset;
    private readonly bool _wide;

    public CprMidiEventPrefixReader(IReadOnlyDictionary<long, CprClassRecordCandidate> headers,
        long archiveFileOffset, bool wide, IReadOnlyList<CprClassDeclarationCandidate>? declarations)
    {
        _headers = headers;
        _archiveFileOffset = archiveFileOffset;
        _wide = wide;
        var width = wide ? 8 : 4;
        _curveBaseRevision = CprClassRevisionEvidence.Find(declarations, "MParamEventCurveBase");
        _declarations = (declarations ?? []).GroupBy(declaration => declaration.SymbolArchiveOffset)
            .Where(group => group.Count() == 1).Select(group => group.First())
            .Where(declaration => declaration.ClassVersion == 0 &&
                declaration.ClassName is "CmAttribCollection" or "MParamEventCurveRationalBezier" or "CmString" or "MDataNode")
            .ToDictionary(declaration => declaration.SymbolArchiveOffset);
        _objects = headers.Values.Where(record => record.BodyBytes > 0 && record.ClassVersion == 0)
            .Where(record => record.BodyFileOffset >= archiveFileOffset + width)
            .GroupBy(record => (ulong)(record.BodyFileOffset - width - archiveFileOffset))
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First());
    }

    public bool TryRead(ReadOnlySpan<byte> data, long fileOffset, out int prefixBytes,
        out bool skippedObject, out string? failure)
    {
        prefixBytes = 17;
        skippedObject = false;
        failure = null;
        if (data.Length < prefixBytes)
        {
            failure = "Packed MIDI event header is incomplete.";
            return false;
        }
        return TryReadAttributes(data, fileOffset, 13, out prefixBytes, out skippedObject, out failure);
    }

    // MEvent (score) and MMidiEvent call the same native attribute-list reader;
    // their base fields and revision gates remain separate in the consumers.
    public bool TryReadAttributes(ReadOnlySpan<byte> data, long fileOffset, int countOffset,
        out int prefixBytes, out bool skippedObject, out string? failure)
    {
        prefixBytes = 0;
        skippedObject = false;
        failure = null;
        if (countOffset < 0 || countOffset > data.Length - 4)
        {
            failure = $"Event attribute count is incomplete at file offset {fileOffset}.";
            return false;
        }
        prefixBytes = countOffset + 4;
        var count = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(countOffset, 4));
        if (count > 16)
        {
            failure = $"Packed MIDI event exceeds the supported attribute count at file offset {fileOffset}.";
            return false;
        }
        for (var index = 0; index < count; index++)
        {
            var itemOffset = prefixBytes;
            if (itemOffset > data.Length - 6)
            {
                failure = $"Packed MIDI attribute header is incomplete at file offset {fileOffset + itemOffset}.";
                return false;
            }
            var item = data[itemOffset..];
            var id = BinaryPrimitives.ReadUInt32BigEndian(item[..4]);
            var flags = BinaryPrimitives.ReadUInt16BigEndian(item.Slice(4, 2));
            prefixBytes += 6;
            if (flags is 1 or 4 && item[..4].IndexOfAnyExceptInRange((byte)32, (byte)126) < 0)
            {
                if (prefixBytes > data.Length - 8)
                {
                    failure = $"Packed MIDI scalar attribute is incomplete at file offset {fileOffset + itemOffset}.";
                    return false;
                }
                prefixBytes += 8;
                continue;
            }
            if (flags == 0x100 && item[..4].IndexOfAnyExceptInRange((byte)32, (byte)126) < 0)
            {
                if (CprAttributeContainerBoundaryReader.TrySkip(data, ref prefixBytes, out var containerFailure))
                {
                    skippedObject = true;
                    continue;
                }
                failure = $"Attribute ID 0x{id:X8}, flags 0x{flags:X4}, at file offset {fileOffset + itemOffset}: " +
                    containerFailure + " No later attribute boundary was inferred.";
                return false;
            }
            // Only the ASM-backed class/flag combinations below are supported.
            // Their framed contents remain opaque; no attribute is applied.
            var expectedClass = (id, flags) switch
            {
                (ScoreAttributes, 0x22) => "CmAttribCollection",
                (BezierAttribute, 2) => "MParamEventCurveRationalBezier",
                (_, 8) when item[..4].IndexOfAnyExceptInRange((byte)32, (byte)126) < 0 => "CmString",
                _ => null
            };
            if (expectedClass is not null && TrySkipObject(data, fileOffset, expectedClass, ref prefixBytes))
            {
                skippedObject = true;
                continue;
            }
            var grammar = (flags & 2) != 0 ? "nested CmObject" : (flags & 0x100) != 0 ? "container"
                : (flags & 8) != 0 ? "nested CmString" : "unsupported attribute";
            failure = $"Attribute ID 0x{id:X8}, flags 0x{flags:X4}, at file offset {fileOffset + itemOffset} " +
                $"requires {grammar} grammar with supported framing; no later attribute boundary was inferred.";
            return false;
        }
        return true;
    }

    internal bool TrySkipObject(ReadOnlySpan<byte> data, long fileOffset, string expectedClass, ref int position)
    {
        var width = _wide ? 8 : 4;
        if (position > data.Length - width) return false;
        var token = _wide ? BinaryPrimitives.ReadUInt64BigEndian(data.Slice(position, 8))
            : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position, 4));
        if (token == 0) { position += width; return true; }
        var referenceBit = _wide ? 0x8000000000000000UL : 0x80000000UL;
        if ((token & referenceBit) == 0)
        {
            if (!_objects.TryGetValue(token, out var previous) || !Matches(previous, expectedClass) ||
                previous.BodyFileOffset + previous.BodyBytes > fileOffset + position) return false;
            position += width;
            return true;
        }
        if (!_headers.TryGetValue(fileOffset + position, out var header) &&
            !TryReadInternedHeader(data, fileOffset, position, token, expectedClass, out header)) return false;
        if (!Matches(header, expectedClass) ||
            (header.PrefixClasses.Count == 0 ? header.ClassToken != token
                : token != (_wide ? ulong.MaxValue : uint.MaxValue) - 1) ||
            header.BodyFileOffset < fileOffset + position + 2 * width ||
            header.BodyFileOffset > fileOffset + data.Length ||
            header.BodyBytes > fileOffset + data.Length - header.BodyFileOffset) return false;
        var sizeOffset = (int)(header.BodyFileOffset - fileOffset) - width;
        var declared = _wide ? BinaryPrimitives.ReadUInt64BigEndian(data.Slice(sizeOffset, 8))
            : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(sizeOffset, 4));
        if (declared != (ulong)header.BodyBytes) return false;
        var objectId = (ulong)(header.BodyFileOffset - width - _archiveFileOffset);
        if (_objects.TryGetValue(objectId, out var existing) && existing != header) return false;
        _objects.TryAdd(objectId, header);
        position = (int)(header.BodyFileOffset + header.BodyBytes - fileOffset);
        return true;
    }

    private bool TryReadInternedHeader(ReadOnlySpan<byte> data, long fileOffset, int position,
        ulong token, string expectedClass, out CprClassRecordCandidate header)
    {
        header = default!;
        var width = _wide ? 8 : 4;
        var referenceBit = _wide ? 0x8000000000000000UL : 0x80000000UL;
        var symbol = token & ~referenceBit;
        // Inventory detail retention is bounded. At an exact attribute cursor,
        // an earlier known declaration still establishes token+length framing;
        // this path never scans bytes for a header or follows a declaration.
        if (position > data.Length - 2 * width ||
            !_declarations.TryGetValue(symbol, out var declaration) || declaration.ClassName != expectedClass ||
            declaration.AvailableAtFileOffset is not long available || available > fileOffset + position ||
            declaration.DeclarationFileOffset >= fileOffset + position ||
            symbol >= (ulong)(fileOffset + position - _archiveFileOffset)) return false;
        var size = _wide ? BinaryPrimitives.ReadUInt64BigEndian(data.Slice(position + width, 8))
            : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position + width, 4));
        if (size == 0 || size > (ulong)(data.Length - position - 2 * width)) return false;
        header = new(expectedClass, fileOffset + position, fileOffset + position + 2 * width,
            (long)size, 0, null, "interned-class-reference", token, symbol, 0, []);
        return true;
    }

    private bool Matches(CprClassRecordCandidate record, string expectedClass) =>
        record.ClassName == expectedClass && record.ClassVersion == 0 && record.BodyBytes > 0 &&
        (expectedClass != "MParamEventCurveRationalBezier" || _curveBaseRevision?.RevisionAt(record.BodyFileOffset) == 0) &&
        (record.PrefixClasses.Count == 0 || expectedClass == "MParamEventCurveRationalBezier" &&
            record.PrefixClasses.SequenceEqual(["MParamEventCurveBase"]));
}
