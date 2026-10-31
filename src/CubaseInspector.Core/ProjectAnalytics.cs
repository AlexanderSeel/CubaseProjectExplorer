using System.Globalization;
using System.Text;

namespace CubaseInspector.Core;

public sealed record ProjectStatistics(
    IReadOnlyDictionary<string, int> TrackTypes,
    IReadOnlyDictionary<string, int> PluginFormats,
    IReadOnlyDictionary<string, int> Manufacturers,
    int CandidatePluginReferences);

public sealed class ProjectStatisticsService
{
    public ProjectStatistics Summarize(ProjectSnapshot project) => new(
        Count(project.Tracks.Select(t => t.Kind)),
        Count(project.Plugins.Where(p => p.Evidence == EvidenceLevel.Confirmed).Select(p => p.Format)),
        Count(project.Plugins.Where(p => p.Evidence == EvidenceLevel.Confirmed)
            .Select(p => string.IsNullOrWhiteSpace(p.Manufacturer) ? "Unknown" : p.Manufacturer!)),
        project.Plugins.Count(p => p.Evidence != EvidenceLevel.Confirmed));

    private static IReadOnlyDictionary<string, int> Count(IEnumerable<string> values) => values
        .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>References in project files, not verified installed plugins or actual usage timestamps.</summary>
public sealed record PluginUsage(string Name, string PluginId, string Format, string? Manufacturer,
    int ProjectCount, int InstanceCount, DateTimeOffset? LatestProjectFileModifiedUtc,
    IReadOnlyList<string> ProjectPaths);

public sealed record PluginUsageAudit(
    IReadOnlyList<PluginUsage> Plugins, int ScannedProjects, int IgnoredCandidateReferences,
    IReadOnlyList<(string Path, string Error)> ScanFailures);

public sealed class PluginUsageAuditService
{
    public PluginUsageAudit Analyze(FolderReport scan,
        IReadOnlyDictionary<string, DateTimeOffset>? projectModificationTimes = null)
    {
        // Use name as well as ID: some exports reuse per-instance IDs in unrelated projects.
        var references = scan.Projects.SelectMany(project => project.Plugins
            .Where(p => p.Evidence == EvidenceLevel.Confirmed && !string.IsNullOrWhiteSpace(p.Name))
            .Select(plugin => (project, plugin)));
        var usage = references.GroupBy(x => (
            Name: x.plugin.Name.Trim().ToUpperInvariant(),
            Id: x.plugin.Id.Trim().ToUpperInvariant(),
            Format: x.plugin.Format.Trim().ToUpperInvariant(),
            Manufacturer: (x.plugin.Manufacturer ?? "").Trim().ToUpperInvariant()))
            .Select(group =>
            {
                var example = group.First().plugin;
                var projects = group.Select(g => g.project.FilePath).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                var timestamps = projects.Where(p => projectModificationTimes?.ContainsKey(p) == true)
                    .Select(p => projectModificationTimes![p]).ToList();
                return new PluginUsage(example.Name, example.Id, example.Format, example.Manufacturer,
                    projects.Count, group.Count(), timestamps.Count == 0 ? null : timestamps.Max(), projects);
            })
            .OrderByDescending(p => p.ProjectCount).ThenByDescending(p => p.InstanceCount)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new PluginUsageAudit(usage, scan.Projects.Count,
            scan.Projects.Sum(p => p.Plugins.Count(x => x.Evidence != EvidenceLevel.Confirmed)),
            scan.Failures);
    }

    public string ToMarkdown(PluginUsageAudit audit)
    {
        var result = new StringBuilder().AppendLine("# Cross-project plugin reference audit")
            .AppendLine()
            .AppendLine($"Projects inspected: {audit.ScannedProjects}; unresolved candidate references excluded: {audit.IgnoredCandidateReferences}; scan failures: {audit.ScanFailures.Count}.")
            .AppendLine()
            .AppendLine("Counts represent references in parsed project snapshots, NOT installed plugin verification. File modification timestamps are NOT actual last-used times. Plugin disk usage is not calculated.")
            .AppendLine()
            .AppendLine("| Plugin | Format | Projects | Instances | Latest project file modified (UTC) |")
            .AppendLine("|---|---|---:|---:|---|");
        foreach (var p in audit.Plugins)
            result.AppendLine($"| {Cell(p.Name)} | {Cell(p.Format)} | {p.ProjectCount} | {p.InstanceCount} | {p.LatestProjectFileModifiedUtc?.UtcDateTime.ToString("u", CultureInfo.InvariantCulture) ?? "Unknown"} |");
        if (audit.ScanFailures.Count != 0)
        {
            result.AppendLine().AppendLine("## Scan failures");
            foreach (var failure in audit.ScanFailures)
                result.AppendLine($"- {Cell(failure.Path)}: {Cell(failure.Error)}");
        }
        return result.ToString();
    }

    public string ToCsv(PluginUsageAudit audit)
    {
        var result = new StringBuilder("Name,PluginId,Format,Manufacturer,ProjectCount,InstanceCount,LatestProjectFileModifiedUtc,ProjectPaths").AppendLine();
        foreach (var p in audit.Plugins)
            result.AppendLine(string.Join(",", new[]
            {
                p.Name, p.PluginId, p.Format, p.Manufacturer, p.ProjectCount.ToString(CultureInfo.InvariantCulture),
                p.InstanceCount.ToString(CultureInfo.InvariantCulture),
                p.LatestProjectFileModifiedUtc?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                string.Join("; ", p.ProjectPaths)
            }.Select(CsvCell)));
        return result.ToString();
    }

    private static string CsvCell(string? item)
    {
        var value = item ?? "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}

/// <summary>A read-only typed multigraph traversal. No missing route is inferred.</summary>
public sealed record RoutingTrace(string SelectedChannelId, bool Upstream,
    IReadOnlyList<string> ChannelIds, IReadOnlyList<RoutingEdge> Edges,
    IReadOnlyList<string> UnresolvedEndpoints, bool ContainsCycle);

public sealed class RoutingGraphService
{
    public Result<RoutingTrace> Trace(ProjectSnapshot project, string channelId, bool upstream = false,
        int maxChannels = 10000)
    {
        if (project.Evidence != EvidenceLevel.Confirmed)
            return Result<RoutingTrace>.Fail("UNVERIFIED_ROUTING", "Routing is unavailable for candidate-only project evidence.");
        if (string.IsNullOrWhiteSpace(channelId))
            return Result<RoutingTrace>.Fail("CHANNEL_REQUIRED", "Select a channel ID.");
        if (maxChannels <= 0)
            return Result<RoutingTrace>.Fail("INVALID_LIMIT", "maxChannels must be positive.");
        var declared = project.Tracks.Select(t => t.ChannelId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        if (!declared.Contains(channelId))
            return Result<RoutingTrace>.Fail("CHANNEL_NOT_FOUND", "Selected channel is not present in parsed channels.");

        var visited = new HashSet<string>(StringComparer.Ordinal) { channelId };
        var queue = new Queue<string>();
        queue.Enqueue(channelId);
        var traversed = new List<RoutingEdge>();
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        var adjacency = project.Routing.GroupBy(e => upstream ? e.ToChannelId : e.FromChannelId,
            StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var connected)) continue;
            foreach (var edge in connected)
            {
                traversed.Add(edge); // Multiple output/send/sidechain edges stay separate.
                var next = upstream ? edge.FromChannelId : edge.ToChannelId;
                if (!declared.Contains(next)) unresolved.Add(next);
                if (visited.Contains(next)) continue;
                if (visited.Count >= maxChannels)
                    return Result<RoutingTrace>.Fail("GRAPH_LIMIT", "Routing traversal exceeded the channel safety limit.");
                visited.Add(next);
                if (declared.Contains(next)) queue.Enqueue(next);
            }
        }
        // Topological reduction distinguishes real cycles from diamond-shaped paths.
        var internalEdges = traversed.Where(e => visited.Contains(e.FromChannelId) && visited.Contains(e.ToChannelId)).ToList();
        var indegree = visited.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var edge in internalEdges) indegree[edge.ToChannelId]++;
        var ready = new Queue<string>(indegree.Where(p => p.Value == 0).Select(p => p.Key));
        var forward = internalEdges.GroupBy(e => e.FromChannelId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var removed = 0;
        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            removed++;
            if (!forward.TryGetValue(node, out var outgoing)) continue;
            foreach (var edge in outgoing)
                if (--indegree[edge.ToChannelId] == 0) ready.Enqueue(edge.ToChannelId);
        }
        return Result<RoutingTrace>.Ok(new RoutingTrace(channelId, upstream,
            visited.OrderBy(x => x, StringComparer.Ordinal).ToList(), traversed,
            unresolved.OrderBy(x => x, StringComparer.Ordinal).ToList(), removed < visited.Count));
    }
}
