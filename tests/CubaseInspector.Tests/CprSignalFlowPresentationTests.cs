using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprSignalFlowPresentationTests
{
    private static CprMixerObservation Mixer(string name, long offset, params CprNamedRecordValue[] fields) =>
        new(name, -3, null, false, null, offset, "PArrangement", CandidateFields: fields);

    private static ProjectSnapshot Project(params CprMixerObservation[] records) =>
        new("session.cpr", "Session", "CPR", null, null, EvidenceLevel.Candidate, [], [], [], [],
            NativeMixerObservations: records);

    [Fact]
    public void LinksBusIdsAndKeepsSendSourceOffsetsWithoutPromotingEvidence()
    {
        var project = Project(
            Mixer("Audio", 100, new CprNamedRecordValue("OutputBus/Value", "i64", "42", 120),
                new CprNamedRecordValue("OutputBus/Name", "string", "Saved Group Label", 115),
                new CprNamedRecordValue("SendFolder/Slot[0]/Output/Value", "i64", "91", 150)),
            Mixer("Group", 200, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "42", 220)),
            Mixer("Reverb", 300, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "91", 320)));

        var view = CprSignalFlowPresentation.Create(project);
        var source = view.Tracks.Single(track => track.Name == "Audio");
        var focus = RoutingFocusBuilder.Build(view, source.ChannelId!).Value!;

        Assert.Equal(2, focus.Outgoing.Count);
        Assert.Contains(focus.Outgoing, link => link.Channel.Name == "Group" && link.Edge.Kind == "Output");
        Assert.Equal("Saved Group Label", Assert.Single(view.Routing, edge => edge.Kind == "Output").TargetLabel);
        var send = Assert.Single(focus.Outgoing, link => link.Edge.Kind == "Send");
        Assert.Equal("Reverb", send.Channel.Name);
        Assert.Equal(150, send.Edge.SourceOffset);
        Assert.EndsWith("SendFolder/Slot[0]/Output/Value", send.Edge.SourcePath);
        Assert.All(view.Routing, edge => Assert.Equal(EvidenceLevel.Candidate, edge.Evidence));
        Assert.Equal(EvidenceLevel.Candidate, view.Evidence);
        Assert.Empty(project.Routing);
        Assert.Empty(project.Tracks);
    }

    [Fact]
    public void DuplicateNamesStaySeparateAndAmbiguousIdsStayUnresolved()
    {
        var view = CprSignalFlowPresentation.Create(Project(
            Mixer("Same", 10, new CprNamedRecordValue("OutputBus/Value", "i64", "8", 12)),
            Mixer("Same", 20, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "8", 22)),
            Mixer("Same", 30, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "8", 32))));

        Assert.Equal(3, view.Tracks.Count);
        Assert.Equal(3, view.Tracks.Select(track => track.Id).Distinct().Count());
        var destination = Assert.Single(view.Routing).ToChannelId;
        Assert.Contains("Bus UID 8", destination);
        Assert.DoesNotContain(view.Tracks, track => track.ChannelId == destination);
        Assert.Contains(RoutingFocusBuilder.Channels(view), channel => channel.Id == destination && channel.Track is null);
    }

    [Fact]
    public void MissingIdsKeepRecordsVisibleWithoutInventingConnectionsFromNamesOrMixerValues()
    {
        var view = CprSignalFlowPresentation.Create(Project(Mixer("Stereo Out", 10),
            Mixer("Audio", 20, new CprNamedRecordValue("Volume/AnchorValue", "f64", "-3", 22))));

        Assert.Equal(2, RoutingFocusBuilder.Channels(view).Count);
        Assert.Empty(view.Routing);
        Assert.Null(view.Tracks[0].Volume); // Native dB values must not become DAWproject linear volume.
        Assert.True(RoutingFocusBuilder.Build(view, view.Tracks[0].ChannelId!).IsSuccess);
    }

    [Fact]
    public void BusIdsDoNotJoinAcrossSourceStreams()
    {
        var otherStream = Mixer("Bus", 30, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "42", 32)) with { SourceStream = "OtherArchive" };
        var view = CprSignalFlowPresentation.Create(Project(
            Mixer("Audio", 10, new CprNamedRecordValue("OutputBus/Value", "i64", "42", 12)), otherStream));

        Assert.Contains("Bus UID 42", Assert.Single(view.Routing).ToChannelId);
    }

    [Fact]
    public void ReadsOutputBusValueAndInputBusValueReferencesAgainstUniqueBusOwners()
    {
        var view = CprSignalFlowPresentation.Create(Project(
            Mixer("Input bus", 100, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "17", 110)),
            Mixer("Track channel", 200,
                new CprNamedRecordValue("InputBusValue", "i64", "17", 210),
                new CprNamedRecordValue("OutputBusValue", "i64", "18", 220)),
            Mixer("Output bus", 300, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "18", 310))));

        var channel = view.Tracks.Single(track => track.Name == "Track channel");
        var incoming = Assert.Single(view.Routing, edge => edge.Kind == "Input");
        var output = Assert.Single(view.Routing, edge => edge.Kind == "Output");
        Assert.Equal("cpr-mixer-0-64", incoming.FromChannelId);
        Assert.Equal(channel.Id, incoming.ToChannelId);
        Assert.Equal(channel.Id, output.FromChannelId);
        Assert.Equal("cpr-mixer-2-12C", output.ToChannelId);
        Assert.EndsWith("InputBusValue", incoming.SourcePath);
        Assert.EndsWith("OutputBusValue", output.SourcePath);
        Assert.All(view.Routing, edge => Assert.Equal(EvidenceLevel.Candidate, edge.Evidence));
    }

    [Fact]
    public void AssociatesMixerRecordToTrackOnlyWhenTheWholeNamedRecordIsInsideOneTrackBody()
    {
        var track = new TrackInfo("arr-track", "Bass", "MIDI", null, null, null, null, null,
            EvidenceLevel.Candidate, "MMidiTrackEvent", 90, SourceEndOffset: 180);
        var project = Project(Mixer("Mixer Bass", 110,
            new CprNamedRecordValue("Name/String", "string", "Mixer Bass", 112),
            new CprNamedRecordValue("OutputBusValue", "i64", "42", 150))) with { Tracks = [track] };

        var mixer = Assert.Single(CprSignalFlowPresentation.Create(project).Tracks,
            item => item.SourceRecordClass == "CPR named record");
        Assert.Equal("arr-track", mixer.ParentTrackId);

        project = project with { Tracks = [track, track with { Id = "also-matches" }] };
        mixer = Assert.Single(CprSignalFlowPresentation.Create(project).Tracks,
            item => item.SourceRecordClass == "CPR named record");
        Assert.Null(mixer.ParentTrackId);
    }
}
