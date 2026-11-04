using System.Buffers.Binary;
using System.Globalization;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Partial denotes omitted raw fields due to a retention budget, not semantic coverage.</summary>
public readonly record struct CprBinaryFieldReadResult(IReadOnlyList<CprBinaryFieldCandidate> Fields, bool Partial);

/// <summary>
/// Exposes anonymous scalar observations for narrowly bounded, binary-derived
/// layouts. These fields have no verified project ownership or musical units.
/// </summary>
public static class CprBinaryFieldCandidateDecoder
{
    public const int MaximumFieldsPerRecord = 256;
    private const string TempoSource =
        "Cubase15.exe reader 0x1431e0870 / writer 0x1431e08f0; " +
        "SHA-256 b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0; " +
        "MTempoEvent serialization version 0; big-endian candidate; " +
        "field semantics unverified; docs/research/CUBASE15_SERIALIZER_ANALYSIS.md";
    private const string TempoTrackSource =
        "Cubase15.exe reader 0x1431e2e00 / writer 0x1431e30f0; " +
        "SHA-256 b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0; " +
        "MTempoTrackEvent counted fixed prefix; " +
        "big-endian candidate; field semantics unverified; suffix attributes remain opaque; " +
        "docs/research/CUBASE15_SERIALIZER_ANALYSIS.md; docs/research/CUBASE15_C_TEMPO_TABLES.md";
    private const string TimeSignatureSource =
        "Cubase15.exe reader 0x1431d3b70 / writer 0x1431d3c40; " +
        "SHA-256 b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0; " +
        "MTimeSignatureEvent shared fixed 20-byte prefix; " +
        "big-endian candidate; field semantics unverified; named attributes remain opaque; " +
        "docs/research/CUBASE15_SERIALIZER_ANALYSIS.md; docs/research/CUBASE15_C_SIGNATURE_REVISIONS.md";

    public static IReadOnlyList<CprBinaryFieldCandidate> Read(CprClassRecordCandidate record,
        ReadOnlySpan<byte> archiveData, long archiveFileOffset,
        ushort? midiEventRevision = null, ushort? midiNoteRevision = null) =>
        ReadBounded(record, archiveData, archiveFileOffset, midiEventRevision, midiNoteRevision).Fields;

    public static CprBinaryFieldReadResult ReadBounded(CprClassRecordCandidate record,
        ReadOnlySpan<byte> archiveData, long archiveFileOffset,
        ushort? midiEventRevision = null, ushort? midiNoteRevision = null,
        int maximumFields = MaximumFieldsPerRecord) =>
        ReadBoundedWithContext(record, archiveData, archiveFileOffset, midiEventRevision,
            midiNoteRevision, maximumFields, null);

    internal static CprBinaryFieldReadResult ReadBoundedWithContext(CprClassRecordCandidate record,
        ReadOnlySpan<byte> archiveData, long archiveFileOffset,
        ushort? midiEventRevision, ushort? midiNoteRevision, int maximumFields,
        CprMidiEventPrefixReader? prefixReader)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (maximumFields is < 0 or > MaximumFieldsPerRecord)
            throw new ArgumentOutOfRangeException(nameof(maximumFields));
        var isTempoEvent = string.Equals(record.ClassName, "MTempoEvent", StringComparison.Ordinal) &&
            record.ClassVersion == 0 && record.BodyBytes == 24;
        var isTempoTrack = string.Equals(record.ClassName, "MTempoTrackEvent", StringComparison.Ordinal) &&
            record.ClassVersion is <= 3 && record.BodyBytes >= 4;
        var isTimeSignature = string.Equals(record.ClassName, "MTimeSignatureEvent", StringComparison.Ordinal) &&
            record.ClassVersion is <= 2 && record.BodyBytes >= 20;
        var isMidiNote = string.Equals(record.ClassName, "MMidiNote", StringComparison.Ordinal) &&
            record.ClassVersion is 0 or 1;
        var isAutomationTrack = string.Equals(record.ClassName, "MAutomationTrack", StringComparison.Ordinal) &&
            record.ClassVersion is <= 2;
        var isMarker = string.Equals(record.ClassName, "MMarkerEvent", StringComparison.Ordinal) &&
            record.ClassVersion == 0;
        if ((!isTempoEvent && !isTempoTrack && !isTimeSignature && !isMidiNote && !isAutomationTrack && !isMarker) ||
            record.BodyBytes < 0 || archiveData.Length < record.BodyBytes ||
            archiveFileOffset < 0 || archiveFileOffset > long.MaxValue - archiveData.Length ||
            record.BodyFileOffset < archiveFileOffset)
            return new([], false);
        var relative = record.BodyFileOffset - archiveFileOffset;
        if (relative > archiveData.Length - record.BodyBytes) return new([], false);
        var body = archiveData.Slice((int)relative, (int)record.BodyBytes);

        if (isAutomationTrack) return CprAutomationTrackFieldReader.Read(body, record.BodyFileOffset,
            record.ClassVersion!.Value, maximumFields);

        if (isMarker) return CprMarkerFieldReader.Read(body, record.BodyFileOffset, maximumFields);

        if (isMidiNote) return Limit(midiNoteRevision == record.ClassVersion
            ? ReadMidiNote(record, body, midiEventRevision, prefixReader) : [], maximumFields);

        if (isTempoTrack) return ReadTempoTrack(record, body, maximumFields);

        if (isTimeSignature)
        {
            // The same u64 + six u16 reads precede the named revision gate.
            // Format only retained fields, including when the project budget
            // is exhausted. Neither the suffix nor musical units are decoded.
            var retained = Math.Min(7, maximumFields);
            if (retained == 0) return new([], true);
            var fields = new CprBinaryFieldCandidate[retained];
            var source = TimeSignatureSource + $"; serializer revision {record.ClassVersion}.";
            fields[0] = Field("body+0", "u64-be", BinaryPrimitives.ReadUInt64BigEndian(body[..8])
                .ToString(CultureInfo.InvariantCulture), record.BodyFileOffset, body[..8], source);
            for (var index = 0; index < retained - 1; index++)
            {
                var offset = 8 + index * 2;
                fields[index + 1] = Field($"body+{offset}", "u16-be",
                    BinaryPrimitives.ReadUInt16BigEndian(body.Slice(offset, 2)).ToString(CultureInfo.InvariantCulture),
                    record.BodyFileOffset + offset, body.Slice(offset, 2), source);
            }
            return new(Array.AsReadOnly(fields), retained < 7);
        }

        // Keep the original bits, including NaN payloads, infinities and signed
        // zero. A readable numeric rendering alone cannot preserve all evidence.
        var tempoRetained = Math.Min(4, maximumFields);
        if (tempoRetained == 0) return new([], true);
        var tempoFields = new CprBinaryFieldCandidate[tempoRetained];
        for (var index = 0; index < tempoRetained; index++)
        {
            var offset = index switch { 0 => 0, 1 => 4, 2 => 12, _ => 20 };
            var raw = body.Slice(offset, index is 1 or 2 ? 8 : 4);
            var (type, value) = index switch
            {
                0 => ("f32-be", BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(raw))
                    .ToString("R", CultureInfo.InvariantCulture)),
                3 => ("u32-be", BinaryPrimitives.ReadUInt32BigEndian(raw).ToString(CultureInfo.InvariantCulture)),
                _ => ("f64-be", BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(raw))
                    .ToString("R", CultureInfo.InvariantCulture))
            };
            tempoFields[index] = Field($"body+{offset}", type, value, record.BodyFileOffset + offset, raw);
        }
        return new(Array.AsReadOnly(tempoFields), tempoRetained < 4);
    }

    private static CprBinaryFieldReadResult Limit(IReadOnlyList<CprBinaryFieldCandidate> fields, int maximumFields) =>
        fields.Count <= maximumFields ? new(fields, false) :
            new(Array.AsReadOnly(fields.Take(maximumFields).ToArray()), true);

    private static CprBinaryFieldReadResult ReadTempoTrack(CprClassRecordCandidate record,
        ReadOnlySpan<byte> body, int maximumFields)
    {
        var count = BinaryPrimitives.ReadInt32BigEndian(body);
        var suffixBytes = record.ClassVersion > 0 ? 6 : 0;
        // Validate the whole fixed table and the active-revision suffix before
        // allocating anything. Attribute lists after it remain uninterpreted.
        if (count < 1 || body.Length < 4 + suffixBytes || count > (body.Length - 4 - suffixBytes) / 22)
            return new([], false);
        if (maximumFields == 0) return new([], true);
        var retainedEntries = Math.Min(count, (maximumFields - 1) / 4);
        var fields = new List<CprBinaryFieldCandidate>(Math.Min(maximumFields, 1 + retainedEntries * 4 + 2));
        var source = TempoTrackSource + $"; serializer revision {record.ClassVersion}; " +
            $"retained fixed entries {retainedEntries} of {count}. " +
            "No BPM, timebase, effective tempo map or complete suffix validation is asserted.";
        Add(0, 4, "u32-be", count.ToString(CultureInfo.InvariantCulture), body[..4]);
        for (var index = 0; index < retainedEntries; index++)
        {
            var offset = 4 + index * 22;
            var row = body.Slice(offset, 22);
            Add(offset, 4, "f32-be", BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(row))
                .ToString("R", CultureInfo.InvariantCulture), row[..4]);
            for (var scalar = 0; scalar < 2; scalar++)
            {
                var relative = 4 + scalar * 8;
                var value = row.Slice(relative, 8);
                Add(offset + relative, 8, "f64-be", BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(value))
                    .ToString("R", CultureInfo.InvariantCulture), value);
            }
            Add(offset + 20, 2, "u16-be", BinaryPrimitives.ReadUInt16BigEndian(row[20..])
                .ToString(CultureInfo.InvariantCulture), row[20..]);
        }
        if (retainedEntries == count && suffixBytes != 0)
        {
            var offset = 4 + count * 22;
            if (fields.Count < maximumFields)
                Add(offset, 4, "f32-be", BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(body.Slice(offset, 4)))
                    .ToString("R", CultureInfo.InvariantCulture), body.Slice(offset, 4));
            if (fields.Count < maximumFields)
                Add(offset + 4, 2, "u16-be", BinaryPrimitives.ReadUInt16BigEndian(body.Slice(offset + 4, 2))
                    .ToString(CultureInfo.InvariantCulture), body.Slice(offset + 4, 2));
        }
        var partial = fields.Count < 1L + (long)count * 4 + (suffixBytes == 0 ? 0 : 2);
        return new(fields.AsReadOnly(), partial);

        void Add(int offset, int bytes, string type, string value, ReadOnlySpan<byte> raw) =>
            fields.Add(Field($"body+{offset}", type, value, record.BodyFileOffset + offset, raw[..bytes], source));
    }

    private static IReadOnlyList<CprBinaryFieldCandidate> ReadMidiNote(CprClassRecordCandidate record,
        ReadOnlySpan<byte> body, ushort? midiEventRevision, CprMidiEventPrefixReader? prefixReader)
    {
        if (midiEventRevision is not ushort eventRevision ||
            record.ClassVersion is not ushort noteRevision ||
            !CprMidiNoteLayout.TryLocate(body, eventRevision, noteRevision, record.BodyFileOffset,
                prefixReader, out var layout, out _)) return [];
        var source = "Cubase15.exe MMidiEvent reader 0x141bb0390 / MMidiNote reader 0x141bb4690; " +
            "SHA-256 b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0; " +
            $"MMidiEvent serialization version {eventRevision}, MMidiNote serialization version {noteRevision}; " +
            "big-endian candidate; field semantics unverified; attributes remain opaque and are not applied to raw fields; " +
            "docs/research/CUBASE15_BACKWARD_COMPATIBILITY.md";
        var fields = new List<CprBinaryFieldCandidate>();
        fields.Add(DoubleField(0, body[..8]));
        for (var offset = 8; offset < 11; offset++)
            fields.Add(Field($"body+{offset}", "u8", body[offset].ToString(CultureInfo.InvariantCulture),
                record.BodyFileOffset + offset, body.Slice(offset, 1), source));
        if (eventRevision > 0)
            fields.Add(Field("body+11", "u16-be", BinaryPrimitives.ReadUInt16BigEndian(body.Slice(11, 2))
                .ToString(CultureInfo.InvariantCulture), record.BodyFileOffset + 11, body.Slice(11, 2), source));
        if (eventRevision == 2)
            fields.Add(Field("body+13", "u32-be", layout.AttributeCount.ToString(CultureInfo.InvariantCulture),
                record.BodyFileOffset + 13, body.Slice(13, 4), source));
        for (var index = 0; index < 3; index++)
        {
            var offset = layout.TailOffset + index * 8;
            fields.Add(DoubleField(offset, body.Slice(offset, 8)));
        }
        if (noteRevision == 1)
        {
            var offset = layout.TailOffset + 24;
            fields.Add(Field($"body+{offset}", "u8", body[offset].ToString(CultureInfo.InvariantCulture),
                record.BodyFileOffset + offset, body.Slice(offset, 1), source));
        }
        return fields.AsReadOnly();

        CprBinaryFieldCandidate DoubleField(int offset, ReadOnlySpan<byte> bytes) =>
            Field($"body+{offset}", "f64-be", BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64BigEndian(bytes)).ToString("R", CultureInfo.InvariantCulture),
                record.BodyFileOffset + offset, bytes, source);
    }

    private static CprBinaryFieldCandidate Field(string field, string type, string value,
        long offset, ReadOnlySpan<byte> bytes, string source = TempoSource) => new(field, type, value,
            offset, bytes.Length, source, RawHex: Convert.ToHexString(bytes));
}
