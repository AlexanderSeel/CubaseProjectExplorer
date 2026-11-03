using System.Buffers.Binary;
using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class MidiFileExporterTests
{
    [Fact]
    public void WritesStandardMidiWithTempoAndDeclaredBeattimedNotes()
    {
        var project = new ProjectSnapshot("session.dawproject", "Session", "DAWproject", "15", 120,
            EvidenceLevel.Confirmed, [new TrackInfo("midi", "Piano", "MIDI", null, null, null, null, null)], [], [], [],
            MidiNotes: [new ProjectMidiNote("midi", .5, .25, 0, 60, .8, .4, "beats")]);

        var bytes = MidiFileExporter.Export(project, "midi");

        Assert.Equal("MThd", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8, 2)));
        Assert.Equal((ushort)480, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12, 2)));
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 }) >= 0);
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0x90, 60, 102 }) >= 0);
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0x80, 60, 51 }) >= 0);
        Assert.True(bytes.AsSpan().EndsWith(new byte[] { 0x00, 0xFF, 0x2F, 0x00 }));
    }

    [Fact]
    public void RefusesMIDIExportWhenTimelineEvidenceIsIncomplete()
    {
        var project = new ProjectSnapshot("session.dawproject", "Session", "DAWproject", "15", null,
            EvidenceLevel.Confirmed, [new TrackInfo("midi", "Piano", "MIDI", null, null, null, null, null)], [], [], [],
            MidiNotes: [new ProjectMidiNote("midi", .5, .25, 0, 60, .8, .4)]);

        var error = Assert.Throws<InvalidOperationException>(() => MidiFileExporter.Export(project, "midi"));
        Assert.Contains("time units", error.Message);
    }
}
