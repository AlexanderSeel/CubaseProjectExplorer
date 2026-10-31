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
    private static byte[] Chunk(string id, byte[] payload) => Join(Encoding.ASCII.GetBytes(id), U32((uint)payload.Length), payload);
    private static byte[] Root(string stream, string cls)
    {
        var a = Encoding.ASCII.GetBytes(stream); var b = Encoding.ASCII.GetBytes(cls);
        return Chunk("ROOT", Join(U32((uint)a.Length), a, U32((uint)b.Length), b));
    }

    [Fact]
    public async Task ReadsCubase12NamedRecordShapeAsCandidateValues()
    {
        var name = Object("Name", ScalarString("String", "Kick"));
        var volume = Object("Volume", Float("AnchorValue", -6.5), Float("Value", 12800));
        var eq = Object("EQ", RecordArray("Band", [Integer("Enable", 1), Float("Gain", 2.0)]), Blob("panner", [1, 2, 3, 4]));
        var records = Join(name, volume, eq, Integer("ChannelType", 3), Integer("Mute", 1), Integer("Solo", 0),
            Object("OwnInputBus", Integer("Bus UID", 42)), Object("OutputBus", Integer("Value", 91)),
            Object("SendFolder", RecordArray("Slot", [Object("Output", Integer("Value", 92))])));
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
