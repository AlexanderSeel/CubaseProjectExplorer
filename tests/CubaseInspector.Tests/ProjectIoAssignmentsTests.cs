using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectIoAssignmentsTests
{
    [Fact]
    public void ConflictingInputMetadataWithoutAUniquePortRemainsUnresolved()
    {
        var project = new ProjectSnapshot("x.cpr", "x", "CPR", "15", null, EvidenceLevel.Candidate,
            [new("track", "Vocal", "Audio", null, null, null, null, null, EvidenceLevel.Candidate,
                "MAudioTrackEvent", 10, SourceEndOffset: 1000)], [], [], [], NativeMixerObservations:
            [new("Vocal", null, null, null, null, 100, "Project", CandidateFields:
            [new("Input Device Name", "string", "Device A", 110),
             new("Input Device Name", "string", "Device B", 120)])]);
        var row = Assert.Single(ProjectIoAssignments.From(StudioIoDiagramBuilder.Build(project)));
        Assert.False(row.EndpointsResolved);
        Assert.Equal("Assignment not resolved", row.From);
    }

    [Fact]
    public void ZeroFlatInputAndOutputReferencesFollowTheAsmGateWithoutChangingOtherUidShapes()
    {
        var fields = new CprNamedRecordValue[] { new("InputBusValue", "i64", "0", 110),
            new("OutputBusValue", "i64", "0", 120), new("OutputBus/Value", "i64", "0", 130) };
        var project = new ProjectSnapshot("x.cpr", "x", "CPR", "15", null, EvidenceLevel.Candidate, [], [], [], [],
            NativeMixerObservations: [new("Channel", null, null, null, null, 100, "Project", CandidateFields: fields)]);
        var route = Assert.Single(CprSignalFlowPresentation.Create(project).Routing);
        Assert.EndsWith("OutputBus/Value", route.SourcePath);
        Assert.Equal(3, project.NativeMixerObservations![0].CandidateFields!.Count);
        Assert.Equal(EvidenceLevel.Candidate, route.Evidence);
    }

    [Fact]
    public void UnresolvedBusReferencesAreNeverPresentedAsResolvedMappings()
    {
        var diagram = new StudioIoDiagram(
            [new("track", "Keys", "Instrument", "", "CPR", Evidence: EvidenceLevel.Candidate),
             new("port", "Output 1", "Saved output port", "", "CPR", Evidence: EvidenceLevel.Candidate),
             new("Bus UID 0 [Project]", "Bus 0", "Bus reference", "", "CPR")],
            [new("track", "port", "Saved output assignment", "OutputBus/Connections[0]", 42, EvidenceLevel.Candidate),
             new("track", "Bus UID 0 [Project]", "Output", "OutputBus/Value", 50, EvidenceLevel.Candidate)], 0, 0, 1);
        var rows = ProjectIoAssignments.From(diagram);
        Assert.True(rows[0].EndpointsResolved);
        Assert.Equal("Output 1", rows[0].To);
        Assert.Equal(42, rows[0].SourceOffset);
        Assert.Equal(EvidenceLevel.Candidate, rows[0].Evidence);
        Assert.False(rows[1].EndpointsResolved);
        Assert.Equal("Assignment not resolved", rows[1].To);
    }

    [Fact]
    public void ConflictingDeviceNamesAreNotResolvedByTakingTheFirstValue()
    {
        var project = new ProjectSnapshot("x.cpr", "x", "CPR", "15", null, EvidenceLevel.Candidate,
            [new("track", "Vocal", "Audio", null, null, null, null, null, EvidenceLevel.Candidate,
                "MAudioTrackEvent", 10, SourceEndOffset: 1000)], [], [], [], NativeMixerObservations:
            [new("Vocal", null, null, null, null, 100, "Project", CandidateFields:
            [new("Input Device Name", "string", "Device A", 110),
             new("Input Device Name", "string", "Device B", 120),
             new("Input Port Name", "string", "Mic 1", 130)])]);
        var input = Assert.Single(StudioIoDiagramBuilder.Build(project).Nodes, node => node.Kind == "Saved track input");
        Assert.Equal("Mic 1", input.Name);
        Assert.Equal(3, input.StoredFields!.Count);
        Assert.Equal(EvidenceLevel.Candidate, input.Evidence);
    }
}
