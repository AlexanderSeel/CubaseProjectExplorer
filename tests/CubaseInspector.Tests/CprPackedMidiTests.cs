using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprPackedMidiTests
{
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] U32(uint value)
    {
        var result = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(result, value); return result;
    }
    private static byte[] Name(string value) => Join(U32((uint)Encoding.UTF8.GetByteCount(value + "\0")), Encoding.UTF8.GetBytes(value + "\0"));
    private static byte[] PackedNote(double start = 120.125, double duration = 131.92000000000007,
        byte pitch = 36, byte velocity = 102)
    {
        var value = new byte[56]; value[0] = 0x90; value[9] = 2; value[10] = pitch; value[11] = velocity; value[17] = 1;
        Encoding.ASCII.GetBytes("adcn").CopyTo(value, 18); value[23] = 1;
        BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(1, 8), BitConverter.DoubleToInt64Bits(start));
        BinaryPrimitives.WriteInt64BigEndian(value.AsSpan(32, 8), BitConverter.DoubleToInt64Bits(duration));
        return value;
    }
    private static byte[] Record(string name, byte[] body) => Join(U32(uint.MaxValue), Name(name), [0, 1], U32((uint)body.Length), body);
    private static byte[] Chunk(string tag, byte[] body) => Join(Encoding.ASCII.GetBytes(tag), U32((uint)body.Length), body);

    [Fact]
    public void PreservesFractionalNegativeAndZeroValuesWithoutInventingTimebase()
    {
        var data = Join(Name("Part α"), new byte[16], PackedNote(-12.25, 0, 0, 0), PackedNote());
        var scan = CprMidiPartNoteScanner.Scan(data, 100);
        Assert.False(scan.Partial);
        Assert.Equal(2, scan.Notes.Count);
        Assert.Equal(100 + Name("Part α").Length + 16, scan.Notes[0].FileOffset);
        Assert.Equal(-12.25, scan.Notes[0].PositionTicks);
        Assert.Equal(0, scan.Notes[0].Velocity);
        Assert.Equal(0, scan.Notes[0].MidiPitch);
        Assert.Equal(0, scan.Notes[0].DurationTicks);
        Assert.Equal(131.92000000000007, scan.Notes[1].DurationTicks);
        Assert.Equal("Part α", scan.Notes[0].CandidatePartName);
        Assert.Equal(2, scan.Notes[0].Channel);
        Assert.All(scan.Notes, note => Assert.Equal("RIFF packed MMidiPart", note.SourceLayout));
    }

    [Fact]
    public void RequiresBoundedNameTypedAttributesAndCompleteTail()
    {
        var note = PackedNote(); note[23] = 8;
        Assert.Empty(CprMidiPartNoteScanner.Scan(Join(Name("Part"), note), 0).Notes);
        Assert.Empty(CprMidiPartNoteScanner.Scan(PackedNote(), 0).Notes);
        Assert.Empty(CprMidiPartNoteScanner.Scan(Join(Name("Part"), PackedNote()[..^1]), 0).Notes);
        note = PackedNote(); note[17] = 255;
        Assert.Empty(CprMidiPartNoteScanner.Scan(Join(Name("Part"), note), 0).Notes);
    }

    [Fact]
    public void ClassBodySizeCannotMasqueradeAsCompactNoteStatusOrConsumeTheNoteBudget()
    {
        var body = new byte[154]; body[16] = 8; body[9] = 60; body[10] = 100;
        for (var i = 0; i < 8; i++) { Encoding.ASCII.GetBytes("adcn").CopyTo(body, 17 + i * 14); body[22 + i * 14] = 1; }
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(129, 8), BitConverter.DoubleToInt64Bits(240));
        var record = Record("MMidiNote", body); // Last body-size byte 0x9A resembles note-on status.
        var name = Name("Part"); var data = Join(name, record, PackedNote());
        var scan = CprMidiPartNoteScanner.Scan(data, 100, maximumNotes: 1,
            excludedRecords: [new CprUninterpretedRange(100 + name.Length, record.Length)]);
        Assert.Single(scan.Notes);
        Assert.Equal(100 + name.Length + record.Length, scan.Notes[0].FileOffset);
        Assert.Equal(36, scan.Notes[0].MidiPitch);
    }

    [Fact]
    public void ReportsSafetyLimitsAndHonorsCancellation()
    {
        var data = Join(Name("Part"), PackedNote(), PackedNote());
        var limited = CprMidiPartNoteScanner.Scan(data, 0, maximumNotes: 1);
        Assert.Single(limited.Notes); Assert.True(limited.Partial);
        Assert.True(CprMidiPartNoteScanner.Scan(data, 0, maximumBytes: 8).Partial);
        Assert.Throws<OperationCanceledException>(() => CprMidiPartNoteScanner.Scan(data, 0,
            cancellationToken: new CancellationToken(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CprMidiPartNoteScanner.Scan(data, long.MaxValue));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(true, 6)]
    public void RecordNotesUseDeclaredAttributeCountInsteadOfFixedBodyLength(bool wide, int count)
    {
        var body = new byte[17 + count * 14 + 25];
        body[8] = 2; body[9] = 64; body[10] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(15, 2), (ushort)count);
        for (var i = 0; i < count; i++)
        {
            Encoding.ASCII.GetBytes("adcn").CopyTo(body, 17 + i * 14); body[22 + i * 14] = 1;
        }
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(17 + count * 14, 8), BitConverter.DoubleToInt64Bits(1.125));
        Assert.True(CprMidiPartNoteScanner.TryReadRecord(body, 42, wide, out var note));
        Assert.Equal(1.125, note.DurationTicks); Assert.Equal(0, note.Velocity); Assert.Equal(64, note.MidiPitch);
        Assert.StartsWith(wide ? "RIF2" : "RIFF", note.SourceLayout);
        Assert.False(CprMidiPartNoteScanner.TryReadRecord(body[..^1], 42, wide, out _));
        body[16]++;
        Assert.False(CprMidiPartNoteScanner.TryReadRecord(body, 42, wide, out _));
    }

    [Fact]
    public void NativeNoteRevisionGateDoesNotApplyRevisionOneOffsetsToRevisionZero()
    {
        var body = new byte[112];
        body[8] = 2; body[9] = 64; body[10] = 100;
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(13, 4), 5);
        for (var index = 0; index < 5; index++)
        {
            Encoding.ASCII.GetBytes("adcn").CopyTo(body, 17 + index * 14);
            body[22 + index * 14] = 1;
        }
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(87, 8), BitConverter.DoubleToInt64Bits(24));

        Assert.True(CprMidiPartNoteScanner.TryReadRecord(body, 0, true, out _, classVersion: 1));
        Assert.False(CprMidiPartNoteScanner.TryReadRecord(body, 0, true, out _, classVersion: 0));
        Assert.False(CprMidiPartNoteScanner.TryReadRecord(body, 0, true, out _, classVersion: 2));
    }

    [Fact]
    public void NativeFlagsArePreservedAndDoNotDiscardValidNotes()
    {
        var packed = PackedNote();
        BinaryPrimitives.WriteUInt16BigEndian(packed.AsSpan(12, 2), 0x8001);
        var note = Assert.Single(CprMidiPartNoteScanner.Scan(Join(Name("Part"), packed), 100).Notes);
        Assert.Equal((ushort)0x8001, note.RawFlags);
        var body = new byte[56]; body[16] = 1; body[9] = 60;
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(11, 2), 0x4002);
        Encoding.ASCII.GetBytes("adcn").CopyTo(body, 17); body[22] = 1;
        Assert.True(CprMidiPartNoteScanner.TryReadRecord(body, 0, true, out var record));
        Assert.Equal((ushort)0x4002, record.RawFlags);
        body[13] = 1; // Full u32 count must be bounded, not just its last byte.
        Assert.False(CprMidiPartNoteScanner.TryReadRecord(body, 0, true, out _));
    }

    [Fact]
    public async Task RiffPartNotesReachIndexWithCandidateContextAndNoNormalizedMidi()
    {
        var track = Record("MMidiTrackEvent", Join(Record("MListNode", Name("Keys")),
            Record("MMidiPartEvent", Record("MMidiPart", Join(Name("Verse"), new byte[16], PackedNote())))));
        var root = Join(U32(12), Encoding.ASCII.GetBytes("Arrangement1"), U32(12), Encoding.ASCII.GetBytes("PArrangement"));
        var chunks = Join(Chunk("ROOT", root), Chunk("ARCH", track));
        var data = Join("RIFF"u8.ToArray(), U32((uint)chunks.Length), "NUND"u8.ToArray(), chunks);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, data);
            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var snapshot = result.Value!;
            Assert.Empty(snapshot.MidiNotes ?? []);
            var note = Assert.Single(snapshot.NativeMidiNoteCandidates!);
            Assert.Equal("Keys", note.CandidateTrackName); Assert.Equal("Verse", note.CandidatePartName);
            Assert.NotNull(note.CandidateTrackOffset); Assert.NotNull(note.CandidatePartOffset);
            var index = ProjectAssetIndexer.Build(snapshot);
            Assert.Empty(index.MidiTracks);
            var indexed = Assert.Single(index.NativeMidiNoteCandidates!);
            Assert.Contains("RIFF MMidiPart", indexed.Warning);
            Assert.DoesNotContain("Cubase 15.0.40", indexed.Warning);
            Assert.Equal(EvidenceLevel.Candidate, indexed.Evidence);
            Assert.Equal("Verse", Assert.Single(NativeMidiBrowser.Group(index.NativeMidiNoteCandidates!)).Name);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }
}
