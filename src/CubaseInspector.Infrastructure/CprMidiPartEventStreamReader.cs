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
        "docs/research/CUBASE15_MIDI_STREAM_COMPATIBILITY.md";

    private readonly Dictionary<long, CprClassRecordCandidate> _headers;
    private readonly HashSet<ulong> _objectIds;
    private readonly CprClassRevisionEvidence? _partRevision;
    private readonly CprClassRevisionEvidence? _eventRevision;
    private readonly CprClassRevisionEvidence? _noteRevision;
    private readonly Dictionary<byte, CprClassRevisionEvidence?> _scalarEventRevisions;
    private readonly long _archiveFileOffset;
    private readonly long _scanEnd;
    private readonly bool _wide;

    public CprMidiPartEventStreamReader(CprArchiveRecordInventory? inventory, bool wide)
    {
        _wide = wide;
        _archiveFileOffset = inventory?.PayloadFileOffset ?? 0;
        _scanEnd = _archiveFileOffset + (inventory?.BytesScanned ?? 0);
        _headers = (inventory?.Records ?? []).ToDictionary(record => record.HeaderFileOffset);
        var width = wide ? 8 : 4;
        _objectIds = _headers.Values
            // Zero-body entries may be class-only predeclarations, which do
            // not establish an object-table entry. Empty objects stay opaque.
            .Where(record => record.BodyBytes > 0 && record.BodyFileOffset >= _archiveFileOffset + width)
            .Select(record => (ulong)(record.BodyFileOffset - width - _archiveFileOffset)).ToHashSet();
        _partRevision = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiPart");
        _eventRevision = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiEvent");
        _noteRevision = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiNote");
        _scalarEventRevisions = new()
        {
            [0xB0] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiController"),
            [0xD0] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiAfterTouch"),
            [0xE0] = CprClassRevisionEvidence.Find(inventory?.ClassDeclarations, "MMidiPitchBend")
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
                result = new(notes.AsReadOnly(), scanned, true);
                return true;
            }
            if (position >= data.Length) return false;
            var status = data[position++];
            if (status == 0)
            {
                // Full objects retain their own class/header bounds and are
                // inspected separately by arrangement traversal; do not add
                // another copy of a typed note from this stream pass.
                if (_headers.TryGetValue(bodyFileOffset + position, out var header))
                {
                    var end = header.BodyFileOffset + header.BodyBytes - bodyFileOffset;
                    if (end < position || end > data.Length) return false;
                    position = (int)end;
                }
                else if (!TryReference(data, bodyFileOffset, ref position)) return false;
                continue;
            }
            var isNote = status is 0x80 or 0x90;
            if (!isNote && (!_scalarEventRevisions.TryGetValue(status, out var revision) ||
                    revision?.RevisionAt(bodyFileOffset + position) != 0)) return false;
            if (position > data.Length - 17)
            {
                if (scanned == body.Length) return false;
                result = new(notes.AsReadOnly(), scanned, true);
                return true;
            }
            var attributeCount = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position + 13, 4));
            if (attributeCount > 16) return false;
            var eventBytes = (isNote ? 42 : 17) + (int)attributeCount * 14;
            if (position > data.Length - eventBytes)
            {
                if (scanned == body.Length) return false;
                result = new(notes.AsReadOnly(), scanned, true);
                return true;
            }
            if (!isNote)
            {
                if (!CprMidiNoteLayout.TryReadPrefix(data.Slice(position, eventBytes), 2, out _, out _)) return false;
                position += eventBytes;
                continue;
            }
            if (!CprMidiPartNoteScanner.TryReadRecord(data.Slice(position, eventBytes),
                    bodyFileOffset + position, _wide, out var note, 1, 2)) return false;
            notes.Add(note with
            {
                FileOffset = bodyFileOffset + position - 1,
                Evidence = Source,
                SourceLayout = (_wide ? "RIF2" : "RIFF") + " MMidiPart/counted-events",
                CandidatePartName = partName
            });
            position += eventBytes;
        }
        // The v1/v2 callback returns after this list. Unconsumed bytes are not
        // permission to scan for another plausible zero-attribute event.
        if (position != body.Length) return false;
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
}
