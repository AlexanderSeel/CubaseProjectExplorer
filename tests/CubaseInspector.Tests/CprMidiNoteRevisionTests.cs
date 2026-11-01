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

    private static byte[] Project(bool wide, ushort? eventRevision, ushort noteRevision, byte[] body,
        bool conflictingRevision = false, bool futureBase = false, bool conflictingNote = false)
    {
        var declaration = eventRevision is ushort version
            ? Join(Number((wide ? ulong.MaxValue : uint.MaxValue) - 1, wide), Name("MMidiEvent", version),
                Record("UnrelatedCarrier", 0, [], wide))
            : Array.Empty<byte>();
        if (conflictingRevision)
            declaration = Join(declaration, Record("MMidiEvent", 1, [], wide));
        var note = Record("MMidiNote", noteRevision, body, wide);
        var archive = futureBase ? Join(note, declaration) : Join(declaration, note);
        if (conflictingNote) archive = Join(archive, Record("MMidiNote", 0, [], wide));
        var stream = Encoding.ASCII.GetBytes("Arrangement1");
        var cls = Encoding.ASCII.GetBytes("PArrangement");
        var chunks = Join(Chunk("ROOT", Join(Number((uint)stream.Length), stream,
                Number((uint)cls.Length), cls), wide), Chunk("ARCH", archive, wide));
        return Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"), Number((uint)chunks.Length, wide),
            Encoding.ASCII.GetBytes("NUND"), chunks);
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
