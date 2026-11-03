using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprMidiNoteRevisionTests
{
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private static byte[] Number(ulong value, bool wide = false)
    {
        var bytes = new byte[wide ? 8 : 4];
        if (wide) BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
        return bytes;
    }

    private static byte[] Name(string value, ushort revision)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\0");
        var version = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(version, revision);
        return Join(Number((uint)bytes.Length), bytes, version);
    }

    private static byte[] Record(string name, ushort revision, byte[] body, bool wide) =>
        Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide), Name(name, revision),
            Number((uint)body.Length, wide), body);

    private static byte[] Chunk(string name, byte[] body, bool wide) =>
        Join(Encoding.ASCII.GetBytes(name), Number((uint)body.Length, wide), body);

    private static byte[] Body(int baseRevision, int noteRevision, int count = 2)
    {
        // Independent wire samples follow the callbacks, including the two
        // otherwise uninterpreted f64 tail values and the revision-1 byte.
        var prefix = baseRevision switch { 0 => 11, 1 => 13, _ => 17 + count * 14 };
        var bytes = new byte[prefix + 24 + noteRevision];
        BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(-2.5));
        bytes[8] = 2; bytes[9] = 64; bytes[10] = 100;
        if (baseRevision > 0) BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(11), 0x8001);
        if (baseRevision == 2)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(13), (uint)count);
            for (var index = 0; index < count; index++)
            {
                Encoding.ASCII.GetBytes("test").CopyTo(bytes, 17 + index * 14);
                bytes[22 + index * 14] = (byte)(index % 2 == 0 ? 1 : 4);
            }
        }
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(prefix), BitConverter.DoubleToInt64Bits(120));
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(prefix + 8), 0x7FF8000000001234);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(prefix + 16), 0x8000000000000000);
        if (noteRevision == 1) bytes[^1] = 0xAB;
        return bytes;
    }

    private static byte[] EventDeclaration(bool wide, ushort? revision) => revision is ushort version
        ? Join(Number((wide ? ulong.MaxValue : uint.MaxValue) - 1, wide), Name("MMidiEvent", version),
            Record("UnrelatedCarrier", 0, [], wide)) : [];

    private static byte[] Project(bool wide, ushort? eventRevision, ushort noteRevision, byte[] body,
        bool conflictingRevision = false, bool futureBase = false, bool conflictingNote = false,
        byte[]? before = null, byte[]? after = null)
    {
        var declaration = EventDeclaration(wide, eventRevision);
        if (conflictingRevision)
            declaration = Join(declaration, Record("MMidiEvent", 1, [], wide));
        var note = Record("MMidiNote", noteRevision, body, wide);
        var archive = futureBase ? Join(note, declaration) : Join(declaration, before ?? [], note, after ?? []);
        if (conflictingNote) archive = Join(archive, Record("MMidiNote", 0, [], wide));
        var stream = Encoding.ASCII.GetBytes("Arrangement1");
        var cls = Encoding.ASCII.GetBytes("PArrangement");
        var chunks = Join(Chunk("ROOT", Join(Number((uint)stream.Length), stream,
                Number((uint)cls.Length), cls), wide), Chunk("ARCH", archive, wide));
        return Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"), Number((uint)chunks.Length, wide),
            Encoding.ASCII.GetBytes("NUND"), chunks);
    }

    private static byte[] ObjectBody(byte[] value, uint id = 0x53634172, ushort flags = 0x22)
    {
        var note = Body(2, 1, 0);
        var prefix = note[..17];
        BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(13), 2);
        var type = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(type, flags);
        return Join(prefix, Number(id), type, value, Body(2, 1, 1)[17..31], note[^25..]);
    }

    [Fact]
    public async Task TypedNoteBudgetPreservesEarlierCandidatesAndStillFindsLaterTrackRecords()
    {
        var note = Record("MMidiNote", 1, Body(2, 1, 0), false);
        var following = Join(Join(Enumerable.Repeat(note, 10000).ToArray()),
            Record("MAudioTrackEvent", 0, [], false));
        var bytes = Project(false, 2, 1, Body(2, 1, 0), after: following);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(10000, result.Value!.Arrangement!.MidiNoteCandidates!.Count);
            Assert.True(result.Value.Arrangement.Partial);
            Assert.Contains("10,000 MIDI note candidate limit", result.Value.Arrangement.LimitReasons!);
            Assert.Contains(result.Value.Arrangement.TrackHints!, hint => hint.RecordClass == "MAudioTrackEvent");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public async Task TypedNotesUseExactOpaqueObjectBoundariesAndPreserveRawTailBits(bool wide, int kind)
    {
        var payload = new byte[] { 0x90, 0xFF, 0x00, 0x42 };
        var collection = Record(kind == 4 ? "CmString" : "CmAttribCollection", 0, payload, wide);
        var before = kind == 2 ? collection : Array.Empty<byte>();
        var objectId = EventDeclaration(wide, 2).Length + collection.Length - payload.Length - (wide ? 8 : 4);
        var value = kind switch
        {
            1 => Number(0, wide),
            2 => Number((ulong)objectId, wide),
            3 => Join(Number((wide ? ulong.MaxValue : uint.MaxValue) - 1, wide),
                Name("MParamEventCurveBase", 0), Record("MParamEventCurveRationalBezier", 0, payload, wide)),
            _ => collection
        };
        var body = ObjectBody(value, kind == 3 ? 0x624F7563U : 0x53634172U,
            kind == 3 ? (ushort)2 : kind == 4 ? (ushort)8 : (ushort)0x22);
        var bytes = Project(wide, 2, 1, body, before: before);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var note = Assert.Single(result.Value!.Arrangement!.MidiNoteCandidates!);
            Assert.Equal(-2.5, note.PositionTicks);
            Assert.Equal(120, note.DurationTicks);
            Assert.Equal(64, note.MidiPitch);
            Assert.Equal((ushort)0x8001, note.RawFlags);
            Assert.Contains("attributes were not applied", note.Evidence);
            var record = Assert.Single(Assert.Single(result.Value.ArchiveInventories!).Records,
                item => item.ClassName == "MMidiNote");
            Assert.Equal(10, record.RawFields!.Count);
            Assert.Contains(record.RawFields, field => field.RawHex == "7FF8000000001234");
            Assert.Contains(record.RawFields, field => field.RawHex == "8000000000000000");
            Assert.All(record.RawFields, field => Assert.Equal(Convert.ToHexString(
                bytes.AsSpan((int)field.FileOffset, field.ByteCount)), field.RawHex));
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.Single(snapshot.Value!.NativeMidiNoteCandidates!);
            Assert.Empty(snapshot.Value.MidiNotes ?? []);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoteLikeRecordsInsideTypedNoteAttributesNeverBecomeArrangementNotes(bool wide)
    {
        var fakeNote = Record("MMidiNote", 1, Body(2, 1, 0), wide);
        foreach (var supported in new[] { true, false })
        {
            var value = Record(supported ? "CmAttribCollection" : "UnsupportedCollection", 0, fakeNote, wide);
            var bytes = Project(wide, 2, 1, ObjectBody(value));
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
            try
            {
                await File.WriteAllBytesAsync(path, bytes);
                var result = await new CprStructureInspector().InspectAsync(path);
                Assert.True(result.IsSuccess);
                Assert.Equal(supported ? 1 : 0, result.Value!.Arrangement!.MidiNoteCandidates!.Count);
                Assert.Equal(2, Assert.Single(result.Value.ArchiveInventories!).Records.Count(r => r.ClassName == "MMidiNote"));
                if (!supported) Assert.Contains(result.Value.Arrangement.LimitReasons!, reason => reason.Contains("Attribute ID"));
            }
            finally { File.Delete(path); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedObjectUnknownClassesFlagsRevisionsAndForwardReferencesStayOpaque(bool wide)
    {
        var payload = new byte[] { 0, 1, 2, 3 };
        var collection = Record("CmAttribCollection", 0, payload, wide);
        var wrongLength = collection.ToArray();
        var width = wide ? 8 : 4;
        Number(10000, wide).CopyTo(wrongLength, wrongLength.Length - payload.Length - width);
        var placeholder = ObjectBody(Number(0, wide));
        var futureId = EventDeclaration(wide, 2).Length + Record("MMidiNote", 1, placeholder, wide).Length +
            collection.Length - payload.Length - width;
        var cases = new[]
        {
            ObjectBody(Record("OtherCollection", 0, payload, wide)),
            ObjectBody(Record("CmAttribCollection", 1, payload, wide)),
            ObjectBody(wrongLength), ObjectBody(Number(123456, wide)),
            ObjectBody(Number((ulong)futureId, wide)),
            ObjectBody(collection, flags: 8), ObjectBody(collection, flags: 0x102),
            ObjectBody(Record("MParamEventCurveRationalBezier", 0, payload, wide), 0x624F7563, 2),
            ObjectBody(collection)[..^1], Join(ObjectBody(collection), [0])
        };
        foreach (var body in cases)
        {
            var bytes = Project(wide, 2, 1, body, after: collection);
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
            try
            {
                await File.WriteAllBytesAsync(path, bytes);
                var result = await new CprStructureInspector().InspectAsync(path);
                Assert.True(result.IsSuccess, result.Error?.Message);
                Assert.Empty(result.Value!.Arrangement!.MidiNoteCandidates!);
                Assert.True(result.Value.Arrangement.Partial);
                Assert.All(Assert.Single(result.Value.ArchiveInventories!).Records.Where(r => r.ClassName == "MMidiNote"),
                    record => Assert.Empty(record.RawFields!));
            }
            finally { File.Delete(path); }
        }
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task LaterBaseEvidenceAndConflictingNamedNoteRevisionsCannotSelectEarlierBody(
        bool wide, bool futureBase, bool conflictingNote)
    {
        var bytes = Project(wide, 2, 1, Body(2, 1), futureBase: futureBase, conflictingNote: conflictingNote);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var report = result.Value!;
            Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
            var inventory = Assert.Single(report.ArchiveInventories!);
            Assert.All(inventory.Records.Where(record => record.ClassName == "MMidiNote"),
                record => Assert.Empty(record.RawFields!));
            Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("named MMidiNote"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, 0, 0, 11)]
    [InlineData(true, 0, 0, 11)]
    [InlineData(false, 0, 1, 11)]
    [InlineData(true, 0, 1, 11)]
    [InlineData(false, 1, 0, 13)]
    [InlineData(true, 1, 0, 13)]
    [InlineData(false, 1, 1, 13)]
    [InlineData(true, 1, 1, 13)]
    [InlineData(false, 2, 0, 45)]
    [InlineData(true, 2, 0, 45)]
    [InlineData(false, 2, 1, 45)]
    [InlineData(true, 2, 1, 45)]
    public async Task IndependentRevisionLayoutsExposeAnonymousBitsWithoutPromotingLegacyMidi(
        bool wide, int eventRevision, int noteRevision, int tailOffset)
    {
        var body = Body(eventRevision, noteRevision);
        var bytes = Project(wide, (ushort)eventRevision, (ushort)noteRevision, body);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var report = result.Value!;
            var inventory = Assert.Single(report.ArchiveInventories!);
            var declaration = Assert.Single(inventory.ClassDeclarations!, item => item.ClassName == "MMidiEvent");
            Assert.Equal(eventRevision, declaration.ClassVersion);
            Assert.Equal(inventory.PayloadFileOffset + (long)declaration.SymbolArchiveOffset,
                declaration.DeclarationFileOffset);
            var record = Assert.Single(inventory.Records, item => item.ClassName == "MMidiNote");
            Assert.Empty(record.PrefixClasses); // Base revision is archive-wide, not repeated locally.
            var fields = record.RawFields!;
            Assert.Equal(eventRevision + 7 + noteRevision, fields.Count);
            Assert.Equal("-2.5", fields[0].Value);
            Assert.Contains(fields, field => field.FileOffset == record.BodyFileOffset + tailOffset && field.Value == "120");
            Assert.Contains(fields, field => field.RawHex == "7FF8000000001234" && field.Value == "NaN");
            Assert.Contains(fields, field => field.RawHex == "8000000000000000");
            Assert.Equal(body.Length - (eventRevision == 2 ? 28 : 0), fields.Sum(field => field.ByteCount));
            Assert.All(fields, field =>
            {
                Assert.Equal(EvidenceLevel.Candidate, field.Evidence);
                Assert.Contains("semantics unverified", field.Source);
                Assert.Contains($"MMidiEvent serialization version {eventRevision}", field.Source);
                Assert.Contains($"MMidiNote serialization version {noteRevision}", field.Source);
                Assert.Equal(Convert.ToHexString(bytes.AsSpan((int)field.FileOffset, field.ByteCount)), field.RawHex);
            });
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Empty(snapshot.Value!.MidiNotes ?? []);
            var notes = snapshot.Value.NativeMidiNoteCandidates ?? [];
            if (eventRevision == 2 && noteRevision == 1) Assert.Single(notes);
            else Assert.Empty(notes);
            Assert.Contains("Class declarations", CprStructureInspector.ToMarkdown(report));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, -1, false)]
    [InlineData(true, -1, false)]
    [InlineData(false, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 3, false)]
    [InlineData(true, 65535, false)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    public async Task ModernShapedBytesCannotOverrideMissingConflictingOrUnsupportedBaseRevision(
        bool wide, int eventRevision, bool conflict)
    {
        var bytes = Project(wide, eventRevision < 0 ? null : (ushort)eventRevision, 1, Body(2, 1), conflict);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var report = result.Value!;
            Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
            Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("inherited MMidiEvent"));
            var record = Assert.Single(Assert.Single(report.ArchiveInventories!).Records,
                item => item.ClassName == "MMidiNote" && item.BodyBytes > 0);
            Assert.Empty(record.RawFields!);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnknownTailRevisionsAndVariableAttributePayloadsRemainOpaque()
    {
        var body = Body(2, 1);
        var record = new CprClassRecordCandidate("MMidiNote", 100, 100, body.Length,
            0, null, "inline-class-name", uint.MaxValue, 0, 1, []);
        Assert.Empty(CprBinaryFieldCandidateDecoder.Read(record, body, 100));
        Assert.Empty(CprBinaryFieldCandidateDecoder.Read(record with { ClassVersion = 2 }, body, 100, 2, 2));
        Assert.Empty(CprBinaryFieldCandidateDecoder.Read(record with { BodyBytes = body.Length - 1 }, body, 100, 2, 1));
        body[22] = 5; // Combined flags are not the exact scalar subset.
        Assert.Empty(CprBinaryFieldCandidateDecoder.Read(record, body, 100, 2, 1));
        body[22] = 8; // String payload does not have a fixed 14-byte stride.
        Assert.Empty(CprBinaryFieldCandidateDecoder.Read(record, body, 100, 2, 1));
    }
}
