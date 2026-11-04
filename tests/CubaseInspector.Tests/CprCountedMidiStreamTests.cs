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
            Stream(wide, [good, Event(0x91)]),
            Stream(wide, [Event(attributeType: 8)]),
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
            ScalarEvent(0xF0), // Both length words missing.
            Join(ScalarEvent(0xF0), Number(0))[..^1],
            SysexEvent([])[..^1], // Second word truncated, even for zero payload.
            ScalarEvent(0xA0, attributeType: 8),
            ScalarEvent(0xC0, attributeType: 8),
            Join(ScalarEvent(0xF0, attributeType: 8), Number(0), Number(0)),
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
                Assert.Empty(report.Arrangement!.MidiNoteCandidates!);
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
            if (partial) Assert.Contains(arrangement.LimitReasons!, reason => reason.Contains("budget", StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        });
    }
}
