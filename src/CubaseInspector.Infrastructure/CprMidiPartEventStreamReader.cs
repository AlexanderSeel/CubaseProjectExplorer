using System.Buffers.Binary;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// A conservative subset of MMidiPart's counted event grammar. Unsupported
/// events never trigger byte resynchronization or zero-attribute guesses.
/// All returned notes remain raw candidates, without normalized musical timing.
/// </summary>
internal sealed class CprMidiPartEventStreamReader
{
    private const int MaximumEvents = 250_000;
    private const string Source =
        "Counted MMidiPart event candidate; Cubase15.exe MMidiPart reader 0x141bc6e90, " +
        "event factory 0x141bb14b0, MMidiEvent reader 0x141bb0390 and MMidiNote reader 0x141bb4690; " +
        "SHA-256 b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0. " +
        "Packet boundaries compared with bounded SX1-3 and later CPR samples. " +
        "Timing units, part placement and ownership remain unverified; " +
        "docs/research/CUBASE15_MIDI_STREAM_COMPATIBILITY.md; additional event boundaries: " +
        "docs/research/CUBASE15_PACKED_EVENT_CALLBACKS.md";

    private readonly Dictionary<long, CprClassRecordCandidate> _headers;
    private readonly CprMidiEventPrefixReader _prefixReader;
    private readonly HashSet<ulong> _objectIds;
    private readonly CprClassRevisionEvidence? _partRevision;
    private readonly CprClassRevisionEvidence? _eventRevision;
    private readonly CprClassRevisionEvidence? _noteRevision;
    private readonly CprClassRevisionEvidence? _scoreRevision;
    private readonly CprClassRevisionEvidence? _scoreBaseRevision;
    private readonly Dictionary<byte, CprClassRevisionEvidence?> _eventClassRevisions;
    private readonly long _archiveFileOffset;
    private readonly long _scanEnd;
    private readonly bool _wide;

    public CprMidiPartEventStreamReader(CprArchiveRecordInventory? inventory, bool wide)
    {
        _wide = wide;
        _archiveFileOffset = inventory?.PayloadFileOffset ?? 0;
        _scanEnd = _archiveFileOffset + (inventory?.BytesScanned ?? 0);
        _headers = (inventory?.Records ?? []).ToDictionary(record => record.HeaderFileOffset);
        _prefixReader = new(_headers, _archiveFileOffset, wide, inventory?.ClassDeclarations);
        var width = wide ? 8 : 4;
        _objectIds = _headers.Values
            // Zero-body entries may be class-only predeclarations, which do
            // not establish an object-table entry. Empty objects stay opaque.
            .Where(record => record.BodyBytes > 0 && record.BodyFileOffset >= _archiveFileOffset + width)
            .Select(record => (ulong)(record.BodyFileOffset - width - _archiveFileOffset)).ToHashSet();
        _partRevision = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiPart");
        _eventRevision = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiEvent");
        _noteRevision = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiNote");
        _scoreRevision = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MScoreEvent");
        _scoreBaseRevision = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MEvent");
        _eventClassRevisions = new()
        {
            [0x40] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MNoteChangeEvent"),
            [0x70] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MChordEvent"),
            [0xA0] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiPolyPressure"),
            [0xB0] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiController"),
            [0xC0] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiProgramChange"),
            [0xD0] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiAfterTouch"),
            [0xE0] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiPitchBend"),
            [0xF0] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiSysex")
        };
    }

    public bool TryRead(ReadOnlySpan<byte> body, long bodyFileOffset, ushort recordRevision,
        int maximumNotes, int maximumBytes, CancellationToken ct, out CprMidiPartNoteScan result)
    {
        result = default!;
        ct.ThrowIfCancellationRequested();
        var scanned = Math.Min(body.Length, maximumBytes);
        var data = body[..scanned];
        if (bodyFileOffset < _archiveFileOffset || bodyFileOffset > _scanEnd - body.Length ||
            recordRevision is not (1 or 2) || _partRevision?.RevisionAt(bodyFileOffset) != recordRevision ||
            !CprMidiPartNoteScanner.TryName(data, out var partName, out var position)) return false;
        if (position > data.Length - 4) return false;
        var kind = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position, 4));
        position += 4;
        if (kind is 0 or 17)
        {
            // Only null/existing-object tokens have a mode-width extent. Their
            // referenced objects and any musical timebase remain uninterpreted.
            for (var index = 0; index < 2; index++)
                if (!TryReference(data, bodyFileOffset, ref position)) return false;
        }
        else if (kind is >= 1 and <= 15)
        {
            // These kinds have the same eight-byte scalar boundary in both
            // native feature-flag paths. Higher kinds can switch object grammar.
            if (position > data.Length - 8) return false;
            position += 8;
        }
        else return false;
        if (position > data.Length - 4) return false;
        var eventCount = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position, 4));
        position += 4;
        if (eventCount > MaximumEvents) return false;
        var notes = new List<CprRif2MidiNoteCandidate>();
        if (eventCount == 0)
        {
            if (position != body.Length) return false;
            result = new(notes.AsReadOnly(), scanned, false);
            return true;
        }
        if (position >= data.Length || data[position] > 127) return false;
        var declarationCount = data[position++];
        for (var index = 0; index < declarationCount; index++)
        {
            if (!_headers.TryGetValue(bodyFileOffset + position, out var header) || header.BodyBytes != 0 ||
                header.BodyFileOffset <= bodyFileOffset + position ||
                header.BodyFileOffset > bodyFileOffset + data.Length) return false;
            position = (int)(header.BodyFileOffset - bodyFileOffset);
        }
        if (_eventRevision?.RevisionAt(bodyFileOffset + position) != 2 ||
            _noteRevision?.RevisionAt(bodyFileOffset + position) != 1) return false;

        for (var index = 0U; index < eventCount; index++)
        {
            if ((index & 255) == 0) ct.ThrowIfCancellationRequested();
            if (notes.Count >= maximumNotes || position >= data.Length && scanned < body.Length)
            {
                var reason = notes.Count >= maximumNotes
                    ? $"Packed MIDI note candidate limit ({maximumNotes:N0}) reached; remaining events were not read."
                    : "Packed MIDI byte scan limit reached; remaining events were not read.";
                return Partial(notes, scanned, reason, out result);
            }
            if (position >= data.Length)
                return Partial([], scanned, "Packed MIDI event list ended before its declared event count; its candidate prefix was rejected.", out result);
            var status = data[position++];
            if (status == 0)
            {
                // Full objects retain their own class/header bounds and are
                // inspected separately by arrangement traversal; do not add
                // another copy of a typed note from this stream pass.
                if (_headers.TryGetValue(bodyFileOffset + position, out var header))
                {
                    var end = header.BodyFileOffset + header.BodyBytes - bodyFileOffset;
                    if (end < position || end > data.Length)
                        return Partial([], scanned, "Escaped MIDI object is outside the scanned part; its candidate prefix was rejected.", out result);
                    position = (int)end;
                }
                else if (!TryReference(data, bodyFileOffset, ref position))
                    return Partial([], scanned, "Escaped MIDI reference is unresolved; its candidate prefix was rejected.", out result);
                continue;
            }
            if (status == 0x30)
            {
                if (!TrySkipScore(data, bodyFileOffset, ref position, out var scoreFailure))
                    return Partial(notes, scanned,
                        $"Unsupported score discriminator 0x30 at file offset {bodyFileOffset + position - 1}; " +
                        "requires earlier MScoreEvent v1/MEvent v0 or v1 declarations, a supported MDataNode boundary, " +
                        "supported inherited attributes and a complete suffix. " + scoreFailure +
                        " Remaining events were not interpreted.", out result);
                continue;
            }
            var isNote = status is 0x80 or 0x90;
            if (!isNote)
            {
                if (!_eventClassRevisions.TryGetValue(status, out var revision))
                    return Partial(notes, scanned,
                        $"Unsupported packed MIDI event discriminator 0x{status:X2} at file offset {bodyFileOffset + position - 1}; remaining events were not interpreted.", out result);
                if (revision?.RevisionAt(bodyFileOffset + position) != 0)
                    return Partial(notes, scanned,
                        $"Packed MIDI event 0x{status:X2} has no unambiguous supported class revision at file offset {bodyFileOffset + position}; remaining events were not interpreted.", out result);
            }
            if (!_prefixReader.TryRead(data[position..], bodyFileOffset + position,
                    out var prefixBytes, out var skippedObject, out var prefixFailure))
                return Partial(scanned < body.Length || !isNote ? notes : [], scanned,
                    prefixFailure + (scanned < body.Length ? " Packed MIDI byte scan limit reached."
                        : " Its candidate prefix was rejected."), out result);
            // The callbacks share the MMidiEvent base. Chords add the note
            // tail plus one byte; note changes add u16, u32 and f64. Values
            // are skipped as wire data, without assigning musical semantics.
            var tailBytes = isNote ? 25 : status switch { 0x70 => 26, 0x40 => 14, _ => 0 };
            var eventBytes = prefixBytes + tailBytes;
            if (position > data.Length - eventBytes)
            {
                if (scanned == body.Length)
                    return Partial([], scanned, "Packed MIDI event body is incomplete; its candidate prefix was rejected.", out result);
                return Partial(notes, scanned, "Packed MIDI byte scan limit reached inside an event body.", out result);
            }
            if (!isNote)
            {
                if (status == 0xF0)
                {
                    var payloadStart = position + eventBytes;
                    if (payloadStart > data.Length - 8)
                        return Partial(notes, scanned,
                            $"Packed SysEx payload length fields at file offset {bodyFileOffset + payloadStart} are outside the scanned bytes.", out result);
                    var payloadBytes = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(payloadStart, 4));
                    var duplicateBytes = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(payloadStart + 4, 4));
                    // The reader uses the first count; its matching writer
                    // emits it twice. Other pairs remain unsupported here.
                    if (payloadBytes != duplicateBytes)
                        return Partial(notes, scanned,
                            $"Packed SysEx payload length fields differ at file offset {bodyFileOffset + payloadStart}; remaining events were not interpreted.", out result);
                    if (payloadBytes > data.Length - payloadStart - 8)
                        return Partial(notes, scanned,
                            $"Packed SysEx payload at file offset {bodyFileOffset + payloadStart + 8} exceeds the scanned bytes; remaining events were not interpreted.", out result);
                    eventBytes += 8 + (int)payloadBytes;
                }
                position += eventBytes;
                continue;
            }
            if (!CprMidiPartNoteScanner.TryReadFramedModernFields(data.Slice(position, eventBytes),
                    bodyFileOffset + position, _wide, prefixBytes, out var note))
            {
                var raw = data.Slice(position, eventBytes);
                var detail = $" Raw bytes at +8/+9/+10: {raw[8]}/{raw[9]}/{raw[10]}; " +
                        $"f64 at +0: {BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(raw[..8])):R}; " +
                        $"f64 at +{prefixBytes}: {BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(raw.Slice(prefixBytes, 8))):R}.";
                return Partial([], scanned,
                    $"Packed MIDI note body at file offset {bodyFileOffset + position} is unsupported; its candidate prefix was rejected.{detail}", out result);
            }
            notes.Add(note with
            {
                FileOffset = bodyFileOffset + position - 1,
                Evidence = Source + (skippedObject
                    ? "; Nested event attribute object skipped as opaque; attributes were not applied to raw note fields. " +
                      "docs/research/CUBASE15_NESTED_ATTRIBUTES.md" : ""),
                SourceLayout = (_wide ? "RIF2" : "RIFF") + " MMidiPart/counted-events",
                CandidatePartName = partName
            });
            position += eventBytes;
        }
        // The v1/v2 callback returns after this list. Unconsumed bytes are not
        // permission to scan for another plausible zero-attribute event.
        if (position != body.Length)
            return Partial([], scanned, "Bytes remain after the declared packed MIDI event list; its candidate prefix was rejected.", out result);
        result = new(notes.AsReadOnly(), scanned, false);
        return true;

    }

    private bool TryReference(ReadOnlySpan<byte> data, long bodyFileOffset, ref int position)
    {
        var width = _wide ? 8 : 4;
        if (position > data.Length - width) return false;
        var token = _wide ? BinaryPrimitives.ReadUInt64BigEndian(data.Slice(position, 8)) :
            BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position, 4));
        var referenceBit = _wide ? 0x8000000000000000UL : 0x80000000UL;
        if ((token & referenceBit) != 0 || token != 0 &&
            (!_objectIds.Contains(token) || token >= (ulong)(bodyFileOffset + position - _archiveFileOffset))) return false;
        position += width;
        return true;
    }

    private bool TrySkipScore(ReadOnlySpan<byte> data, long fileOffset, ref int position, out string? failure)
    {
        failure = null;
        var revision = _scoreBaseRevision?.RevisionAt(fileOffset + position);
        if (_scoreRevision?.RevisionAt(fileOffset + position) != 1 || revision is not (0 or 1)) return false;
        // 0x1427EBB00: flags u16, three f64, MDataNode, optional
        // MEvent attributes, then f64/u8/u8. No MMidiEvent base prefix.
        // MDataNode is bounded by the archive object header and stays opaque.
        if (position > data.Length - 26) return false;
        var nodeEnd = position + 26;
        if (!_prefixReader.TrySkipObject(data, fileOffset, "MDataNode", ref nodeEnd)) return false;
        var prefix = nodeEnd - position;
        if (revision == 1 && !_prefixReader.TryReadAttributes(data[position..], fileOffset + position, prefix,
                out prefix, out _, out failure))
            return false;
        if (prefix > data.Length - position - 10) return false;
        position += prefix + 10;
        return true;
    }

    private static bool Partial(List<CprRif2MidiNoteCandidate> notes, int bytesScanned,
        string reason, out CprMidiPartNoteScan result)
    {
        result = new(notes.AsReadOnly(), bytesScanned, true, reason);
        return true;
    }
}
