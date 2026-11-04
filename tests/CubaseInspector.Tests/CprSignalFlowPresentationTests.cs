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
    public void BusIdsAreScopedToArchiveOffsetsEvenWhenStreamNamesRepeat()
    {
        var first = Mixer("First", 110, new CprNamedRecordValue("OutputBusValue/Value", "i64", "42", 120))
            with { SourceArchiveOffset = 100 };
        var second = Mixer("Second", 210, new CprNamedRecordValue("OutputBusValue/Value", "i64", "42", 220))
            with { SourceArchiveOffset = 200 };
        var target = Mixer("Bus", 250, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "42", 260))
            with { SourceArchiveOffset = 200 };
        var legacy = Mixer("Legacy", 310, new CprNamedRecordValue("OutputBusValue/Value", "i64", "42", 320));
        var view = CprSignalFlowPresentation.Create(Project(first, second, target, legacy));

        var firstEdge = Assert.Single(view.Routing, edge => edge.FromChannelId == "cpr-mixer-0-6E");
        var secondEdge = Assert.Single(view.Routing, edge => edge.FromChannelId == "cpr-mixer-1-D2");
        var legacyEdge = Assert.Single(view.Routing, edge => edge.FromChannelId == "cpr-mixer-3-136");
        Assert.StartsWith("Bus UID 42", firstEdge.ToChannelId);
        Assert.Equal("cpr-mixer-2-FA", secondEdge.ToChannelId);
        Assert.StartsWith("Bus UID 42", legacyEdge.ToChannelId);
        Assert.NotEqual(firstEdge.ToChannelId, legacyEdge.ToChannelId);
    }

    [Fact]
    public void ZeroSelectionsDoNotBecomeRoutesOrConsumeUnusedSlots()
    {
        var observation = Mixer("Disconnected", 100,
            new CprNamedRecordValue("InputBusValue/Value", "i64", "0", 110),
            new CprNamedRecordValue("OutputBusValue/Value", "i64", "0", 120),
            new CprNamedRecordValue("SendFolder/Slot[0]/Output/Value", "i64", "0", 130),
            new CprNamedRecordValue("directRoutingFolder/Slot[0]/Output/Value", "i64", "0", 140),
            new CprNamedRecordValue("RecorderBus/Bus UID", "i64", "0", 150),
            new CprNamedRecordValue("OwnInputBus/Child Bus[0]/Bus UID", "i64", "0", 160),
            new CprNamedRecordValue("Parent Bus UID", "i64", "0", 170));
        var view = CprSignalFlowPresentation.Create(Project(observation));
        Assert.Empty(view.Routing);
        Assert.Equal(7, Assert.Single(view.NativeMixerObservations!).CandidateFields!.Count);
    }

    [Theory]
    [InlineData("42", 1)]
    [InlineData("91", 0)]
    [InlineData("-1", 0)]
    public void DuplicateDestinationPathsRequireOneStoredValue(string secondValue, int routes)
    {
        var view = CprSignalFlowPresentation.Create(Project(Mixer("Source", 100,
            new CprNamedRecordValue("OutputBusValue/Value", "i64", "42", 110),
            new CprNamedRecordValue("OutputBusValue/Value", "i64", secondValue, 120))));
        Assert.Equal(routes, view.Routing.Count);
        Assert.Equal(2, Assert.Single(view.NativeMixerObservations!).CandidateFields!.Count);
        if (routes == 1) Assert.Equal(110, view.Routing[0].SourceOffset);
    }

    [Fact]
    public void ChildBusArraySyntaxRetainsEachMembershipAndItsName()
    {
        var view = CprSignalFlowPresentation.Create(Project(Mixer("Bus", 100,
            new CprNamedRecordValue("OwnInputBus/Child Bus[0]/Bus UID", "i64", "42", 110),
            new CprNamedRecordValue("OwnInputBus/Child Bus[0]/Name", "string", "Left", 120),
            new CprNamedRecordValue("RecorderBus/Child Bus[1]/Bus UID/Value", "i64", "43", 130),
            new CprNamedRecordValue("RecorderBus/Child Bus[1]/Name", "string", "Right", 140))));
        Assert.Equal(2, view.Routing.Count);
        Assert.Contains(view.Routing, edge => edge.Kind == "Child bus" && edge.TargetLabel == "Left" && edge.SourceOffset == 110);
        Assert.Contains(view.Routing, edge => edge.Kind == "Child bus" && edge.TargetLabel == "Right" && edge.SourceOffset == 130);
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

    [Fact]
    public void DisplaysExactTrackOwnerForNativeChannelEndpoints()
    {
        var track = new TrackInfo("arr-audio", "Lead vocal", "Audio", null, null, null, null, null,
            EvidenceLevel.Candidate, "MAudioTrackEvent", 100, SourceEndOffset: 300);
        var project = Project(
            Mixer("Mixer channel", 130,
                new CprNamedRecordValue("OutputBusValue", "i64", "42", 160)),
            Mixer("Group bus", 400,
                new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "42", 430))) with
        { Tracks = [track] };

        var view = CprSignalFlowPresentation.Create(project);
        Assert.Equal("arr-audio", view.Tracks.Single(item => item.Id == "cpr-mixer-0-82").ParentTrackId);
        var channel = RoutingFocusBuilder.Channels(view).Single(item => item.Id == "cpr-mixer-0-82");
        var destination = RoutingFocusBuilder.Channels(view).Single(item => item.Id == "cpr-mixer-1-190");

        Assert.Equal("Lead vocal · channel", channel.Name);
        Assert.Equal("Group bus", destination.Name);
        Assert.Equal(EvidenceLevel.Candidate, view.Routing.Single().Evidence);
    }

    [Fact]
    public void ReadsRecorderBusDirectRoutingAndChildBusIdentifiersAsSeparateConnections()
    {
        var view = CprSignalFlowPresentation.Create(Project(
            Mixer("Lead", 100,
                new CprNamedRecordValue("OutputBusValue", "i64", "17", 110),
                new CprNamedRecordValue("directRoutingFolder/Slot[0]/Output/Value", "i64", "23", 120),
                new CprNamedRecordValue("RecorderBus/Bus UID", "i64", "31", 130),
                new CprNamedRecordValue("RecorderBus/Name", "string", "Vocal record bus", 132),
                new CprNamedRecordValue("OwnInputBus/Child Bus/[0]/Bus UID", "i64", "18", 140),
                new CprNamedRecordValue("OwnInputBus/Child Bus/[0]/Name", "string", "Left output", 142)),
            Mixer("Main out", 200, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "17", 210)),
            Mixer("Cue out", 300, new CprNamedRecordValue("OwnInputBus/Bus UID", "i64", "23", 310))));

        var lead = view.Tracks.Single(track => track.Name == "Lead");
        Assert.Contains(view.Routing, edge => edge.FromChannelId == lead.Id && edge.Kind == "Output" &&
            edge.ToChannelId == "cpr-mixer-1-C8");
        Assert.Contains(view.Routing, edge => edge.FromChannelId == lead.Id && edge.Kind == "Child bus" &&
            edge.ToChannelId == "cpr-bus:PArrangement:18");
        Assert.Contains(view.Routing, edge => edge.FromChannelId == lead.Id && edge.Kind == "Direct output" &&
            edge.ToChannelId == "cpr-mixer-2-12C");
        Assert.Contains(view.Routing, edge => edge.FromChannelId == lead.Id && edge.Kind == "Record" &&
            edge.TargetLabel == "Vocal record bus");
        Assert.All(view.Routing.Where(edge => edge.SourcePath?.Contains("RecorderBus", StringComparison.Ordinal) == true ||
            edge.SourcePath?.Contains("directRoutingFolder", StringComparison.Ordinal) == true ||
            edge.SourcePath?.Contains("Child Bus", StringComparison.Ordinal) == true),
            edge => Assert.Equal(EvidenceLevel.Candidate, edge.Evidence));
    }

    [Fact]
    public void JoinsExplicitBusDefinitionsAndParentBusUidFromCubaseBusLoaderFields()
    {
        var view = CprSignalFlowPresentation.Create(Project(
            Mixer("Lead", 100, new CprNamedRecordValue("OutputBusValue", "i64", "42", 110)),
            Mixer("Stereo Out", 200,
                new CprNamedRecordValue("Bus UID", "i64", "42", 210),
                new CprNamedRecordValue("Bus Type", "i64", "3", 212),
                new CprNamedRecordValue("Input Arrangement", "string", "stereo", 214)),
            Mixer("Control Room", 300,
                new CprNamedRecordValue("Bus UID", "i64", "91", 310),
                new CprNamedRecordValue("Bus Type", "i64", "5", 312),
                new CprNamedRecordValue("Parent Bus UID", "i64", "42", 314))));

        var stereo = Assert.Single(view.Tracks, track => track.Name == "Stereo Out");
        Assert.Equal("Bus", stereo.Kind);
        var output = Assert.Single(view.Routing, edge => edge.Kind == "Output");
        Assert.Equal(stereo.Id, output.ToChannelId);
        var parent = Assert.Single(view.Routing, edge => edge.Kind == "Parent bus");
        Assert.Equal("cpr-mixer-2-12C", parent.FromChannelId);
        Assert.Equal(stereo.Id, parent.ToChannelId);
        Assert.EndsWith("Parent Bus UID", parent.SourcePath);
        Assert.All(view.Routing, edge => Assert.Equal(EvidenceLevel.Candidate, edge.Evidence));
    }

    [Fact]
    public void ShowsSavedTrackInputOnlyWhenNamedRecordIsInsideOneTrackExtent()
    {
        var track = new TrackInfo("arr-mic", "Vocal mic", "Audio", null, null, null, null, null,
            EvidenceLevel.Candidate, "MAudioTrackEvent", 90, SourceEndOffset: 200);
        var observation = Mixer("Saved device record", 120,
            new CprNamedRecordValue("Input Device Name", "string", "Studio Interface", 130),
            new CprNamedRecordValue("Input Channel ID", "i64", "2", 145),
            new CprNamedRecordValue("Input Port Name", "string", "Mic/Line 2", 160));
        var project = Project(observation) with { Tracks = [track] };

        var map = StudioIoDiagramBuilder.Build(project);
        var input = Assert.Single(map.Nodes, node => node.Kind == "Saved track input");
        Assert.Equal("Studio Interface · Mic/Line 2", input.Name);
        Assert.Contains(input.StoredFields!, value => value.Contains("Input Channel ID = 2", StringComparison.Ordinal));
        var link = Assert.Single(map.Links, item => item.Label == "Saved track input");
        Assert.Equal(input.Id, link.From);
        Assert.Equal("track:arr-mic", link.To);
        Assert.Equal(EvidenceLevel.Candidate, link.Evidence);

        var ambiguous = project with { Tracks = [track, track with { Id = "other" }] };
        Assert.DoesNotContain(StudioIoDiagramBuilder.Build(ambiguous).Nodes, node => node.Kind == "Saved track input");
    }
}
