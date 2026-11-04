using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprNamedRecordScannerTests
{
    private static byte[] U16(ushort value) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, value); return b; }
    private static byte[] U32(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); return b; }
    private static byte[] U64(ulong value) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, value); return b; }
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    private static byte[] Name(string value) { var bytes = Encoding.ASCII.GetBytes(value + "\0"); return Join(U32((uint)bytes.Length), bytes); }
    private static byte[] ScalarString(string key, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0\uFEFF");
        return Join(Name(key), U16(8), U32((uint)bytes.Length), bytes);
    }
    private static byte[] Float(string key, double value) => Join(Name(key), U16(4), U64((ulong)BitConverter.DoubleToInt64Bits(value)));
    private static byte[] Integer(string key, long value) => Join(Name(key), U16(1), U64((ulong)value));
    private static byte[] Object(string key, params byte[][] fields) => Join(Name(key), U16(2), U16(6), U32((uint)fields.Length), Join(fields));
    private static byte[] RecordArray(string key, params byte[][][] items) => Join(Name(key), U16(2), U16(5), U32((uint)items.Length),
        Join(items.Select(item => Join(U32((uint)item.Length), Join(item))).ToArray()));
    private static byte[] Blob(string key, byte[] value) => Join(Name(key), U16(2), U16(7), U32((uint)value.Length), value);
    private static byte[] Instance(string className, params byte[][] fields) =>
        Join(U16(0x14), Name(className), U64(0), U32((uint)fields.Length), Join(fields));
    private static byte[] InstanceArray(string key, params byte[][] items) =>
        Join(Name(key), U16(2), U16(0xC9), U32((uint)items.Length), Join(items));
    private static byte[] Chunk(string id, byte[] payload) => Join(Encoding.ASCII.GetBytes(id), U32((uint)payload.Length), payload);
    private static byte[] ChunkWide(string id, byte[] payload) => Join(Encoding.ASCII.GetBytes(id), U64((ulong)payload.Length), payload);
    private static byte[] Root(string stream, string cls)
    {
        var a = Encoding.ASCII.GetBytes(stream); var b = Encoding.ASCII.GetBytes(cls);
        return Chunk("ROOT", Join(U32((uint)a.Length), a, U32((uint)b.Length), b));
    }
    private static byte[] RootWide(string stream, string cls)
    {
        var a = Encoding.ASCII.GetBytes(stream); var b = Encoding.ASCII.GetBytes(cls);
        return ChunkWide("ROOT", Join(U32((uint)a.Length), a, U32((uint)b.Length), b));
    }

    private static async Task WithNamedArchives(bool wide, byte[][] archives,
        Action<byte[], CprStructureReport, ProjectSnapshot> check)
    {
        var chunks = Join(archives.Select(archive => wide
            ? Join(RootWide("Mixer", "PMixer"), ChunkWide("ARCH", archive))
            : Join(Root("Mixer", "PMixer"), Chunk("ARCH", archive))).ToArray());
        var bytes = Join(Encoding.ASCII.GetBytes(wide ? "RIF2" : "RIFF"),
            wide ? U64((uint)chunks.Length) : U32((uint)chunks.Length), "NUND"u8.ToArray(), chunks);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var structure = await new CprStructureInspector().InspectAsync(path);
            var project = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(structure.IsSuccess, structure.Error?.Message);
            Assert.True(project.IsSuccess, project.Error?.Message);
            check(bytes, structure.Value!, project.Value!);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsNestedBusSelectionsAndRetainsRawRoutingControls(bool wide)
    {
        var records = Join(Object("Name", ScalarString("String", "Audio")),
            Object("InputBusValue", Integer("Value", 41), Integer("AnchorValue", 999)),
            Object("OutputBusValue", Integer("Value", 42)),
            Object("outputBusEnable", Integer("Value", 0)),
            Object("foldbackSendFolder", RecordArray("Slot", [Object("Output", Integer("Value", 91))])),
            ScalarString("Name", "Input"), Integer("Bus UID", 41), Integer("Bus Type", 1),
            ScalarString("Name", "Output"), Integer("Bus UID", 42), Integer("Bus Type", 3));
        await WithNamedArchives(wide, [records], (bytes, report, project) =>
        {
            var mixer = Assert.Single(project.NativeMixerObservations!, item => item.Name == "Audio");
            Assert.Contains(mixer.CandidateFields!, field => field.Path == "InputBusValue/Value" && field.Value == "41");
            Assert.Contains(mixer.CandidateFields!, field => field.Path == "outputBusEnable/Value" && field.Value == "0");
            Assert.Contains(mixer.CandidateFields!, field => field.Path == "foldbackSendFolder/Slot[0]/Output/Value");
            var archive = Assert.Single(report.Chunks, chunk => chunk.Id == "ARCH");
            Assert.Equal(archive.Offset + (wide ? 12 : 8), mixer.SourceArchiveOffset);
            Assert.All(report.NamedRecordCandidates!, item => Assert.Equal(mixer.SourceArchiveOffset, item.SourceArchiveOffset));
            var view = CprSignalFlowPresentation.Create(project);
            Assert.Equal(2, view.Routing.Count);
            var input = Assert.Single(view.Routing, edge => edge.Kind == "Input");
            var output = Assert.Single(view.Routing, edge => edge.Kind == "Output");
            Assert.Equal("Input", view.Tracks.Single(track => track.Id == input.FromChannelId).Name);
            Assert.Equal("Output", view.Tracks.Single(track => track.Id == output.ToChannelId).Name);
            Assert.EndsWith("/InputBusValue/Value", input.SourcePath);
            Assert.EndsWith("/OutputBusValue/Value", output.SourcePath);
            Assert.Null(output.Enabled); // A raw control value does not establish enabled semantics.
            Assert.Equal("Value", Encoding.ASCII.GetString(bytes.AsSpan((int)output.SourceOffset! + 4, 5)));
            Assert.All(view.Routing, edge => Assert.Equal(EvidenceLevel.Candidate, edge.Evidence));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedStreamNamesCannotResolveToAnotherArchive(bool wide)
    {
        var source = Join(Object("Name", ScalarString("String", "Audio")), Object("OutputBusValue", Integer("Value", 42)));
        var target = Join(ScalarString("Name", "Other archive bus"), Integer("Bus UID", 42), Integer("Bus Type", 3));
        await WithNamedArchives(wide, [source, target], (_, report, project) =>
        {
            Assert.Equal(2, project.NativeMixerObservations!.Select(item => item.SourceArchiveOffset).Distinct().Count());
            Assert.Single(project.NativeMixerObservations!.Select(item => item.SourceStream).Distinct());
            var route = Assert.Single(CprSignalFlowPresentation.Create(project).Routing);
            Assert.StartsWith("Bus UID 42", route.ToChannelId);
            Assert.Equal(2, report.NamedRecordCandidates!.Count);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassInstanceArraysKeepSlotIndicesIncludingNullEntries(bool wide)
    {
        var slots = InstanceArray("Slot", U16(0),
            Instance("SendSlot", Object("Output", Integer("Value", 42))),
            Instance("SendSlot", Object("Output", Integer("Value", 91))));
        var records = Join(Object("Name", ScalarString("String", "Audio")), Object("SendFolder", slots));
        await WithNamedArchives(wide, [records], (bytes, report, project) =>
        {
            var values = Assert.Single(report.NamedRecordCandidates!).Values;
            Assert.Contains(values, field => field.Path == "SendFolder/Slot[1]/Output/Value" && field.Value == "42");
            Assert.Contains(values, field => field.Path == "SendFolder/Slot[2]/Output/Value" && field.Value == "91");
            var classField = Assert.Single(values, field => field.Path == "SendFolder/Slot[1]/class");
            Assert.Equal("SendSlot", classField.Value);
            Assert.Equal(9U, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)classField.ArchiveOffset)));
            var view = CprSignalFlowPresentation.Create(project);
            Assert.Equal(2, view.Routing.Count);
            Assert.Contains(view.Routing, edge => edge.SourcePath!.EndsWith("Slot[1]/Output/Value"));
            Assert.Contains(view.Routing, edge => edge.SourcePath!.EndsWith("Slot[2]/Output/Value"));
            Assert.DoesNotContain(values, field => field.Path.Contains("Slot[0]"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArraySourceOffsetsPointToTheStoredValueStart(bool wide)
    {
        var text = Encoding.UTF8.GetBytes("O|Driver|Port\0");
        var records = Join(Object("Name", ScalarString("String", "Bus")),
            Object("OwnInputBus", Join(Name("Connections"), U16(2), U16(4), U32(1), U32((uint)text.Length), text)),
            Join(Name("Ids"), U16(2), U16(2), U32(1), U64(42)));
        await WithNamedArchives(wide, [records], (bytes, report, _) =>
        {
            var values = Assert.Single(report.NamedRecordCandidates!).Values;
            var connection = Assert.Single(values, field => field.Path == "OwnInputBus/Connections[0]");
            Assert.Equal((uint)text.Length, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)connection.ArchiveOffset)));
            var integer = Assert.Single(values, field => field.Path == "Ids[0]");
            Assert.Equal(42UL, BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan((int)integer.ArchiveOffset)));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteInstanceArraysCannotPublishPartialDestinations(bool wide)
    {
        var slots = InstanceArray("Slot", Instance("SendSlot", Object("Output", Integer("Value", 42))),
            Instance("SendSlot", Object("Output", Integer("Value", 91))));
        var records = Join(Object("Name", ScalarString("String", "Audio")), Object("SendFolder", slots[..^1]));
        await WithNamedArchives(wide, [records], (_, _, project) =>
        {
            Assert.Empty(CprSignalFlowPresentation.Create(project).Routing);
        });
    }

    [Theory]
    [InlineData(false, "SendFolder")]
    [InlineData(true, "SendFolder")]
    [InlineData(false, "foldbackSendFolder")]
    [InlineData(true, "foldbackSendFolder")]
    public async Task LargePannerBlocksCannotCrowdOutDestinationsOrInsertNames(bool wide, string folderName)
    {
        var folder = Object(folderName, Object("Panner",
                Enumerable.Range(0, 600).Select(index => Integer("Field" + index, index)).ToArray()),
            RecordArray("Slot", [Object("Output", Integer("Value", 91))]));
        var records = Join(Object("Name", ScalarString("String", "Audio")), folder,
            Object("InsertFolder", RecordArray("Slot", [ScalarString("Plugin Name", "Stored effect")])),
            Object("OutputBusValue", Integer("Value", 42)));
        await WithNamedArchives(wide, [records], (_, _, project) =>
        {
            var fields = Assert.Single(project.NativeMixerObservations!).CandidateFields!;
            Assert.Equal(512, fields.Count);
            Assert.Contains(fields, field => field.Path == folderName + "/Slot[0]/Output/Value" && field.Value == "91");
            Assert.Contains(fields, field => field.Path == "InsertFolder/Slot[0]/Plugin Name" && field.Value == "Stored effect");
            Assert.Contains(fields, field => field.Path == "OutputBusValue/Value" && field.Value == "42");
        });
    }

    [Fact]
    public async Task ReadsCubase12NamedRecordShapeAsCandidateValues()
    {
        var name = Object("Name", ScalarString("String", "Kick"));
        var volume = Object("Volume", Float("AnchorValue", -6.5), Float("Value", 12800));
        var eq = Object("EQ", RecordArray("Band", [Integer("Enable", 1), Float("Gain", 2.0)]), Blob("panner", [1, 2, 3, 4]));
        var records = Join(name, volume, eq, Integer("ChannelType", 3), Integer("Mute", 1), Integer("Solo", 0),
            Object("OwnInputBus", Integer("Bus UID", 42)), Object("OutputBus", Integer("Value", 91)),
            Object("SendFolder", RecordArray("Slot", [Object("Output", Integer("Value", 92))])),
            Integer("Bus UID", 43), Integer("Bus Type", 3),
            ScalarString("Input Arrangement", "stereo"), ScalarString("Input Device Name", "Interface"),
            Integer("Input Channel ID", 2), ScalarString("Input Port Name", "Mic/Line 2"));
        var body = Join(Root("Mixer", "PMixer"), Chunk("ARCH", records));
        var fileBytes = Join(Encoding.ASCII.GetBytes("RIFF"), U32((uint)body.Length), Encoding.ASCII.GetBytes("NUND"), body);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, fileBytes);
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var candidate = Assert.Single(result.Value!.NamedRecordCandidates!);
            Assert.Contains(candidate.Values, value => value.Path == "Name/String" && value.Value == "Kick");
            Assert.Contains(candidate.Values, value => value.Path == "Volume/AnchorValue" && value.Value == "-6.5");
            Assert.Contains(candidate.Values, value => value.Path == "Volume/Value" && value.Type == "f64");
            Assert.Contains(candidate.Values, value => value.Path == "EQ/Band[0]/Enable" && value.Value == "1");
            Assert.Contains(candidate.Values, value => value.Path == "EQ/Band[0]/Gain" && value.Value == "2");
            Assert.Contains(candidate.Values, value => value.Path == "EQ/panner" && value.Value.Contains("opaque"));
            Assert.Contains("not independently verified", candidate.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Cubase 12 named-record candidates", CprStructureInspector.ToMarkdown(result.Value));

            var project = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(project.IsSuccess, project.Error?.Message);
            Assert.Null(project.Value!.Tracks.SingleOrDefault(track => track.Name == "Kick")?.Volume);
            Assert.Contains(project.Value.NativeMetadataCandidates!, item =>
                item.Field == "Mixer volume" && item.Value == "Kick: -6.5 dB");
            Assert.Contains(project.Value.NativeMetadataCandidates!, item =>
                item.Field == "Mute state" && item.Value == "Kick: On");
            Assert.Contains(project.Value.NativeMetadataCandidates!, item =>
                item.Field == "Solo state" && item.Value == "Kick: Off");
            var mixer = Assert.Single(project.Value.NativeMixerObservations!);
            Assert.Equal("Kick", mixer.Name);
            Assert.Equal(-6.5, mixer.VolumeDb);
            Assert.Null(mixer.Pan);
            Assert.True(mixer.Mute);
            Assert.False(mixer.Solo);
            Assert.Contains("Unverified", mixer.Evidence);
            Assert.Contains(mixer.CandidateFields!, field => field.Path == "OwnInputBus/Bus UID" && field.Value == "42");
            Assert.Contains(mixer.CandidateFields!, field => field.Path == "OutputBus/Value" && field.Value == "91");
            Assert.Contains(mixer.CandidateFields!, field => field.Path == "Bus UID" && field.Value == "43");
            Assert.Contains(mixer.CandidateFields!, field => field.Path == "Input Device Name" && field.Value == "Interface");
            Assert.Contains(mixer.CandidateFields!, field => field.Path == "Input Port Name" && field.Value == "Mic/Line 2");
            var routing = CprSignalFlowPresentation.Create(project.Value);
            Assert.Equal(2, routing.Routing.Count);
            Assert.Contains(routing.Routing, edge => edge.Kind == "Send" && edge.ToChannelId.StartsWith("Bus UID 92"));
            Assert.All(routing.Routing, edge => Assert.True(edge.SourceOffset > 0));
            var coverage = ProjectFieldCoverage.Evaluate(project.Value);
            Assert.Equal(ProjectFieldState.Candidate, coverage["Mixer volume"]);
            Assert.Equal(ProjectFieldState.Candidate, coverage["Mute / solo"]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ReadsBusFieldsAfterLargeStripsWithoutMergingTheNextNamedRecord()
    {
        var records = Join(Object("Name", ScalarString("String", "Audio")),
            Object("StripFolder", Enumerable.Range(0, 160).Select(index => Integer("Field" + index, index)).ToArray()),
            Object("OutputBus", Integer("Value", 42)),
            Object("Name", ScalarString("String", "Group")), Object("OwnInputBus", Integer("Bus UID", 42)));
        var body = Join(Root("Mixer", "PMixer"), Chunk("ARCH", records));
        var bytes = Join(Encoding.ASCII.GetBytes("RIFF"), U32((uint)body.Length), Encoding.ASCII.GetBytes("NUND"), body);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var result = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(2, result.Value!.NativeMixerObservations!.Count);
            var view = CprSignalFlowPresentation.Create(result.Value);
            var connection = Assert.Single(view.Routing);
            Assert.Equal("Audio", view.Tracks.Single(track => track.ChannelId == connection.FromChannelId).Name);
            Assert.Equal("Group", view.Tracks.Single(track => track.ChannelId == connection.ToChannelId).Name);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ReadsNativeRoutingFieldsFromWideRif2ArrangementArchives()
    {
        var mixer = Join(Object("Name", ScalarString("String", "Wide Audio")),
            Object("OutputBus", Integer("Value", 42)),
            Object("SendFolder", RecordArray("Slot", [Object("Output", Integer("Value", 43))])),
            Object("directRoutingFolder", RecordArray("Slot", [Object("Output", Integer("Value", 44))])),
            Object("RecorderBus", Integer("Bus UID", 45)));
        var body = Join(RootWide("Arrangement1", "PArrangement"), ChunkWide("ARCH", mixer));
        var fileBytes = Join(Encoding.ASCII.GetBytes("RIF2"), U64((ulong)body.Length), Encoding.ASCII.GetBytes("NUND"), body);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, fileBytes);
            var project = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(project.IsSuccess, project.Error?.Message);
            var mixerRecord = Assert.Single(project.Value!.NativeMixerObservations!);
            Assert.Contains(mixerRecord.CandidateFields!, field => field.Path == "OutputBus/Value");
            Assert.Contains(mixerRecord.CandidateFields!, field => field.Path.StartsWith("directRoutingFolder/", StringComparison.Ordinal));
            var flow = CprSignalFlowPresentation.Create(project.Value);
            Assert.Contains(flow.Routing, edge => edge.Kind == "Direct output");
            Assert.Contains(flow.Routing, edge => edge.Kind == "Send");
            Assert.Contains(flow.Routing, edge => edge.Kind == "Record");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task FindsBusDefinitionsUsingCubasesScalarNameAndParentUidFields()
    {
        var bus = Join(ScalarString("Name", "Stereo Out"), Integer("Bus UID", 42), Integer("Bus Type", 3),
            ScalarString("Input Arrangement", "stereo"));
        var parent = Join(ScalarString("Name", "Control Room"), Integer("Bus UID", 91),
            Integer("Parent Bus UID", 42), Integer("Bus Type", 5));
        var body = Join(Root("Mixer", "PMixer"), Chunk("ARCH", Join(bus, parent)));
        var bytes = Join(Encoding.ASCII.GetBytes("RIFF"), U32((uint)body.Length), Encoding.ASCII.GetBytes("NUND"), body);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var project = await new CprEvidenceReader().ReadAsync(path);
            Assert.True(project.IsSuccess, project.Error?.Message);
            var observations = project.Value!.NativeMixerObservations!;
            Assert.Contains(observations, observation => observation.Name == "Stereo Out" &&
                observation.CandidateFields!.Any(field => field.Path == "Bus UID" && field.Value == "42"));
            Assert.Contains(observations, observation => observation.Name == "Control Room" &&
                observation.CandidateFields!.Any(field => field.Path == "Parent Bus UID" && field.Value == "42"));
            var graph = CprSignalFlowPresentation.Create(project.Value);
            Assert.Contains(graph.Tracks, track => track.Name == "Stereo Out" && track.Kind == "Bus");
            Assert.Contains(graph.Routing, edge => edge.Kind == "Parent bus" &&
                edge.ToChannelId == graph.Tracks.Single(track => track.Name == "Stereo Out").ChannelId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task IgnoresAnchorLikeBytesWithInvalidRecordLengths()
    {
        var falseAnchor = new byte[] { 0, 0, 0, 5, (byte)'N', (byte)'a', (byte)'m', (byte)'e', 0,
            0, 2, 0, 6, 0, 0, 0, 1, 0, 0, 0, 7, (byte)'S', (byte)'t', (byte)'r', (byte)'i', (byte)'n', (byte)'g', 0, 0, 8,
            0xff, 0xff, 0xff, 0xff };
        var body = Join(Root("Mixer", "PMixer"), Chunk("ARCH", falseAnchor));
        var fileBytes = Join(Encoding.ASCII.GetBytes("RIFF"), U32((uint)body.Length), Encoding.ASCII.GetBytes("NUND"), body);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, fileBytes);
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Empty(result.Value!.NamedRecordCandidates!);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
