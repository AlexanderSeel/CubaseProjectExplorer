namespace CubaseInspector.Core;

public sealed record RoutingDiagramNode(
    string ChannelId, string Label, double X, double Y, bool HasConfirmedTrack);

public sealed record RoutingDiagramEdge(
    string FromChannelId, string ToChannelId, string Kind,
    double X1, double Y1, double X2, double Y2, bool BackwardOrCycle);

public sealed record RoutingDiagram(
    IReadOnlyList<RoutingDiagramNode> Nodes, IReadOnlyList<RoutingDiagramEdge> Edges,
    double Width, double Height, bool Truncated, string Explanation);

/// <summary>
/// Deterministic, bounded, read-only horizontal rendering layout for confirmed routing edges.
/// Does not infer endpoints, topology, or physical I/O from native CPR candidates.
/// </summary>
public static class RoutingDiagramBuilder
{
    private const int MaxNodes = 100;
    private const int MaxEdges = 250;
    private const double NodeWidth = 192;
    private const double NodeHeight = 62;
    private const double HorizontalStep = 242;
    private const double VerticalStep = 91;

    public static RoutingDiagram Build(ProjectSnapshot project, IReadOnlyList<RoutingEdge> sourceEdges)
    {
        if (project.Evidence != EvidenceLevel.Confirmed)
            return Empty("Routing unavailable: CPR candidate records do not establish verified connections.");
        if (sourceEdges.Count == 0)
            return Empty("No confirmed routing edges found for the selected channel.");

        var chosen = sourceEdges.Take(MaxEdges).ToArray();
        var ids = chosen.SelectMany(e => new[] { e.FromChannelId, e.ToChannelId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).Take(MaxNodes).ToArray();
        var set = ids.ToHashSet(StringComparer.Ordinal);
        var edges = chosen.Where(e => set.Contains(e.FromChannelId) && set.Contains(e.ToChannelId))
            .ToArray();
        if (ids.Length == 0) return Empty("No readable channel IDs in routing edges.");

        // Kahn layering for acyclic regions. Cycles go in a final column;
        // loop/backward edges are flagged explicitly rather than concealed.
        var incoming = ids.ToDictionary(x => x, _ => 0, StringComparer.Ordinal);
        var outgoing = ids.ToDictionary(x => x, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            incoming[edge.ToChannelId]++;
            outgoing[edge.FromChannelId].Add(edge.ToChannelId);
        }
        var levels = ids.ToDictionary(x => x, _ => 0, StringComparer.Ordinal);
        var queue = new Queue<string>(ids.Where(id => incoming[id] == 0));
        var processed = new HashSet<string>(StringComparer.Ordinal);
        while (queue.TryDequeue(out var current))
        {
            processed.Add(current);
            foreach (var next in outgoing[current])
            {
                levels[next] = Math.Max(levels[next], levels[current] + 1);
                if (--incoming[next] == 0) queue.Enqueue(next);
            }
        }
        var maxLevel = levels.Values.DefaultIfEmpty(0).Max();
        foreach (var remaining in ids.Where(id => !processed.Contains(id)))
            levels[remaining] = maxLevel + 1;

        var tracks = project.Tracks
            .Where(t => t.Evidence == EvidenceLevel.Confirmed && !string.IsNullOrWhiteSpace(t.ChannelId))
            .GroupBy(t => t.ChannelId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var nodes = new List<RoutingDiagramNode>();
        foreach (var level in levels.GroupBy(p => p.Value).OrderBy(g => g.Key))
        {
            var row = 0;
            foreach (var id in level.Select(p => p.Key).OrderBy(x => x, StringComparer.Ordinal))
            {
                var hasTrack = tracks.TryGetValue(id, out var name);
                nodes.Add(new RoutingDiagramNode(id, hasTrack ? name! + " (" + id + ")" : id + " (unresolved)",
                    28 + level.Key * HorizontalStep, 38 + row++ * VerticalStep, hasTrack));
            }
        }
        var map = nodes.ToDictionary(n => n.ChannelId, StringComparer.Ordinal);
        var lines = edges.Select(e =>
        {
            var from = map[e.FromChannelId];
            var to = map[e.ToChannelId];
            return new RoutingDiagramEdge(e.FromChannelId, e.ToChannelId, e.Kind,
                from.X + NodeWidth, from.Y + NodeHeight / 2,
                to.X, to.Y + NodeHeight / 2,
                levels[e.ToChannelId] <= levels[e.FromChannelId]);
        }).ToArray();
        var width = Math.Max(520, nodes.Max(n => n.X) + NodeWidth + 40);
        var height = Math.Max(280, nodes.Max(n => n.Y) + NodeHeight + 48);
        return new RoutingDiagram(nodes, lines, width, height,
            sourceEdges.Count > MaxEdges || ids.Length == MaxNodes,
            "Directed routing from confirmed channel IDs only. Grey nodes indicate unresolved channel identity.");
    }

    private static RoutingDiagram Empty(string explanation) =>
        new([], [], 520, 280, false, explanation);
}
