using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class RoutingDiagramTests
{
    [Fact]
    public void AcyclicRoutingHasIncreasingLayersAndNamedConfirmedChannels()
    {
        var project = Snapshot(EvidenceLevel.Confirmed,
        [
            new TrackInfo("t1","Bass","Audio","c1","bus",null,null,null),
            new TrackInfo("t2","Group","Group","bus","master",null,null,null),
            new TrackInfo("t3","Output","Master","master",null,null,null,null)
        ]);
        var diagram = RoutingDiagramBuilder.Build(project,
        [
            new RoutingEdge("c1","bus","Output"),
            new RoutingEdge("bus","master","Output")
        ]);
        Assert.Equal(3, diagram.Nodes.Count);
        Assert.Equal(2, diagram.Edges.Count);
        Assert.True(diagram.Nodes.Single(n=>n.ChannelId=="c1").X <
            diagram.Nodes.Single(n=>n.ChannelId=="master").X);
        Assert.All(diagram.Nodes, n=>Assert.True(n.HasConfirmedTrack));
        Assert.DoesNotContain(diagram.Edges, e=>e.BackwardOrCycle);
    }

    [Fact]
    public void CandidateCprNeverProducesFabricatedGraph()
    {
        var project = Snapshot(EvidenceLevel.Candidate, []);
        var result = RoutingDiagramBuilder.Build(project,
            [new RoutingEdge("a","b","Output")]);
        Assert.Empty(result.Edges);
        Assert.Contains("unavailable", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CycleAndMissingEndpointsAreExplicit()
    {
        var project = Snapshot(EvidenceLevel.Confirmed, []);
        var diagram = RoutingDiagramBuilder.Build(project,
        [
            new RoutingEdge("a","b","Send"),
            new RoutingEdge("b","a","Return")
        ]);
        Assert.Equal(2, diagram.Edges.Count);
        Assert.All(diagram.Nodes, n=>Assert.False(n.HasConfirmedTrack));
        Assert.All(diagram.Edges, e=>Assert.True(e.BackwardOrCycle));
    }

    private static ProjectSnapshot Snapshot(EvidenceLevel evidence, IReadOnlyList<TrackInfo> tracks) =>
        new("test.dawproject","Test","DAWproject",null,null,evidence,tracks,[],[],[]);
}
