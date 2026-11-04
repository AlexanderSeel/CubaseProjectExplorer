using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprAttributeContainerTests
{
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] U32(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); return b; }
    private static byte[] U16(ushort value) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, value); return b; }
    private static byte[] Width(ulong value, bool wide)
    {
        if (!wide) return U32((uint)value);
        var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, value); return b;
    }
    private static byte[] Wire(byte[] value) => Join(U32((uint)value.Length), value);
    private static byte[] Text(string value) => Wire(Encoding.ASCII.GetBytes(value + "\0"));
    private static byte[] Record(string name, ushort revision, byte[] body, bool wide) =>
        Join(Width(wide ? ulong.MaxValue : uint.MaxValue, wide), Text(name), U16(revision), Width((uint)body.Length, wide), body);
    private static byte[] Chunk(string name, byte[] body, bool wide) =>
        Join(Encoding.ASCII.GetBytes(name), Width((uint)body.Length, wide), body);
    private static byte[] TypeToken => Encoding.ASCII.GetBytes("0123456789ABCDEFabcdef0123456789\0");
    private static byte[] Property(ushort type, byte[] value) => Join(Text("stored"), U16(type), value);
    private static byte[] Container(params byte[][] properties) => Join(Wire(TypeToken), U32((uint)properties.Length), Join(properties));
    private static byte[] Attribute(byte[] value, ushort flags = 0x100) => Join("cont"u8.ToArray(), U16(flags), value);

    private static byte[] Note(byte pitch, byte[]? attribute = null)
    {
        var prefix = new byte[17];
        BinaryPrimitives.WriteInt64BigEndian(prefix, BitConverter.DoubleToInt64Bits(-2.5));
        prefix[8] = 2; prefix[9] = pitch; prefix[10] = 100;
        BinaryPrimitives.WriteUInt16BigEndian(prefix.AsSpan(11), 0x8001);
        BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(13), attribute is null ? 0U : 2U);
        var tail = new byte[25];
        BinaryPrimitives.WriteInt64BigEndian(tail, BitConverter.DoubleToInt64Bits(120));
        BinaryPrimitives.WriteUInt64BigEndian(tail.AsSpan(8), 0x7FF8000000001234);
        BinaryPrimitives.WriteUInt64BigEndian(tail.AsSpan(16), 0x8000000000000000);
        tail[^1] = 0xAB;
        return Join(prefix, attribute ?? [], attribute is null ? [] : Join("next"u8.ToArray(), U16(4), new byte[8]), tail);
    }

    private static byte[] Project(bool wide, int kind, byte[] container, ushort flags = 0x100)
    {
        var declarations = Join(Record("MMidiEvent", 2, [], wide), Record("MMidiNote", 1, [], wide));
        var attribute = Attribute(container, flags);
        byte[] archive;
        if (kind == 1)
            archive = Join(declarations, Record("MMidiNote", 1, Note(61, attribute), wide),
                Record("MMidiNote", 1, Note(62), wide));
        else
        {
            byte[] events;
            if (kind == 2)
            {
                declarations = Join(declarations, Record("MEvent", 1, [], wide), Record("MScoreEvent", 1, [], wide));
                var score = Join([0x30], new byte[26], Width(0, wide), U32(1), attribute, new byte[10]);
                events = Join([0x90], Note(61), score, [0x90], Note(62));
            }
            else events = Join([0x90], Note(61, attribute), [0x90], Note(62));
            var part = Join(Text("Part"), U32(0), Width(0, wide), Width(0, wide), U32(kind == 2 ? 3U : 2U),
                [(byte)(kind == 2 ? 4 : 2)], declarations, events);
            archive = Record("MMidiPart", 2, part, wide);
        }
        var root = Chunk("ROOT", Join(TextWithoutNull("Arrangement1"), TextWithoutNull("PArrangement")), wide);
        var chunks = Join(root, Chunk("ARCH", archive, wide));
        return Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"), Width((uint)chunks.Length, wide), "NUND"u8.ToArray(), chunks);
    }
    private static byte[] TextWithoutNull(string value) => Wire(Encoding.ASCII.GetBytes(value));

    private static async Task WithProject(byte[] source, Func<string, CprStructureReport, Task> check)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, source);
            var read = await new CprStructureInspector().InspectAsync(path);
            Assert.True(read.IsSuccess, read.Error?.Message);
            await check(path, read.Value!);
            Assert.Equal(source, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)]
    [InlineData(false, 1)] [InlineData(true, 1)]
    [InlineData(false, 2)] [InlineData(true, 2)]
    public async Task PrimitivePropertiesStayOpaqueAcrossCountedTypedAndScoreEvents(bool wide, int kind)
    {
        var fakeNote = Record("MMidiNote", 1, Note(99), wide);
        var value = Container(Property(1, new byte[8]), Property(4, Enumerable.Repeat((byte)0xFF, 8).ToArray()),
            Property(8, Wire(fakeNote)));
        var source = Project(wide, kind, value);
        await WithProject(source, async (path, report) =>
        {
            Assert.False(report.Arrangement!.Partial);
            var notes = report.Arrangement.MidiNoteCandidates!;
            Assert.Equal(new[] { 61, 62 }, notes.Select(note => note.MidiPitch));
            Assert.All(notes, note => { Assert.Equal(-2.5, note.PositionTicks); Assert.Equal(120, note.DurationTicks); });
            Assert.Equal(source.Length - (kind == 1 ? 42 : 43), notes[1].FileOffset);
            if (kind == 1)
            {
                var raw = report.ArchiveInventories!.SelectMany(inv => inv.Records)
                    .First(record => record.ClassName == "MMidiNote" && record.BodyBytes > 0).RawFields!;
                Assert.Equal(10, raw.Count);
                Assert.Contains(raw, field => field.RawHex == "7FF8000000001234");
                Assert.Contains(raw, field => field.RawHex == "8000000000000000");
            }
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Empty(snapshot.Value!.MidiNotes ?? []);
            Assert.Equal(2, snapshot.Value.NativeMidiNoteCandidates!.Count);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EmptyAndBoundedPropertyCountsAndTypeTokenFormsHaveExactBoundaries(bool wide)
    {
        var digits = TypeToken[..^1];
        var tokens = new[] { digits, TypeToken, Join([0xEF, 0xBB, 0xBF], TypeToken), Join(TypeToken, [0xEF, 0xBB, 0xBF]) };
        foreach (var token in tokens)
            foreach (var count in new[] { 0, 256 })
            {
                var value = Join(Wire(token), U32((uint)count), Join(Enumerable.Repeat(Property(1, new byte[8]), count).ToArray()));
                await WithProject(Project(wide, 0, value), (_, report) =>
                {
                    Assert.False(report.Arrangement!.Partial);
                    Assert.Equal(new[] { 61, 62 }, report.Arrangement.MidiNoteCandidates!.Select(note => note.MidiPitch));
                    return Task.CompletedTask;
                });
            }
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)]
    [InlineData(false, 1)] [InlineData(true, 1)]
    [InlineData(false, 2)] [InlineData(true, 2)]
    public async Task UnsupportedPropertiesAndMalformedLengthsCannotExposeLaterNotes(bool wide, int kind)
    {
        var fakeNote = Join([0x90], Note(99));
        var invalid = new List<byte[]>
        {
            Join(Wire(TypeToken), U32(uint.MaxValue), fakeNote),
            Join(Wire(TypeToken), U32(257), fakeNote),
            Join(Text("not-a-type-token"), U32(0), fakeNote),
            Join(Wire(TypeToken), U32(1), U32(uint.MaxValue), fakeNote),
            Join(Wire(TypeToken), U32(1), Text("stored"), U16(8), U32(50_001), fakeNote),
            Join(Wire(TypeToken), U32(1), Text("stored"), U16(8), U32(uint.MaxValue), fakeNote),
            Join(Wire(TypeToken), U32(1), Text("stored"), U16(8), U32(5000), fakeNote)
        };
        invalid.Add(Container(Property(2, Record("MMidiNote", 1, Note(99), wide))));
        invalid.AddRange(new ushort[] { 0, 2, 3, 5, 0x108, ushort.MaxValue }.Select(type => Container(Property(type, fakeNote))));
        foreach (var value in invalid)
            await WithProject(Project(wide, kind, value), (_, report) =>
            {
                Assert.True(report.Arrangement!.Partial);
                var pitches = report.Arrangement.MidiNoteCandidates!.Select(note => note.MidiPitch).ToArray();
                // Typed records retain the later independently framed note;
                // score streams retain their earlier complete note only.
                Assert.Equal(kind == 1 ? new[] { 62 } : kind == 2 ? new[] { 61 } : [], pitches);
                Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("Container", StringComparison.Ordinal));
                return Task.CompletedTask;
            });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ContainerByteBudgetIsInclusiveAndCannotBorrowALaterBoundary(bool wide)
    {
        var first = Property(8, Wire(new byte[50_000]));
        var overhead = Container(first, Property(8, Wire([]))).Length;
        foreach (var extra in new[] { 0, 1 })
        {
            var value = Container(first, Property(8, Wire(new byte[65536 - overhead + extra])));
            Assert.Equal(65536 + extra, value.Length);
            await WithProject(Project(wide, 0, value), (_, report) =>
            {
                Assert.Equal(extra != 0, report.Arrangement!.Partial);
                Assert.Equal(extra == 0 ? 2 : 0, report.Arrangement.MidiNoteCandidates!.Count);
                return Task.CompletedTask;
            });
        }
    }
}
