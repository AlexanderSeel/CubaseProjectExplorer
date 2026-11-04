using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

/// <summary>
/// Black-box wire fixtures for the counted MMidiPart stream. Zero-attribute
/// events deliberately cannot be recovered by the weaker byte-pattern scanner.
/// </summary>
public sealed class CprCountedMidiStreamTests
{
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();

    private static byte[] Number(ulong value, bool wide = false)
    {
        var bytes = new byte[wide ? 8 : 4];
        if (wide) BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
        return bytes;
    }

    private static byte[] Text(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        return Join(Number((uint)bytes.Length), bytes);
    }

    private static byte[] ClassName(string name, ushort revision)
    {
        var version = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(version, revision);
        return Join(Text(name), version);
    }

    private static byte[] Record(string name, ushort revision, byte[] body, bool wide) =>
        Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide), ClassName(name, revision),
            Number((uint)body.Length, wide), body);

    private static byte[] Declarations(bool wide, ushort? baseRevision = 2, ushort? noteRevision = 1)
    {
        if (noteRevision is not ushort note)
            return baseRevision is ushort baseOnly ? Record("MMidiEvent", baseOnly, [], wide) : [];
        var prefix = baseRevision is ushort version
            ? Join(Number((wide ? ulong.MaxValue : uint.MaxValue) - 1, wide), ClassName("MMidiEvent", version))
            : Array.Empty<byte>();
        return Join(prefix, Record("MMidiNote", note, [], wide));
    }

    private static byte[] Event(byte status = 0x90, double position = -12.25, double duration = 0,
        byte pitch = 0, byte velocity = 0, ushort attributeType = 0)
    {
        var attributes = attributeType == 0 ? 0 : 1;
        var bytes = new byte[43 + attributes * 14];
        bytes[0] = status;
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(1), BitConverter.DoubleToInt64Bits(position));
        bytes[9] = 2; bytes[10] = pitch; bytes[11] = velocity;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 0x8001);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(14), (uint)attributes);
        if (attributes != 0)
        {
            Encoding.ASCII.GetBytes("test").CopyTo(bytes, 18);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(22), attributeType);
        }
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(18 + attributes * 14), BitConverter.DoubleToInt64Bits(duration));
        bytes[^1] = 0xAB;
        return bytes;
    }

    private sealed record PartStream(byte[] Bytes, int EventOffset);

    private static byte[] ScalarEvent(byte status, ushort attributeType = 0) =>
        Event(status, attributeType: attributeType)[..(18 + (attributeType == 0 ? 0 : 14))];

    private static byte[] SysexEvent(byte[] payload, int? declaredBytes = null, uint secondWord = 0,
        ushort attributeType = 0) =>
        Join(ScalarEvent(0xF0, attributeType), Number(unchecked((uint)(declaredBytes ?? payload.Length))),
            Number(secondWord), payload);

    private static byte[] NestedEvent(byte[] value, byte status = 0x90, ushort flags = 0x22, uint id = 0x53634172)
    {
        var note = Event(status, position: 120.5, duration: 240.25, pitch: 61, velocity: 100);
        var header = note[..18];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(14), 2);
        var type = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(type, flags);
        // A second scalar follows the variable object. Its exact boundary
        // must be found before locating the three-double note suffix.
        var scalar = Event(attributeType: 4)[18..32];
        return Join(header, Number(id), type, value, scalar,
            status is 0x80 or 0x90 ? note[^25..] : []);
    }

    private static PartStream Stream(bool wide, byte[][] events, int? count = null,
        byte[]? declarations = null, byte declarationCount = 1, uint timebaseKind = 0)
    {
        var timebase = timebaseKind is 0 or 17
            ? Join(Number(0, wide), Number(0, wide))
            : new byte[8];
        var prefix = Join(Text("Part α"), Number(timebaseKind), timebase, Number((uint)(count ?? events.Length)));
        if ((count ?? events.Length) == 0) return new(prefix, prefix.Length);
        var table = Join([declarationCount], declarations ?? Declarations(wide));
        return new(Join(prefix, table, Join(events)), prefix.Length + table.Length);
    }

    private sealed record TestProject(byte[] Bytes, int FirstEventOffset, int ArchiveOffset);

    private static TestProject Project(bool wide, PartStream stream, ushort partRevision = 2,
        byte[]? before = null, byte[]? after = null)
    {
        var part = Record("MMidiPart", partRevision, stream.Bytes, wide);
        var partEvent = Record("MMidiPartEvent", 2, part, wide);
        var track = Record("MMidiTrackEvent", 4, partEvent, wide);
        var archive = Join(before ?? [], track, after ?? []);
        var rootBody = Join(Number(12), Encoding.ASCII.GetBytes("Arrangement1"),
            Number(12), Encoding.ASCII.GetBytes("PArrangement"));
        var root = Join("ROOT"u8.ToArray(), Number((uint)rootBody.Length, wide), rootBody);
        var chunks = Join(root, "ARCH"u8.ToArray(), Number((uint)archive.Length, wide), archive);
        var projectHeader = Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"),
            Number((uint)chunks.Length, wide), "NUND"u8.ToArray());
        var archiveOffset = projectHeader.Length + root.Length + 4 + (wide ? 8 : 4);
        var offset = archiveOffset + (before?.Length ?? 0) + track.Length - stream.Bytes.Length + stream.EventOffset;
        return new(Join(projectHeader, chunks), offset, archiveOffset);
    }

    private static async Task WithProject(TestProject project, Func<string, CprStructureReport, Task> check)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, project.Bytes);
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            await check(path, result.Value!);
            Assert.Equal(project.Bytes, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, 1, 0x80, 0)]
    [InlineData(true, 1, 0x80, 0)]
    [InlineData(false, 2, 0x90, 0)]
    [InlineData(true, 2, 0x90, 0)]
    [InlineData(false, 2, 0x90, 1)]
    [InlineData(true, 2, 0x90, 15)]
    [InlineData(false, 2, 0x80, 17)]
    [InlineData(true, 2, 0x80, 17)]
    public async Task ReadsCompleteCountedZeroAttributeEventsWithoutNormalizingMidi(
        bool wide, int revision, int status, int timebaseKind)
    {
        var first = Event((byte)status);
        var second = Event(0x90, 120.5, 240.25, 127, 127);
        var project = Project(wide, Stream(wide, [first, second], timebaseKind: (uint)timebaseKind), (ushort)revision);
        await WithProject(project, async (path, report) =>
        {
            var notes = report.Arrangement!.MidiNoteCandidates!;
            Assert.Equal(2, notes.Count);
            Assert.Equal(project.FirstEventOffset, notes[0].FileOffset);
            Assert.Equal(project.FirstEventOffset + 43, notes[1].FileOffset);
            Assert.Equal((byte)status, project.Bytes[(int)notes[0].FileOffset]);
            Assert.Equal(-12.25, notes[0].PositionTicks);
            Assert.Equal(0, notes[0].DurationTicks);
            Assert.Equal(0, notes[0].MidiPitch);
            Assert.Equal(0, notes[0].Velocity);
            Assert.Equal(120.5, notes[1].PositionTicks);
            Assert.Equal(240.25, notes[1].DurationTicks);
            Assert.Equal(127, notes[1].MidiPitch);
            Assert.Equal(127, notes[1].Velocity);
            Assert.All(notes, note =>
            {
                Assert.Equal((wide ? "RIF2" : "RIFF") + " MMidiPart/counted-events", note.SourceLayout);
                Assert.Equal("Part α", note.CandidatePartName);
                Assert.Equal(2, note.Channel);
                Assert.Equal((ushort)0x8001, note.RawFlags);
                Assert.NotNull(note.CandidateTrackOffset);
                Assert.NotNull(note.CandidatePartOffset);
            });
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Empty(snapshot.Value!.MidiNotes ?? []);
            Assert.Equal(2, snapshot.Value.NativeMidiNoteCandidates!.Count);
            Assert.Equal(EvidenceLevel.Candidate, snapshot.Value.Evidence);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteCountsUnknownStatusesAndVariablePayloadsCannotResynchronize(bool wide)
    {
        var good = Event();
        var valid = Stream(wide, [good]);
        var cases = new[]
        {
            Stream(wide, [good, good], count: 1),
            Stream(wide, [good], count: 2),
            Stream(wide, [good[..^1]]),
            Stream(wide, [Event(0x91)]),
            // Plain flag 8 with an 8-byte null is now valid RIF2 CmString framing.
            Stream(wide, [Event(attributeType: 0x108)]),
            Stream(wide, [good], declarationCount: 0xFF),
            Stream(wide, [good], declarations: [], declarationCount: 0),
            Stream(wide, [good], timebaseKind: 16),
            valid with { Bytes = Join(valid.Bytes, [0x44]) }
        };
        foreach (var stream in cases)
        {
            var project = Project(wide, stream);
            await WithProject(project, (_, report) =>
            {
                Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
                return Task.CompletedTask;
            });
        }

        var interrupted = Project(wide, Stream(wide, [good, Event(0x91), Event()]));
        await WithProject(interrupted, (_, report) =>
        {
            var arrangement = report.Arrangement!;
            Assert.Single(arrangement.MidiNoteCandidates!);
            Assert.True(arrangement.Partial);
            Assert.Contains(arrangement.LimitReasons!, reason => reason.Contains("unsupported packed MIDI event discriminator 0x91", StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedAttributeStopsBeforeInferringLaterNoteOrAttributeBoundaries(bool wide)
    {
        var nested = Event(attributeType: 0x22);
        Encoding.ASCII.GetBytes("ScAr").CopyTo(nested, 18);
        Number(0x123456, wide).CopyTo(nested, 24);
        var project = Project(wide, Stream(wide, [Event(), nested, Event(pitch: 99)]));
        await WithProject(project, (_, report) =>
        {
            var arrangement = report.Arrangement!;
            Assert.True(arrangement.Partial);
            Assert.Empty(arrangement.MidiNoteCandidates!);
            Assert.Contains(arrangement.LimitReasons!, reason => reason.Contains("Attribute ID 0x53634172, flags 0x0022") &&
                reason.Contains("nested CmObject") && reason.Contains("no later attribute boundary was inferred"));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FramedScoreAttributesStayOpaqueAndPreserveFollowingScalarAndNotes(bool wide)
    {
        var payload = Event(pitch: 99, attributeType: 1);
        var packet = NestedEvent(Record("CmAttribCollection", 0, payload, wide));
        var project = Project(wide, Stream(wide, [packet, Event(pitch: 62)]));
        await WithProject(project, async (path, report) =>
        {
            var notes = report.Arrangement!.MidiNoteCandidates!;
            Assert.False(report.Arrangement.Partial);
            Assert.Equal(new[] { 61, 62 }, notes.Select(note => note.MidiPitch));
            Assert.Equal(120.5, notes[0].PositionTicks);
            Assert.Equal(240.25, notes[0].DurationTicks);
            Assert.Equal(project.FirstEventOffset + packet.Length, notes[1].FileOffset);
            Assert.Contains("attributes were not applied", notes[0].Evidence);
            var snapshot = (await new CprEvidenceReader().ReadAsync(path)).Value!;
            Assert.Equal(2, snapshot.NativeMidiNoteCandidates!.Count);
            return;
        });
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task StringAttributeInlineNullAndEarlierReferencesPreserveFollowingFields(bool wide, int kind)
    {
        var payload = Event(pitch: 99, attributeType: 1);
        var earlier = Record("CmString", 0, payload, wide);
        var id = (ulong)(earlier.Length - payload.Length - (wide ? 8 : 4));
        var value = kind switch { 1 => Number(0, wide), 2 => Number(id, wide), _ => earlier };
        var packet = NestedEvent(value, flags: 8, id: 0x74657874);
        var project = Project(wide, Stream(wide, [packet, Event(pitch: 62)]), before: kind == 2 ? earlier : []);
        await WithProject(project, async (path, report) =>
        {
            Assert.False(report.Arrangement!.Partial);
            var notes = report.Arrangement.MidiNoteCandidates!;
            Assert.Equal(new[] { 61, 62 }, notes.Select(note => note.MidiPitch));
            Assert.Equal(120.5, notes[0].PositionTicks);
            Assert.Equal(240.25, notes[0].DurationTicks);
            Assert.Equal(project.FirstEventOffset + packet.Length, notes[1].FileOffset);
            Assert.Contains("attributes were not applied", notes[0].Evidence);
            Assert.Equal(2, (await new CprEvidenceReader().ReadAsync(path)).Value!.NativeMidiNoteCandidates!.Count);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedNoteLikeBytesInsideAStringCannotBecomeAnArrangementEvent(bool wide)
    {
        var payload = Record("MMidiNote", 1, Event(pitch: 99)[1..], wide);
        var packet = NestedEvent(Record("CmString", 0, payload, wide), flags: 8, id: 0x74657874);
        var project = Project(wide, Stream(wide, [packet, Event(pitch: 62)]));
        await WithProject(project, (_, report) =>
        {
            Assert.False(report.Arrangement!.Partial);
            Assert.Equal(new[] { 61, 62 }, report.Arrangement.MidiNoteCandidates!.Select(note => note.MidiPitch));
            Assert.Contains(report.ArchiveInventories!.SelectMany(i => i.Records),
                record => record.ClassName == "MMidiNote" && record.BodyBytes > 0);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownStringFlagsClassesRevisionsLengthsAndReferencesStayOpaque(bool wide)
    {
        var payload = Event(pitch: 99, attributeType: 1);
        var value = Record("CmString", 0, payload, wide);
        var wrongLength = value.ToArray();
        Number(100000, wide).CopyTo(wrongLength, wrongLength.Length - payload.Length - (wide ? 8 : 4));
        var cases = new[]
        {
            NestedEvent(Record("CmString", 1, payload, wide), flags: 8),
            NestedEvent(Record("OtherString", 0, payload, wide), flags: 8),
            NestedEvent(value, flags: 0x28), NestedEvent(value, flags: 0x108),
            NestedEvent(value, flags: 8, id: 0x01020304),
            NestedEvent(wrongLength, flags: 8), NestedEvent(Number(123456, wide), flags: 8)
        };
        foreach (var packet in cases)
        {
            var project = Project(wide, Stream(wide, [packet, Event(pitch: 62)]));
            await WithProject(project, (_, report) =>
            {
                Assert.True(report.Arrangement!.Partial);
                Assert.Empty(report.Arrangement.MidiNoteCandidates!);
                Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("Attribute ID"));
                return Task.CompletedTask;
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScoreAttributeNullAndEarlierCollectionReferencesHaveModeWidth(bool wide)
    {
        var payload = Event(pitch: 99, attributeType: 1);
        var earlier = Record("CmAttribCollection", 0, payload, wide);
        var id = (ulong)(earlier.Length - payload.Length - (wide ? 8 : 4));
        var project = Project(wide, Stream(wide, [NestedEvent(Number(0, wide)), NestedEvent(Number(id, wide)), Event()]),
            before: earlier);
        await WithProject(project, (_, report) =>
        {
            Assert.False(report.Arrangement!.Partial);
            Assert.Equal(3, report.Arrangement.MidiNoteCandidates!.Count);
            Assert.DoesNotContain(report.Arrangement.MidiNoteCandidates!, note => note.MidiPitch == 99);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedCollectionsCanBeSkippedInSupportedNonNoteEvents(bool wide)
    {
        var packet = NestedEvent(Record("CmAttribCollection", 0, Event(pitch: 99, attributeType: 1), wide), 0xB0);
        var declarations = Join(Declarations(wide), Record("MMidiController", 0, [], wide));
        var project = Project(wide, Stream(wide, [packet, Event(pitch: 62)], declarations: declarations, declarationCount: 2));
        await WithProject(project, (_, report) =>
        {
            Assert.False(report.Arrangement!.Partial);
            Assert.Equal(62, Assert.Single(report.Arrangement.MidiNoteCandidates!).MidiPitch);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BezierAttributeUsesExplicitBaseRevisionAndOpaqueFramedBody(bool wide)
    {
        var body = Event(pitch: 99, attributeType: 1);
        var prefix = Join(Number((wide ? ulong.MaxValue : uint.MaxValue) - 1, wide), ClassName("MParamEventCurveBase", 0));
        var curve = Join(prefix, Record("MParamEventCurveRationalBezier", 0, body, wide));
        var project = Project(wide, Stream(wide, [NestedEvent(curve, flags: 2, id: 0x624F7563), Event(pitch: 62)]));
        await WithProject(project, (_, report) =>
        {
            Assert.False(report.Arrangement!.Partial);
            Assert.Equal(new[] { 61, 62 }, report.Arrangement.MidiNoteCandidates!.Select(note => note.MidiPitch));
            Assert.Contains("attributes were not applied", report.Arrangement.MidiNoteCandidates![0].Evidence);
            return Task.CompletedTask;
        });

        var unsupported = Project(wide, Stream(wide,
            [NestedEvent(Record("MParamEventCurveRationalBezier", 0, body, wide), flags: 2, id: 0x624F7563)]),
            before: Record("MParamEventCurveBase", 1, [], wide));
        await WithProject(unsupported, (_, report) =>
        {
            Assert.True(report.Arrangement!.Partial);
            Assert.Empty(report.Arrangement.MidiNoteCandidates!);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ForwardObjectReferencesCannotBorrowAFutureCollection(bool wide, bool stringValue)
    {
        var payload = Event(pitch: 99, attributeType: 1);
        var future = Record(stringValue ? "CmString" : "CmAttribCollection", 0, payload, wide);
        var packet = NestedEvent(Number(0, wide), flags: stringValue ? (ushort)8 : (ushort)0x22);
        var project = Project(wide, Stream(wide, [packet, Event()]), after: future);
        var id = (ulong)(project.Bytes.Length - payload.Length - (wide ? 8 : 4) - project.ArchiveOffset);
        Number(id, wide).CopyTo(project.Bytes, project.FirstEventOffset + 24);
        await WithProject(project, (_, report) =>
        {
            Assert.True(report.Arrangement!.Partial);
            Assert.Empty(report.Arrangement.MidiNoteCandidates!);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NestedObjectByteBudgetRetainsOnlyPrecedingCompleteNotes(bool wide, bool stringValue)
    {
        var payload = new byte[4 * 1024 * 1024];
        Event(pitch: 99, attributeType: 1).CopyTo(payload, 100);
        var packet = NestedEvent(Record(stringValue ? "CmString" : "CmAttribCollection", 0, payload, wide),
            flags: stringValue ? (ushort)8 : (ushort)0x22);
        var project = Project(wide, Stream(wide, [Event(pitch: 48), packet, Event()]));
        await WithProject(project, (_, report) =>
        {
            Assert.True(report.Arrangement!.Partial);
            Assert.Equal(48, Assert.Single(report.Arrangement.MidiNoteCandidates!).MidiPitch);
            Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("byte scan limit"));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedTrailerCannotFallBackIntoOpaqueNestedObjects(bool wide)
    {
        var payload = Event(pitch: 99, attributeType: 1);
        var stream = Stream(wide, [NestedEvent(Record("CmAttribCollection", 0, payload, wide)), Event()]);
        var project = Project(wide, stream with { Bytes = Join(stream.Bytes, [0x44]) });
        await WithProject(project, (_, report) =>
        {
            Assert.True(report.Arrangement!.Partial);
            Assert.Empty(report.Arrangement.MidiNoteCandidates!);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EarlierClassDeclarationsStillFrameNestedValuesBeyondInventoryDetailBudget(bool wide, bool stringValue)
    {
        var declarations = Declarations(wide);
        var collectionDeclaration = Record(stringValue ? "CmString" : "CmAttribCollection", 0, [], wide);
        var filler = Record("Opaque", 0, [0], wide);
        var before = Join(declarations, collectionDeclaration, Join(Enumerable.Repeat(filler, 10_000).ToArray()));
        var payload = Event(pitch: 99, attributeType: 1);
        var value = Join(Number((wide ? 0x8000000000000000UL : 0x80000000UL) | (uint)declarations.Length, wide),
            Number((uint)payload.Length, wide), payload);
        var packet = NestedEvent(value, flags: stringValue ? (ushort)8 : (ushort)0x22);
        var project = Project(wide, Stream(wide, [packet, Event(pitch: 62)], declarations: [], declarationCount: 0), before: before);
        await WithProject(project, (_, report) =>
        {
            Assert.Contains(report.ArchiveInventories!, inventory => inventory.Partial);
            Assert.False(report.Arrangement!.Partial);
            Assert.Equal(new[] { 61, 62 }, report.Arrangement.MidiNoteCandidates!.Select(note => note.MidiPitch));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedOrMalformedNestedObjectsCannotExposeTheirNoteLikePayloads(bool wide)
    {
        var payload = Event(pitch: 99, attributeType: 1);
        var validObject = Record("CmAttribCollection", 0, payload, wide);
        var wrongLength = validObject.ToArray();
        Number((uint)(payload.Length + 200), wide).CopyTo(wrongLength, wrongLength.Length - payload.Length - (wide ? 8 : 4));
        var packets = new[]
        {
            NestedEvent(Record("OtherCollection", 0, payload, wide)),
            NestedEvent(Record("CmAttribCollection", 1, payload, wide)),
            NestedEvent(wrongLength),
            NestedEvent(Number(0x123456, wide)),
            NestedEvent(validObject, flags: 0x102),
            NestedEvent(validObject, flags: 8)
        };
        foreach (var packet in packets)
            await WithProject(Project(wide, Stream(wide, [Event(), packet, Event()])), (_, report) =>
            {
                Assert.True(report.Arrangement!.Partial);
                Assert.Empty(report.Arrangement.MidiNoteCandidates!);
                Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("no later attribute boundary was inferred"));
                return Task.CompletedTask;
            });
    }

    [Theory]
    [InlineData(false, 0xA0)]
    [InlineData(true, 0xA0)]
    [InlineData(false, 0xC0)]
    [InlineData(true, 0xC0)]
    [InlineData(false, 0xF0)]
    [InlineData(true, 0xF0)]
    [InlineData(false, 0x70)]
    [InlineData(true, 0x70)]
    [InlineData(false, 0x40)]
    [InlineData(true, 0x40)]
    [InlineData(false, 0x30)]
    [InlineData(true, 0x30)]
    public async Task StopsBeforeEventsWithoutSupportedClassEvidenceWithoutResynchronizing(bool wide, int status)
    {
        // Additional classes need their own earlier revision declarations;
        // Score events additionally need a supported MEvent base and null-node grammar.
        var unsupported = Join([(byte)status], Event(), Event(0x80, 900, 30, 44, 100));
        var project = Project(wide, Stream(wide, [Event(0x90, 60, 30, 48, 100), unsupported]));
        await WithProject(project, (_, report) =>
        {
            var arrangement = report.Arrangement!;
            var note = Assert.Single(arrangement.MidiNoteCandidates!);
            Assert.Equal(project.FirstEventOffset, note.FileOffset);
            Assert.Equal(48, note.MidiPitch);
            Assert.True(arrangement.Partial);
            var expected = status == 0x30 ? $"discriminator 0x{status:X2}" : "supported class revision";
            Assert.Contains(arrangement.LimitReasons!, reason => reason.Contains(expected, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        });
    }

    private static byte[] EmptyScore(bool wide, ushort baseRevision)
    {
        var bytes = new byte[1 + 26 + (wide ? 8 : 4) + (baseRevision == 1 ? 4 : 0) + 10];
        bytes[0] = 0x30;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(1), 0x8001);
        // The score's opaque scalar bits must not be treated as note fields.
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(3), 0x7FF8000000001234);
        bytes[^1] = 0xAB;
        return bytes;
    }

    private static byte[] ScoreAttributes(bool wide, params byte[][] attributes)
    {
        var empty = EmptyScore(wide, 1);
        var prefix = empty[..^10];
        BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(27 + (wide ? 8 : 4)), (uint)attributes.Length);
        return Join(prefix, Join(attributes), empty[^10..]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ScoreInheritedAttributesShareFramingWithoutSharingMidiBaseFields(bool wide, bool stringValue)
    {
        var scalar = Event(attributeType: 4)[18..32];
        var text = Join(Number(0x74657874), [0, 8], Record("CmString", 0, Event(pitch: 99), wide));
        var score = ScoreAttributes(wide, stringValue ? [text, scalar] : [scalar]);
        var before = Join(Record("MEvent", 1, [], wide), Record("MScoreEvent", 1, [], wide));
        var first = Event(pitch: 48);
        var project = Project(wide, Stream(wide, [first, score, Event(pitch: 62)]), before: before);
        await WithProject(project, (_, report) =>
        {
            Assert.False(report.Arrangement!.Partial);
            Assert.Equal(new[] { 48, 62 }, report.Arrangement.MidiNoteCandidates!.Select(note => note.MidiPitch));
            Assert.Equal(project.FirstEventOffset + first.Length + score.Length,
                report.Arrangement.MidiNoteCandidates![1].FileOffset);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedScoreAttributeReportsItsIdAndDoesNotBorrowALaterBoundary(bool wide)
    {
        var value = Join(Number(0x74657874), [0, 0x28], Record("CmString", 0, Event(pitch: 99), wide));
        var score = ScoreAttributes(wide, value);
        var before = Join(Record("MEvent", 1, [], wide), Record("MScoreEvent", 1, [], wide));
        var project = Project(wide, Stream(wide, [Event(pitch: 48), score, Event(pitch: 62)]), before: before);
        await WithProject(project, (_, report) =>
        {
            Assert.True(report.Arrangement!.Partial);
            Assert.Equal(48, Assert.Single(report.Arrangement.MidiNoteCandidates!).MidiPitch);
            Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("Attribute ID 0x74657874, flags 0x0028"));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public async Task EmptyScoreUsesItsDistinctBaseAndLeavesFollowingNoteAtTheExactOffset(bool wide, ushort baseRevision)
    {
        var score = EmptyScore(wide, baseRevision);
        var before = Join(Record("MEvent", baseRevision, [], wide), Record("MScoreEvent", 1, [], wide));
        var first = Event(pitch: 48);
        var project = Project(wide, Stream(wide, [first, score, Event(pitch: 62)]), before: before);
        await WithProject(project, async (path, report) =>
        {
            Assert.False(report.Arrangement!.Partial);
            Assert.Equal(new[] { 48, 62 }, report.Arrangement.MidiNoteCandidates!.Select(note => note.MidiPitch));
            Assert.Equal(project.FirstEventOffset + first.Length + score.Length,
                report.Arrangement.MidiNoteCandidates![1].FileOffset);
            var snapshot = (await new CprEvidenceReader().ReadAsync(path)).Value!;
            Assert.Equal(2, snapshot.NativeMidiNoteCandidates!.Count);
            Assert.Empty(snapshot.MidiNotes ?? []);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScoreSkipsExactMDataNodeObjectOpaquelyAndKeepsFollowingNoteBoundary(bool wide)
    {
        var empty = EmptyScore(wide, 1);
        var node = Record("MDataNode", 0, [0x90, 0x01, 0x02, 0x03], wide);
        var score = Join(empty[..27], node, empty[^14..]);
        var first = Event(pitch: 48);
        var before = Join(Record("MEvent", 1, [], wide), Record("MScoreEvent", 1, [], wide));
        var project = Project(wide, Stream(wide, [first, score, Event(pitch: 62)]), before: before);
        await WithProject(project, (_, report) =>
        {
            Assert.False(report.Arrangement!.Partial);
            Assert.Equal(new[] { 48, 62 }, report.Arrangement.MidiNoteCandidates!.Select(note => note.MidiPitch));
            Assert.Equal(project.FirstEventOffset + first.Length + score.Length,
                report.Arrangement.MidiNoteCandidates![1].FileOffset);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScoreNeedsEarlierUnambiguousSupportedScoreAndEventRevisions(bool wide)
    {
        var score = EmptyScore(wide, 1);
        var valid = Join(Record("MEvent", 1, [], wide), Record("MScoreEvent", 1, [], wide));
        var contexts = new[]
        {
            Array.Empty<byte>(), Record("MScoreEvent", 1, [], wide), Record("MEvent", 1, [], wide),
            Join(Record("MEvent", 2, [], wide), Record("MScoreEvent", 1, [], wide)),
            Join(Record("MEvent", 1, [], wide), Record("MScoreEvent", 0, [], wide)),
            Join(Record("MEvent", 1, [], wide), Record("MScoreEvent", 2, [], wide)),
            Join(valid, Record("MEvent", 0, [], wide)),
            Join(valid, Record("MScoreEvent", 0, [], wide))
        };
        foreach (var context in contexts)
        {
            var project = Project(wide, Stream(wide, [Event(pitch: 48), score, Event(pitch: 62)]),
                before: context, after: valid);
            await WithProject(project, (_, report) =>
            {
                Assert.True(report.Arrangement!.Partial);
                Assert.Equal(48, Assert.Single(report.Arrangement.MidiNoteCandidates!).MidiPitch);
                Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("score discriminator 0x30"));
                return Task.CompletedTask;
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScoreObjectReferencesAttributesAndTruncationCannotResynchronizeToLaterNotes(bool wide)
    {
        var width = wide ? 8 : 4;
        var nonNull = EmptyScore(wide, 1);
        Number(1, wide).CopyTo(nonNull, 27);
        var attributes = EmptyScore(wide, 1);
        BinaryPrimitives.WriteUInt32BigEndian(attributes.AsSpan(27 + width), 1);
        var before = Join(Record("MEvent", 1, [], wide), Record("MScoreEvent", 1, [], wide));
        foreach (var score in new[] { nonNull, attributes, EmptyScore(wide, 1)[..^12] })
        {
            var project = Project(wide, Stream(wide, [Event(pitch: 48), score], count: 3), before: before);
            await WithProject(project, (_, report) =>
            {
                Assert.True(report.Arrangement!.Partial);
                Assert.Equal(48, Assert.Single(report.Arrangement.MidiNoteCandidates!).MidiPitch);
                Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("score discriminator 0x30"));
                return Task.CompletedTask;
            });
        }
    }

    [Theory]
    [InlineData(false, 0xA0, "MMidiPolyPressure")]
    [InlineData(true, 0xA0, "MMidiPolyPressure")]
    [InlineData(false, 0xC0, "MMidiProgramChange")]
    [InlineData(true, 0xC0, "MMidiProgramChange")]
    [InlineData(false, 0xF0, "MMidiSysex")]
    [InlineData(true, 0xF0, "MMidiSysex")]
    [InlineData(false, 0x70, "MChordEvent")]
    [InlineData(true, 0x70, "MChordEvent")]
    [InlineData(false, 0x40, "MNoteChangeEvent")]
    [InlineData(true, 0x40, "MNoteChangeEvent")]
    public async Task ReadsNotesAfterAdditionalDeclaredEventBodiesAtExactBoundaries(bool wide, int status, string className)
    {
        foreach (var attributeType in new ushort[] { 0, 1, 4 })
        {
            var basePacket = Event((byte)status, attributeType: attributeType)[..^25];
            // Payloads deliberately contain note-like bytes. They must remain
            // opaque; only the subsequent counted note is returned.
            var payload = Join(Event(attributeType: 1), Event(0x80, attributeType: 4));
            var packet = status switch
            {
                0x70 => Join(Event((byte)status, attributeType: attributeType), [0xAB]),
                0x40 => Join(basePacket, new byte[14]),
                0xF0 => Join(basePacket, Number((uint)payload.Length), Number((uint)payload.Length), payload),
                _ => basePacket
            };
            var declarations = Join(Declarations(wide), Record(className, 0, [], wide));
            var first = Event(0x90, 60, 30, 48, 100);
            var project = Project(wide, Stream(wide, [first, packet, Event(0x80, 480, 120, 49, 101)],
                declarations: declarations, declarationCount: 2));
            await WithProject(project, (_, report) =>
            {
                var arrangement = report.Arrangement!;
                var notes = arrangement.MidiNoteCandidates!;
                Assert.Equal(2, notes.Count);
                Assert.Equal(project.FirstEventOffset + first.Length + packet.Length, notes[1].FileOffset);
                Assert.Equal(49, notes[1].MidiPitch);
                Assert.False(arrangement.Partial);
                return Task.CompletedTask;
            });
        }
    }

    [Theory]
    [InlineData(false, 0xA0, "MMidiPolyPressure")]
    [InlineData(true, 0xA0, "MMidiPolyPressure")]
    [InlineData(false, 0xC0, "MMidiProgramChange")]
    [InlineData(true, 0xC0, "MMidiProgramChange")]
    [InlineData(false, 0xF0, "MMidiSysex")]
    [InlineData(true, 0xF0, "MMidiSysex")]
    [InlineData(false, 0x70, "MChordEvent")]
    [InlineData(true, 0x70, "MChordEvent")]
    [InlineData(false, 0x40, "MNoteChangeEvent")]
    [InlineData(true, 0x40, "MNoteChangeEvent")]
    public async Task AdditionalEventRevisionsMustBeEarlierSupportedAndUnambiguous(bool wide, int status, string className)
    {
        var stream = Stream(wide, [Event(), [(byte)status], Event()]);
        var projects = new[]
        {
            Project(wide, stream, after: Record(className, 0, [], wide)),
            Project(wide, stream, before: Record(className, 1, [], wide)),
            Project(wide, stream, before: Join(Record(className, 0, [], wide), Record(className, 1, [], wide)))
        };
        foreach (var project in projects)
            await WithProject(project, (_, report) =>
            {
                var arrangement = report.Arrangement!;
                Assert.Single(arrangement.MidiNoteCandidates!);
                Assert.True(arrangement.Partial);
                Assert.Contains(arrangement.LimitReasons!, reason => reason.Contains("supported class revision", StringComparison.Ordinal));
                return Task.CompletedTask;
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SysExLengthsStayBoundedAndOpaque(bool wide)
    {
        var basePacket = Event(0xF0)[..^25];
        var declarations = Join(Declarations(wide), Record("MMidiSysex", 0, [], wide));
        var validEmpty = Project(wide, Stream(wide,
            [Event(), Join(basePacket, Number(0), Number(0)), Event(0x80, 480, 120, 49, 101)],
            declarations: declarations, declarationCount: 2));
        await WithProject(validEmpty, (_, report) =>
        {
            Assert.Equal(2, report.Arrangement!.MidiNoteCandidates!.Count);
            Assert.False(report.Arrangement.Partial);
            return Task.CompletedTask;
        });

        var packets = new[]
        {
            Join(basePacket, Number(44), Number(0), Event()),
            Join(basePacket, Number(uint.MaxValue), Number(uint.MaxValue), Event()),
            Join(basePacket, Number(0)),
            Join(basePacket, Number(4 * 1024 * 1024), Number(4 * 1024 * 1024), new byte[4 * 1024 * 1024])
        };
        foreach (var packet in packets)
        {
            var project = Project(wide, Stream(wide, [Event(), packet],
                declarations: declarations, declarationCount: 2));
            await WithProject(project, (_, report) =>
            {
                var arrangement = report.Arrangement!;
                Assert.Single(arrangement.MidiNoteCandidates!);
                Assert.True(arrangement.Partial);
                Assert.Contains(arrangement.LimitReasons!, reason => reason.Contains("SysEx", StringComparison.Ordinal));
                return Task.CompletedTask;
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedCountedListCannotFallBackIntoPreviouslySkippedSysExPayload(bool wide)
    {
        var payload = Event(0x90, 900, 30, 99, 127, attributeType: 1);
        var packet = Join(Event(0xF0)[..^25], Number((uint)payload.Length), Number((uint)payload.Length), payload);
        var declarations = Join(Declarations(wide), Record("MMidiSysex", 0, [], wide));
        var stream = Stream(wide, [Event(), packet], declarations: declarations, declarationCount: 2);
        var malformed = new[]
        {
            stream with { Bytes = Join(stream.Bytes, [0x44]) },
            Stream(wide, [Event(), packet], count: 3, declarations: declarations, declarationCount: 2)
        };
        foreach (var value in malformed)
            await WithProject(Project(wide, value), (_, report) =>
            {
                var arrangement = report.Arrangement!;
                Assert.Empty(arrangement.MidiNoteCandidates!);
                Assert.True(arrangement.Partial);
                Assert.Contains(arrangement.LimitReasons!, reason => reason.Contains("candidate prefix was rejected", StringComparison.Ordinal));
                return Task.CompletedTask;
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiresSupportedNamedRevisionsAvailableBeforeEvents(bool wide)
    {
        var note = Event();
        var cases = new[]
        {
            Project(wide, Stream(wide, [note], declarations: Declarations(wide, baseRevision: null))),
            Project(wide, Stream(wide, [note], declarations: Declarations(wide, noteRevision: null))),
            Project(wide, Stream(wide, [note], declarations: Declarations(wide, baseRevision: 1))),
            Project(wide, Stream(wide, [note], declarations: Declarations(wide, baseRevision: 3))),
            Project(wide, Stream(wide, [note], declarations: Declarations(wide, noteRevision: 0))),
            Project(wide, Stream(wide, [note], declarations: Declarations(wide, noteRevision: 2))),
            Project(wide, Stream(wide, [note], declarations: Declarations(wide, baseRevision: null)),
                after: Record("MMidiEvent", 2, [], wide)),
            Project(wide, Stream(wide, [note], declarations: Declarations(wide, noteRevision: null)),
                after: Record("MMidiNote", 1, [], wide)),
            Project(wide, Stream(wide, [note]), before: Record("MMidiEvent", 1, [], wide)),
            Project(wide, Stream(wide, [note]), after: Record("MMidiNote", 0, [], wide)),
            Project(wide, Stream(wide, [note]), partRevision: 0),
            Project(wide, Stream(wide, [note]), partRevision: 3)
        };
        foreach (var project in cases)
            await WithProject(project, (_, report) =>
            {
                Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
                return Task.CompletedTask;
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyCountEndsWithoutADeclarationCountByte(bool wide)
    {
        var project = Project(wide, Stream(wide, []));
        await WithProject(project, (path, report) =>
        {
            Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
            Assert.False(report.Arrangement.Partial);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkipsDeclaredScalarEventsAtExactBoundariesInMixedStreams(bool wide)
    {
        var table = Join(Declarations(wide), Record("MMidiController", 0, [], wide),
            Record("MMidiAfterTouch", 0, [], wide), Record("MMidiPitchBend", 0, [], wide));
        var scalar = new[] { Event(0xB0)[..18], Event(0xD0)[..18], Event(0xE0)[..18] };
        var project = Project(wide, Stream(wide,
            [scalar[0], Event(), scalar[1], scalar[2], Event(0x80, 480, 120, 60, 100)],
            declarations: table, declarationCount: 4));
        await WithProject(project, (_, report) =>
        {
            var notes = report.Arrangement!.MidiNoteCandidates!;
            Assert.Equal(2, notes.Count);
            Assert.Equal(new long[] { project.FirstEventOffset + 18, project.FirstEventOffset + 18 + 43 + 36 },
                notes.Select(note => note.FileOffset));
            Assert.All(notes, note => Assert.EndsWith("MMidiPart/counted-events", note.SourceLayout));
            Assert.Equal(60, notes[1].MidiPitch);
            Assert.Equal(480, notes[1].PositionTicks);
            Assert.False(report.Arrangement.Partial);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScalarEventNeedsItsOwnSupportedEarlierDeclaration(bool wide)
    {
        var events = new[] { Event(0xB0)[..18], Event() };
        var cases = new[]
        {
            Project(wide, Stream(wide, events)),
            Project(wide, Stream(wide, events), after: Record("MMidiController", 0, [], wide)),
            Project(wide, Stream(wide, events), before: Record("MMidiController", 1, [], wide)),
            Project(wide, Stream(wide, events), before: Join(Record("MMidiController", 0, [], wide),
                Record("MMidiController", 1, [], wide)))
        };
        foreach (var project in cases)
            await WithProject(project, (_, report) =>
            {
                Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
                return Task.CompletedTask;
            });
    }

    [Theory]
    [InlineData(false, 0xA0, "MMidiPolyPressure", 0)]
    [InlineData(true, 0xA0, "MMidiPolyPressure", 1)]
    [InlineData(false, 0xC0, "MMidiProgramChange", 4)]
    [InlineData(true, 0xC0, "MMidiProgramChange", 0)]
    public async Task ReadsNotesBeyondAdditionalScalarEventsWithoutInterpretingTheirValues(
        bool wide, byte status, string className, ushort attributeType)
    {
        var scalar = ScalarEvent(status, attributeType);
        var project = Project(wide, Stream(wide, [Event(), scalar, Event(pitch: 72)],
            declarations: Join(Declarations(wide), Record(className, 0, [], wide)), declarationCount: 2));
        await WithProject(project, (_, report) =>
        {
            var notes = report.Arrangement!.MidiNoteCandidates!;
            Assert.Equal(new long[] { project.FirstEventOffset, project.FirstEventOffset + 43 + scalar.Length },
                notes.Select(note => note.FileOffset));
            Assert.Equal(72, notes[1].MidiPitch);
            Assert.All(notes, note => Assert.EndsWith("MMidiPart/counted-events", note.SourceLayout));
            Assert.False(report.Arrangement.Partial);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false, 0xA0, "MMidiPolyPressure")]
    [InlineData(true, 0xA0, "MMidiPolyPressure")]
    [InlineData(false, 0xC0, "MMidiProgramChange")]
    [InlineData(true, 0xC0, "MMidiProgramChange")]
    [InlineData(false, 0xF0, "MMidiSysex")]
    [InlineData(true, 0xF0, "MMidiSysex")]
    public async Task AdditionalEventsRequireUnambiguousEarlierRevisionZero(
        bool wide, byte status, string className)
    {
        var other = status == 0xF0 ? SysexEvent([]) : ScalarEvent(status);
        var events = new[] { other, Event() };
        var cases = new[]
        {
            Project(wide, Stream(wide, events)),
            Project(wide, Stream(wide, events), after: Record(className, 0, [], wide)),
            Project(wide, Stream(wide, events), before: Record(className, 1, [], wide)),
            Project(wide, Stream(wide, events), before: Join(Record(className, 0, [], wide),
                Record(className, 1, [], wide)))
        };
        foreach (var project in cases)
            await WithProject(project, (_, report) =>
            {
                Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
                return Task.CompletedTask;
            });
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 4)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    public async Task SkipsSysexPayloadExactlyIncludingEmbeddedNotePatterns(bool wide, bool empty, ushort attributeType)
    {
        var payload = empty ? [] : Join(Event(), Event(attributeType: 1));
        // The second word is not used as a length or required to equal the first.
        var sysex = SysexEvent(payload, secondWord: uint.MaxValue, attributeType: attributeType);
        var project = Project(wide, Stream(wide, [Event(), sysex, Event(pitch: 73)],
            declarations: Join(Declarations(wide), Record("MMidiSysex", 0, [], wide)), declarationCount: 2));
        await WithProject(project, async (path, report) =>
        {
            var notes = report.Arrangement!.MidiNoteCandidates!;
            Assert.Equal(new long[] { project.FirstEventOffset, project.FirstEventOffset + 43 + sysex.Length },
                notes.Select(note => note.FileOffset));
            Assert.Equal(73, notes[1].MidiPitch);
            Assert.False(report.Arrangement.Partial);
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Empty(snapshot.Value!.MidiNotes ?? []);
            Assert.Equal(2, snapshot.Value.NativeMidiNoteCandidates!.Count);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsMalformedAdditionalEventsWithoutResynchronizing(bool wide)
    {
        var cases = new[]
        {
            SysexEvent([], -1),
            SysexEvent([], int.MaxValue),
            SysexEvent([], 44), // Would consume the next note and exceed the body.
            ScalarEvent(0xA0, attributeType: 0x28),
            ScalarEvent(0xC0, attributeType: 0x28),
            Join(ScalarEvent(0xF0, attributeType: 0x28), Number(0), Number(0)),
            ScalarEvent(0xA1),
            ScalarEvent(0xC1),
            ScalarEvent(0xF1),
            ScalarEvent(0x40) // Its callback has a suffix; a base-only skip is unsafe.
        };
        var table = Join(Declarations(wide), Record("MMidiPolyPressure", 0, [], wide),
            Record("MMidiProgramChange", 0, [], wide), Record("MMidiSysex", 0, [], wide));
        foreach (var other in cases)
        {
            var project = Project(wide, Stream(wide, [Event(), other, Event()],
                declarations: table, declarationCount: 4));
            await WithProject(project, (_, report) =>
            {
                var arrangement = report.Arrangement!;
                var note = Assert.Single(arrangement.MidiNoteCandidates!);
                Assert.Equal(project.FirstEventOffset, note.FileOffset);
                Assert.EndsWith("MMidiPart/counted-events", note.SourceLayout);
                Assert.True(arrangement.Partial);
                Assert.NotEmpty(arrangement.LimitReasons!);
                return Task.CompletedTask;
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncatedSysexLengthWordsRetainOnlyTheEarlierCompleteNote(bool wide)
    {
        // Keep the truncation at the containing part boundary: following event
        // bytes cannot independently establish where a missing word would end.
        var packets = new[]
        {
            ScalarEvent(0xF0),
            Join(ScalarEvent(0xF0), Number(0))[..^1],
            SysexEvent([])[..^1]
        };
        foreach (var packet in packets)
        {
            var project = Project(wide, Stream(wide, [Event(), packet],
                declarations: Join(Declarations(wide), Record("MMidiSysex", 0, [], wide)), declarationCount: 2));
            await WithProject(project, (_, report) =>
            {
                var note = Assert.Single(report.Arrangement!.MidiNoteCandidates!);
                Assert.Equal(project.FirstEventOffset, note.FileOffset);
                Assert.True(report.Arrangement.Partial);
                Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("SysEx", StringComparison.Ordinal));
                return Task.CompletedTask;
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SysexCrossingByteBudgetRetainsOnlyEarlierNotesAsPartial(bool wide)
    {
        var sysex = SysexEvent(new byte[CprMidiPartNoteScanner.MaximumScanBytes]);
        var project = Project(wide, Stream(wide, [Event(), sysex, Event()],
            declarations: Join(Declarations(wide), Record("MMidiSysex", 0, [], wide)), declarationCount: 2));
        await WithProject(project, (_, report) =>
        {
            var note = Assert.Single(report.Arrangement!.MidiNoteCandidates!);
            Assert.Equal(project.FirstEventOffset, note.FileOffset);
            Assert.EndsWith("MMidiPart/counted-events", note.SourceLayout);
            Assert.True(report.Arrangement.Partial);
            Assert.Contains(report.Arrangement.LimitReasons!, reason => reason.Contains("budget", StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrailingNonNoteEventsDoNotConsumeTheNoteBudget(bool wide)
    {
        var events = Enumerable.Repeat(Event(), 10000).Concat(new[]
        {
            ScalarEvent(0xA0), ScalarEvent(0xC0), SysexEvent(Event())
        }).ToArray();
        var table = Join(Declarations(wide), Record("MMidiPolyPressure", 0, [], wide),
            Record("MMidiProgramChange", 0, [], wide), Record("MMidiSysex", 0, [], wide));
        var project = Project(wide, Stream(wide, events, declarations: table, declarationCount: 4));
        await WithProject(project, (_, report) =>
        {
            Assert.Equal(10000, report.Arrangement!.MidiNoteCandidates!.Count);
            Assert.False(report.Arrangement.Partial);
            Assert.All(report.Arrangement.MidiNoteCandidates, note =>
                Assert.EndsWith("MMidiPart/counted-events", note.SourceLayout));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapedTypedNoteAndExistingReferenceAreNotCountedAgain(bool wide)
    {
        var body = Event(0x90, 60, 30, 48, 100)[1..];
        var typed = Record("MMidiNote", 1, body, wide);
        var escaped = Join([0], typed);
        var reference = Join([0], Number(0, wide));
        var stream = Stream(wide, [escaped, reference, Event(0x80, 120, 30, 49, 101)]);
        var placeholder = Project(wide, stream);
        var typedBodyOffset = placeholder.FirstEventOffset + escaped.Length - body.Length;
        var objectId = (ulong)(typedBodyOffset - (wide ? 8 : 4) - placeholder.ArchiveOffset);
        Number(objectId, wide).CopyTo(stream.Bytes, stream.EventOffset + escaped.Length + 1);
        var project = Project(wide, stream);

        await WithProject(project, async (path, report) =>
        {
            var notes = report.Arrangement!.MidiNoteCandidates!;
            Assert.Equal(2, notes.Count);
            var declared = Assert.Single(notes, note => note.SourceLayout.Contains("MMidiNote/", StringComparison.Ordinal));
            var counted = Assert.Single(notes, note => note.SourceLayout.EndsWith("MMidiPart/counted-events", StringComparison.Ordinal));
            Assert.Equal(typedBodyOffset, declared.FileOffset);
            Assert.Equal(48, declared.MidiPitch);
            Assert.Equal(project.FirstEventOffset + escaped.Length + reference.Length, counted.FileOffset);
            Assert.Equal(49, counted.MidiPitch);
            var snapshot = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
            Assert.Empty(snapshot.Value!.MidiNotes ?? []);
            Assert.Equal(2, snapshot.Value.NativeMidiNoteCandidates!.Count);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapedNullReferencesHaveModeWidthAndUnresolvedReferencesStayOpaque(bool wide)
    {
        var project = Project(wide, Stream(wide, [Join([0], Number(0, wide)), Event()]));
        await WithProject(project, (_, report) =>
        {
            var note = Assert.Single(report.Arrangement!.MidiNoteCandidates!);
            Assert.Equal(project.FirstEventOffset + 1 + (wide ? 8 : 4), note.FileOffset);
            Assert.EndsWith("MMidiPart/counted-events", note.SourceLayout);
            return Task.CompletedTask;
        });
        var unresolved = Project(wide, Stream(wide, [Join([0], Number(1, wide)), Event()]));
        await WithProject(unresolved, (_, report) =>
        {
            Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassOnlyDeclarationCannotEstablishAnExistingObjectReference(bool wide)
    {
        var stream = Stream(wide, [Join([0], Number(0, wide)), Event()]);
        var validNullReference = Project(wide, stream);
        ulong classOnlyId = 0;
        await WithProject(validNullReference, (_, report) =>
        {
            var note = Assert.Single(report.Arrangement!.MidiNoteCandidates!);
            Assert.EndsWith("MMidiPart/counted-events", note.SourceLayout);
            var inventory = Assert.Single(report.ArchiveInventories!);
            var declaration = Assert.Single(inventory.Records,
                record => record.ClassName == "MMidiNote" && record.BodyBytes == 0);
            classOnlyId = (ulong)(declaration.BodyFileOffset - (wide ? 8 : 4) - inventory.PayloadFileOffset);
            Assert.True(classOnlyId > 0);
            Assert.True(inventory.PayloadFileOffset + (long)classOnlyId < validNullReference.FirstEventOffset);
            return Task.CompletedTask;
        });

        // An earlier zero-body declaration has a plausible size-field offset,
        // but the declaration preface did not construct an object at that ID.
        Number(classOnlyId, wide).CopyTo(stream.Bytes, stream.EventOffset + 1);
        var invalidObjectReference = Project(wide, stream);
        await WithProject(invalidObjectReference, (_, report) =>
        {
            Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false, 10000, false)]
    [InlineData(true, 10000, false)]
    [InlineData(false, 10001, true)]
    [InlineData(true, 10001, true)]
    public async Task NoteBudgetDistinguishesExactCardinalityFromIncompleteStream(bool wide, int count, bool partial)
    {
        var events = Enumerable.Repeat(Event(), count).ToArray();
        var project = Project(wide, Stream(wide, events));
        await WithProject(project, (_, report) =>
        {
            var arrangement = report.Arrangement!;
            Assert.Equal(10000, arrangement.MidiNoteCandidates!.Count);
            Assert.Equal(partial, arrangement.Partial);
            Assert.All(arrangement.MidiNoteCandidates, note =>
                Assert.EndsWith("MMidiPart/counted-events", note.SourceLayout));
            Assert.Equal(project.FirstEventOffset + 9999L * 43, arrangement.MidiNoteCandidates[^1].FileOffset);
            if (partial) Assert.Contains(arrangement.LimitReasons!, reason => reason.Contains("limit", StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        });
    }
}
