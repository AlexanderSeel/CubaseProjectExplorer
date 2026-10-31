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
        Assert.Contains(diagram.Nodes, node => node.Kind == "MIDI" && node.Description.Contains("2 notes"));
        Assert.Empty(diagram.Links);
    }
}
