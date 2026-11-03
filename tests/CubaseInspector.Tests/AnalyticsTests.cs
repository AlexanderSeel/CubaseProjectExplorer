using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class AnalyticsTests
{
    private static ProjectSnapshot Snapshot(string path, EvidenceLevel level = EvidenceLevel.Confirmed,
        IReadOnlyList<PluginReference>? plugins = null, IReadOnlyList<RoutingEdge>? routing = null) =>
        new(path, Path.GetFileNameWithoutExtension(path), "DAWproject", null, 120, level,
            [new("a", "Drums", "Audio", "a", "b", null, null, null),
             new("b", "Bus", "Group", "b", "c", null, null, null),
             new("c", "Master", "Master", "c", null, null, null, null),
             new("d", "Bass", "Audio", "d", "b", null, null, null)],
            plugins ?? [], routing ?? [], []);

    private static PluginReference Plugin(string id = "vst3-class-id", EvidenceLevel level = EvidenceLevel.Confirmed) =>
        new(id, "Super Compressor", "Acme Audio", "Vst3Plugin", "a", level);

    [Fact]
    public void Audit_CountsDistinctProjectsAndInstances_ExcludesCandidates()
    {
        var scan = new FolderReport([
            Snapshot("a.dawproject", plugins: [Plugin(), Plugin(), Plugin("offset-1", EvidenceLevel.Candidate)]),
            Snapshot("b.dawproject", plugins: [Plugin()]),
            Snapshot("c.cpr", EvidenceLevel.Candidate, plugins: [Plugin("offset-2", EvidenceLevel.Candidate)])],
            [("bad.cpr", "Unreadable")]);
        var audit = new PluginUsageAuditService().Analyze(scan);
        var usage = Assert.Single(audit.Plugins);
        Assert.Equal(2, usage.ProjectCount);
        Assert.Equal(3, usage.InstanceCount);
        Assert.Equal(2, audit.IgnoredCandidateReferences);
        Assert.Null(usage.LatestProjectFileModifiedUtc);
        Assert.Contains("NOT actual last-used", new PluginUsageAuditService().ToMarkdown(audit));
        Assert.Single(audit.ScanFailures);
    }

    [Fact]
    public void Audit_DoesNotMergeDifferentNamesWithSameExportId()
    {
        var other = Plugin() with { Name = "Unrelated Effect" };
        var scan = new FolderReport([Snapshot("a", plugins: [Plugin()]), Snapshot("b", plugins: [other])], []);
        Assert.Equal(2, new PluginUsageAuditService().Analyze(scan).Plugins.Count);
    }

    [Fact]
    public void LibraryUsage_GroupsConfirmedDisplayLabelsAndKeepsCandidateTokensSeparate()
    {
        var confirmedA = Snapshot("a.dawproject", plugins: [Plugin("export-local-1"), Plugin("export-local-2")]);
        var confirmedB = Snapshot("b.dawproject", plugins: [Plugin("other-export-id")]);
        var candidateA = Snapshot("legacy-a.cpr", EvidenceLevel.Candidate, plugins:
            [Plugin("candidate-token-123", EvidenceLevel.Candidate), Plugin("candidate-token-123", EvidenceLevel.Candidate)]);
        var candidateB = Snapshot("legacy-b.cpr", EvidenceLevel.Candidate, plugins:
            [Plugin("candidate-token-123", EvidenceLevel.Candidate), Plugin("unknown-token", EvidenceLevel.Unknown)]);

        var index = LibraryPluginUsageIndexer.Build([confirmedA, confirmedB, candidateA, candidateB]);

        Assert.Equal(4, index.StoredProjectCount);
        Assert.Equal(2, index.ConfirmedProjectCount);
        Assert.Equal(3, index.CandidateReferenceCount);
        Assert.Equal(1, index.UnknownReferenceCount);
        var confirmed = Assert.Single(index.ConfirmedReferences);
        Assert.Equal(2, confirmed.ProjectCount);
        Assert.Equal(3, confirmed.ReferenceCount);
        Assert.Equal(3, confirmed.DistinctExportedIds);
        var candidate = Assert.Single(index.CandidateMarkers);
        Assert.Equal("candidate-token-123", candidate.Token);
        Assert.Equal(2, candidate.ProjectCount);
        Assert.Equal(3, candidate.OccurrenceCount);
    }

    [Fact]
    public void LibraryUsage_AccumulatorCanBuildAcrossIncrementalBatches()
    {
        var projects = new[]
        {
            Snapshot("a.dawproject", plugins: [Plugin("one"), Plugin("two")]),
            Snapshot("legacy.cpr", EvidenceLevel.Candidate, plugins:
                [Plugin("marker", EvidenceLevel.Candidate) with { Name = "Name unavailable" },
                 Plugin("marker", EvidenceLevel.Candidate) with { Name = "Later name" },
                 Plugin("", EvidenceLevel.Candidate), Plugin("unknown", EvidenceLevel.Unknown)])
        };
        var accumulator = new LibraryPluginUsageIndexer.LibraryPluginUsageAccumulator();
        accumulator.Add(projects[0]);
        accumulator.Add(projects[1]);

        var incremental = accumulator.Build();
        var batch = LibraryPluginUsageIndexer.Build(projects);

        Assert.Equal(batch.StoredProjectCount, incremental.StoredProjectCount);
        Assert.Equal(batch.ConfirmedProjectCount, incremental.ConfirmedProjectCount);
        Assert.Equal(batch.CandidateReferenceCount, incremental.CandidateReferenceCount);
        Assert.Equal(batch.CandidateWithoutTokenCount, incremental.CandidateWithoutTokenCount);
        Assert.Equal(batch.UnknownReferenceCount, incremental.UnknownReferenceCount);
        var batchConfirmed = Assert.Single(batch.ConfirmedReferences);
        var incrementalConfirmed = Assert.Single(incremental.ConfirmedReferences);
        Assert.Equal(batchConfirmed with { ProjectPaths = [] }, incrementalConfirmed with { ProjectPaths = [] });
        Assert.Equal(batchConfirmed.ProjectPaths, incrementalConfirmed.ProjectPaths);
        var batchCandidate = Assert.Single(batch.CandidateMarkers);
        var incrementalCandidate = Assert.Single(incremental.CandidateMarkers);
        Assert.Equal(batchCandidate with { ProjectPaths = [] }, incrementalCandidate with { ProjectPaths = [] });
        Assert.Equal(batchCandidate.ProjectPaths, incrementalCandidate.ProjectPaths);
        Assert.Equal("Name unavailable", Assert.Single(incremental.CandidateMarkers).ExampleName);
    }

    [Fact]
    public void Statistics_ExcludeCandidateMarkers()
    {
        var stats = new ProjectStatisticsService().Summarize(Snapshot("a.cpr", plugins:
            [Plugin(), Plugin("offset", EvidenceLevel.Candidate)]));
        Assert.Equal(2, stats.TrackTypes["Audio"]);
        Assert.Equal(1, stats.PluginFormats["Vst3Plugin"]);
        Assert.Equal(1, stats.CandidatePluginReferences);
    }

    [Fact]
    public void Routing_PreservesEdgeTypesAndUnresolvedEndpoints()
    {
        var source = Snapshot("project.dawproject", routing:
            [new("a", "b", "Output"), new("a", "b", "Send"), new("b", "c", "Output"),
             new("b", "external", "Sidechain")]);
        var service = new RoutingGraphService();
        var traced = service.Trace(source, "a");
        Assert.True(traced.IsSuccess);
        Assert.Equal(4, traced.Value!.Edges.Count);
        Assert.Equal("external", Assert.Single(traced.Value.UnresolvedEndpoints));
        Assert.False(traced.Value.ContainsCycle);
        var upstream = service.Trace(source, "c", upstream: true);
        Assert.Equal(3, upstream.Value!.ChannelIds.Count);
        Assert.Equal(3, upstream.Value.Edges.Count);
    }

    [Fact]
    public void Routing_DistinguishesDiamondFromActualCycle()
    {
        var edges = new[] { new RoutingEdge("a", "b", "Output"), new RoutingEdge("a", "d", "Send"),
            new RoutingEdge("d", "b", "Output"), new RoutingEdge("b", "c", "Output") };
        var service = new RoutingGraphService();
        Assert.False(service.Trace(Snapshot("x", routing: edges), "a").Value!.ContainsCycle);
        Assert.True(service.Trace(Snapshot("x", routing: [.. edges, new("c", "a", "Send")]), "a").Value!.ContainsCycle);
    }

    [Fact]
    public void Routing_TraversesInterpretedDataAndRejectsUnknownChannels()
    {
        var service = new RoutingGraphService();
        Assert.True(service.Trace(Snapshot("x.cpr", EvidenceLevel.Candidate), "a").IsSuccess);
        Assert.Equal("CHANNEL_NOT_FOUND", service.Trace(Snapshot("x"), "missing").Error?.Code);
        Assert.Equal("INVALID_LIMIT", service.Trace(Snapshot("x"), "a", maxChannels: 0).Error?.Code);
    }
}
