using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class RoutingFocusTests
{
    private static TrackInfo Channel(string id, string role) => new(id, id, role, id, null, .5, false, null, ChannelRole: role);
    private static ProjectSnapshot Project(TrackInfo[] tracks, RoutingEdge[] edges) =>
        new("test.dawproject", "Test", "DAWproject", "15", 120, EvidenceLevel.Confirmed, tracks, [], edges, []);

    [Fact]
    public void SelectedBusExposesDirectFeedersDestinationsDevicesAndOutputPath()
    {
        var project = Project([Channel("audio", "regular"), Channel("group", "submix"), Channel("master", "master"), Channel("fx", "effect")],
            [new("audio", "group", "Output"), new("group", "master", "Output"), new("audio", "fx", "Send", 0, false)]);
        project = project with { Plugins = [new("class", "Compressor", null, "Vst3Plugin", "group", EvidenceLevel.Confirmed, Slot: 0)] };
        var focus = RoutingFocusBuilder.Build(project, "group").Value!;
        Assert.Equal("audio", Assert.Single(focus.Incoming).Channel.Id);
        Assert.Equal("master", Assert.Single(focus.Outgoing).Channel.Id);
        Assert.Equal("Compressor", Assert.Single(focus.Selected.Devices).Name);
        Assert.Equal(["group", "master"], focus.OutputPath.Select(channel => channel.Id));
        var audio = RoutingFocusBuilder.Build(project, "audio").Value!;
        Assert.Equal(2, audio.Outgoing.Count);
        Assert.Contains(audio.Outgoing, connection => connection.Edge.Kind == "Send" && connection.Edge.Enabled == false && connection.Edge.Level == 0);
    }

    [Fact]
    public void OutputWithoutDestinationDoesNotInventHardwareOrFinalOutputEdge()
    {
        var focus = RoutingFocusBuilder.Build(Project([Channel("master", "master")], []), "master").Value!;
        Assert.Empty(focus.Outgoing);
        Assert.Single(focus.OutputPath);
    }

    [Fact]
    public void AmbiguousOrMissingChannelDoesNotAcquireDeviceOwnership()
    {
        var project = Project([Channel("a", "regular"), Channel("a", "master"), Channel("b", "regular")],
            [new("b", "a", "Output"), new("a", "missing", "Send")]);
        project = project with { Plugins = [new("class", "EQ", null, "Vst3Plugin", "a", EvidenceLevel.Confirmed)] };
        var focus = RoutingFocusBuilder.Build(project, "a").Value!;
        Assert.Null(focus.Selected.Track);
        Assert.Empty(focus.Selected.Devices);
        Assert.Null(Assert.Single(focus.Outgoing).Channel.Track);
        Assert.NotEmpty(focus.Warnings);
    }

    [Fact]
    public void OutputAndSendCyclesAreExplicitAndNativeCprIsRejected()
    {
        var project = Project([Channel("a", "regular"), Channel("b", "submix")],
            [new("a", "b", "Output"), new("b", "a", "Send")]);
        var focus = RoutingFocusBuilder.Build(project, "a").Value!;
        Assert.True(focus.ContainsCycle);
        Assert.NotEmpty(focus.Warnings);
        Assert.Empty(RoutingFocusBuilder.Channels(project with { Evidence = EvidenceLevel.Candidate }));
        Assert.Equal("UNVERIFIED_ROUTING", RoutingFocusBuilder.Build(project with { Evidence = EvidenceLevel.Candidate }, "a").Error?.Code);
    }
}
