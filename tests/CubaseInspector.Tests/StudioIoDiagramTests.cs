using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class StudioIoDiagramTests
{
    [Fact]
    public void SavedPortStringsSupplyDirectionAndSourceDetailsWithoutInferringOtherConnections()
    {
        var project = new ProjectSnapshot("session.cpr", "Session", "CPR", null, null, EvidenceLevel.Candidate, [], [], [], [],
            NativeMixerObservations: [new("Bus", -3, null, false, false, 100, "Devices", CandidateFields:
            [new("OwnInputBus/Bus UID", "i64", "42", 110),
             new("OwnInputBus/Connections[0]", "string", "I|Saved Driver|Input 1", 120),
             new("OwnInputBus/Connections[1]", "string", "O|Saved Driver|Output 2", 130),
             new("OwnInputBus/Connections[2]", "string", "unclassified saved connection", 140)])]);

        var diagram = StudioIoDiagramBuilder.Build(project);
        Assert.Equal(3, diagram.SavedPorts);
        var input = Assert.Single(diagram.Nodes, node => node.Kind == "Saved input port");
        var output = Assert.Single(diagram.Nodes, node => node.Kind == "Saved output port");
        Assert.Equal("Input 1", input.Name);
        Assert.Equal("Saved Driver", input.Description);
        Assert.Equal(120, input.SourceOffset);
        Assert.Contains(diagram.Links, link => link.From == input.Id && link.Label == "Saved input assignment");
        Assert.Contains(diagram.Links, link => link.To == output.Id && link.Label == "Saved output assignment");
        var unknown = Assert.Single(diagram.Nodes, node => node.Kind == "Saved port description");
        Assert.DoesNotContain(diagram.Links, link => link.From == unknown.Id || link.To == unknown.Id);
        Assert.Empty(project.Routing);
    }

    [Fact]
    public void NativeAudioAndMidiRemainInspectableWithoutInventingADeviceRoute()
    {
        var project = new ProjectSnapshot("session.cpr", "Session", "CPR", null, null, EvidenceLevel.Candidate, [], [], [], [],
            NativeAudioMediaCandidates: [new("Audio/kick.wav", 42, "Drums", 10, "MAudioEvent")],
            NativeMidiNoteCandidates: [new(100, 0, 120, 60, 100), new(200, 120, 120, 64, 100)]);

        var diagram = StudioIoDiagramBuilder.Build(project);
        Assert.Equal(1, diagram.AudioPaths);
        Assert.Equal(2, diagram.MidiNotes);
        Assert.Contains(diagram.Nodes, node => node.Kind == "Audio path" && node.Name == "kick.wav");
        Assert.Contains(diagram.Nodes, node => node.Kind == "Native MIDI part" && node.Description.Contains("2 notes"));
        Assert.Empty(diagram.Links);
    }

    [Fact]
    public void NativeInsertSlotsLeadToChannelOutputAndRoutedBusWithoutLosingEvidence()
    {
        var project = new ProjectSnapshot("session.cpr", "Session", "CPR", null, null, EvidenceLevel.Candidate, [], [], [], [],
            NativeMixerObservations:
            [
                new("Keys", -3, null, false, false, 100, "Devices", CandidateFields:
                [new("OutputBus/Value", "i64", "42", 110),
                 new("InsertFolder/Slot[0]/Plugin Name", "string", "Compressor", 120),
                 new("InsertFolder/Slot[1]/Plugin Name", "string", "Limiter", 130),
                 new("ChannelEQ/ChannelEQBand[0]/EQBandGainValue", "f64", "-2.5", 140),
                 new("postInsertsPreFader", "i64", "1", 145)]),
                new("Group", -1, null, false, false, 200, "Devices", CandidateFields:
                [new("OwnInputBus/Bus UID", "i64", "42", 210)])
            ]);

        var diagram = StudioIoDiagramBuilder.Build(project);
        var inserts = diagram.Nodes.Where(node => node.Kind == "Mixer insert").OrderBy(node => node.SourceOffset).ToArray();
        Assert.Equal(["Compressor", "Limiter"], inserts.Select(node => node.Name));
        Assert.All(inserts, node => Assert.Equal(EvidenceLevel.Candidate, node.Evidence));
        var keysChannel = Assert.Single(diagram.Nodes, node => node.Name == "Keys" && node.Kind == "Mixer record");
        var groupChannel = Assert.Single(diagram.Nodes, node => node.Name == "Group" && node.Kind == "Mixer record");
        var output = Assert.Single(diagram.Nodes, node => node.Kind == "Channel output" && node.Name == "Keys · signal out");
        var eq = Assert.Single(diagram.Nodes, node => node.Kind == "Saved EQ settings");
        var insertPosition = Assert.Single(diagram.Nodes, node => node.Kind == "Insert placement setting");
        Assert.Contains(eq.StoredFields!, field => field.Contains("EQBandGainValue = -2.5", StringComparison.Ordinal));
        Assert.Contains(insertPosition.StoredFields!, field => field.Contains("postInsertsPreFader = 1", StringComparison.Ordinal));
        Assert.Equal(EvidenceLevel.Candidate, insertPosition.Evidence);
        Assert.Contains(diagram.Links, link => link.From == keysChannel.Id && link.To == inserts[0].Id);
        Assert.Contains(diagram.Links, link => link.From == inserts[0].Id && link.To == inserts[1].Id);
        Assert.Contains(diagram.Links, link => link.From == inserts[1].Id && link.To == output.Id);
        Assert.Contains(diagram.Links, link => link.From == keysChannel.Id && link.To == eq.Id);
        Assert.Contains(diagram.Links, link => link.From == output.Id && link.To == groupChannel.Id && link.Label == "Output");
        Assert.DoesNotContain(diagram.Links, link => link.From == keysChannel.Id && link.To == groupChannel.Id);
    }
}
