namespace CubaseInspector.Core;

public sealed record ProjectOutlineNode(
    string Label, string Kind, EvidenceLevel Evidence, IReadOnlyList<ProjectOutlineNode> Children);

/// <summary>
/// Read-only project outline. Only DAWproject confirmed track/plugin ID links become parent/child relationships.
/// Native CPR class hints cannot establish Cubase channel/insert ownership.
/// </summary>
public static class ProjectOutlineBuilder
{
    public static ProjectOutlineNode Build(ProjectSnapshot project)
    {
        var canLink = project.Evidence == EvidenceLevel.Confirmed;
        var assigned = new HashSet<int>();
        var trackNodes = new List<ProjectOutlineNode>();
        for (var trackIndex = 0; trackIndex < project.Tracks.Count; trackIndex++)
        {
            var track = project.Tracks[trackIndex];
            var devices = new List<ProjectOutlineNode>();
            if (canLink && track.Evidence == EvidenceLevel.Confirmed)
            {
                for (var i = 0; i < project.Plugins.Count; i++)
                {
                    var plugin = project.Plugins[i];
                    if (plugin.Evidence == EvidenceLevel.Confirmed &&
                        string.Equals(plugin.TrackId, track.Id, StringComparison.Ordinal))
                    {
                        assigned.Add(i);
                        devices.Add(new ProjectOutlineNode(
                            $"{plugin.Name} · {(plugin.Role ?? plugin.Format)} · slot {(plugin.Slot?.ToString() ?? "?")}",
                            "Plugin", plugin.Evidence, []));
                    }
                }
            }
            trackNodes.Add(new ProjectOutlineNode(
                $"{track.Name} · {track.Kind} · {track.Evidence}",
                "Track", track.Evidence, devices));
        }

        // Link only uniquely declared structured track IDs. Native CPR candidates,
        // duplicate IDs, missing parents and cycles remain visible at root level.
        IReadOnlyList<ProjectOutlineNode> hierarchy = trackNodes;
        if (canLink && trackNodes.Count > 0)
        {
            var distinctIds = project.Tracks.Select((track, index) => (track, index))
                .GroupBy(item => item.track.Id, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single().index,
                    StringComparer.Ordinal);
            var parent = new Dictionary<int, int>();
            for (var i = 0; i < project.Tracks.Count; i++)
            {
                var declaration = project.Tracks[i].ParentTrackId;
                if (declaration is not null && distinctIds.TryGetValue(declaration, out var parentIndex)
                    && i != parentIndex)
                    parent[i] = parentIndex;
            }
            var childIndices = Enumerable.Range(0, trackNodes.Count)
                .Where(parent.ContainsKey).GroupBy(i => parent[i])
                .ToDictionary(g => g.Key, g => g.ToArray());
            var used = new HashSet<int>();
            ProjectOutlineNode? Nest(int index, HashSet<int> ancestry)
            {
                if (!used.Add(index)) return null;
                var nextAncestry = new HashSet<int>(ancestry) { index };
                var children = trackNodes[index].Children.ToList();
                if (childIndices.TryGetValue(index, out var nested))
                    foreach (var child in nested)
                    {
                        if (nextAncestry.Contains(child)) continue;
                        var node = Nest(child, nextAncestry);
                        if (node is not null) children.Add(node);
                    }
                return trackNodes[index] with { Children = children };
            }
            var tree = new List<ProjectOutlineNode>();
            foreach (var index in Enumerable.Range(0, trackNodes.Count).Where(i => !parent.ContainsKey(i)))
                if (Nest(index, []) is { } node) tree.Add(node);
            foreach (var index in Enumerable.Range(0, trackNodes.Count))
                if (!used.Contains(index) && Nest(index, []) is { } node) tree.Add(node);
            hierarchy = tree;
        }

        var unassigned = project.Plugins.Where((_, i) => !assigned.Contains(i))
            .Select(p => new ProjectOutlineNode(
                $"{p.Name} · {p.Format} · {p.Evidence}", "Unassigned plugin/marker", p.Evidence, []))
            .ToArray();
        var categories = new List<ProjectOutlineNode>();
        if (trackNodes.Count > 0)
            categories.Add(new ProjectOutlineNode(
                canLink ? "Declared track hierarchy and inserts" : "Unverified CPR track records (no inferred hierarchy)",
                "Group", project.Evidence, hierarchy));
        if (canLink && project.MediaReferences is { Count: > 0 } media)
            categories.Add(new ProjectOutlineNode(
                "Declared audio media references (existence not checked)", "Group",
                EvidenceLevel.Confirmed,
                media.Select(item => new ProjectOutlineNode(
                    item.Path + (item.SampleRate.HasValue ? " · " + item.SampleRate + " Hz" : ""),
                    "Media reference", EvidenceLevel.Confirmed, [])).ToArray()));
        if (canLink && project.TimelineItems is { Count: > 0 } timeline)
            categories.Add(new ProjectOutlineNode(
                "Declared clips and markers (static export)", "Group",
                EvidenceLevel.Confirmed,
                timeline.Select(item => new ProjectOutlineNode(
                    item.Kind + " · " + (item.Name ?? "(unnamed)") +
                    " · position " + item.Time.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Timeline", EvidenceLevel.Confirmed, [])).ToArray()));
        if (unassigned.Length > 0)
            categories.Add(new ProjectOutlineNode(
                "Unassigned or unverified plugin references / markers",
                "Group", project.Evidence, unassigned));
        return new ProjectOutlineNode(project.Name + " · " + project.Evidence,
            "Project", project.Evidence, categories);
    }
}
