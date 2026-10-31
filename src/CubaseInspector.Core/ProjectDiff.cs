using System.Globalization;
namespace CubaseInspector.Core;

/// <summary>Compares only confirmed structured data; labels ambiguous identities.</summary>
public sealed class ProjectDiff : IProjectDiff
{
    public IReadOnlyList<ProjectChange> Compare(ProjectSnapshot before, ProjectSnapshot after,
        IReadOnlyList<TrackMatchDecision>? trackMatches = null)
    {
        var changes = new List<ProjectChange>();
        if (before.Evidence != EvidenceLevel.Confirmed || after.Evidence != EvidenceLevel.Confirmed)
            return [new("Unavailable", "Project", "Comparison", null, null,
                "A structural comparison requires two confirmed snapshots")];
        if (before.Bpm != after.Bpm)
            changes.Add(new("Transport", "Project", "Bpm", S(before.Bpm), S(after.Bpm), "Exact"));
        var decisions = trackMatches ?? ProjectTrackMatching.Suggest(before, after);
        ProjectTrackMatching.Validate(before, after, decisions);
        var matchQuality = trackMatches is null ? "Unique name/type heuristic" : "User-specified track match";
        var originalBefore = before;
        var originalAfter = after;
        // IDs in DAWproject are document-local references. Normalize owners and
        // route endpoints through the same reviewed track mapping before diffing.
        before = NormalizeReferences(before, decisions.ToDictionary(d => d.BeforeIndex,
            d => d.AfterIndex.HasValue ? $"Matched track {d.BeforeIndex + 1}: {originalBefore.Tracks[d.BeforeIndex].Name}"
                : $"A track {d.BeforeIndex + 1}: {originalBefore.Tracks[d.BeforeIndex].Name}"), "A");
        after = NormalizeReferences(after, decisions.Where(d => d.AfterIndex.HasValue)
            .ToDictionary(d => d.AfterIndex!.Value,
                d => $"Matched track {d.BeforeIndex + 1}: {originalBefore.Tracks[d.BeforeIndex].Name}"), "B");
        var usedOld = new HashSet<int>();
        var usedNew = new HashSet<int>();
        var pairs = new List<(TrackInfo Old, TrackInfo New, TrackInfo SourceOld, TrackInfo SourceNew, string Match)>();
        foreach (var decision in decisions)
        {
            if (decision.AfterIndex is not int target) continue;
            pairs.Add((before.Tracks[decision.BeforeIndex], after.Tracks[target],
                originalBefore.Tracks[decision.BeforeIndex], originalAfter.Tracks[target], matchQuality));
            usedOld.Add(decision.BeforeIndex); usedNew.Add(target);
        }
        if (trackMatches is null)
        {
            var unmatchedKeys = after.Tracks.Where((_, index) => !usedNew.Contains(index))
                .Select(track => (track.Name, track.Kind)).ToHashSet();
            foreach (var decision in decisions.Where(decision => !decision.AfterIndex.HasValue))
                if (unmatchedKeys.Contains((before.Tracks[decision.BeforeIndex].Name, before.Tracks[decision.BeforeIndex].Kind)))
                    changes.Add(new("Ambiguous", before.Tracks[decision.BeforeIndex].Name, "Match",
                        originalBefore.Tracks[decision.BeforeIndex].Id, null, "Duplicate name/type: identity unresolved"));
        }
        foreach (var (oldTrack, newTrack, sourceOld, sourceNew, match) in pairs)
        {
            Add("Name", oldTrack.Name, newTrack.Name);
            Add("Kind", oldTrack.Kind, newTrack.Kind);
            Add("Volume", S(oldTrack.Volume), S(newTrack.Volume));
            Add("Pan", S(oldTrack.Pan), S(newTrack.Pan));
            Add("Mute", S(oldTrack.Mute), S(newTrack.Mute));
            Add("Solo", S(oldTrack.Solo), S(newTrack.Solo));
            if (oldTrack.DestinationChannelId != newTrack.DestinationChannelId)
                changes.Add(new("Track", oldTrack.Name, "Destination", sourceOld.DestinationChannelId, sourceNew.DestinationChannelId, match));
            if (oldTrack.ParentTrackId != newTrack.ParentTrackId)
                changes.Add(new("Track", oldTrack.Name, "Parent track", sourceOld.ParentTrackId, sourceNew.ParentTrackId, match));
            void Add(string property, string? a, string? b)
            {
                if (a != b) changes.Add(new("Track", oldTrack.Name, property, a, b, match));
            }
        }
        for (var i = 0; i < before.Tracks.Count; i++)
            if (!usedOld.Contains(i))
                changes.Add(new("Track", before.Tracks[i].Name, "Removed", before.Tracks[i].Kind, null, "Unmatched"));
        for (var i = 0; i < after.Tracks.Count; i++)
            if (!usedNew.Contains(i))
                changes.Add(new("Track", after.Tracks[i].Name, "Added", null, after.Tracks[i].Kind, "Unmatched"));

        ComparePlugins(before.Plugins.Where(p => p.Evidence == EvidenceLevel.Confirmed).ToArray(),
            after.Plugins.Where(p => p.Evidence == EvidenceLevel.Confirmed).ToArray(), changes);
        CountChanges(before.Routing, after.Routing, EdgeKey, "Routing", changes);
        return changes.Select(change => change.Category is "Plugin" or "Routing"
            ? change with { MatchQuality = matchQuality + "; " + change.MatchQuality } : change).ToArray();
    }

    private static ProjectSnapshot NormalizeReferences(ProjectSnapshot project,
        IReadOnlyDictionary<int, string> identities, string side)
    {
        string Identity(int index) => identities.GetValueOrDefault(index) ?? $"{side} track {index + 1}: {project.Tracks[index].Name}";
        var trackIds = project.Tracks.Select((track, index) => (track.Id, Index: index))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() == 1)
            .ToDictionary(group => group.Key, group => Identity(group.Single().Index), StringComparer.Ordinal);
        var channelIds = project.Tracks.Select((track, index) => (track.ChannelId, Index: index))
            .Where(item => !string.IsNullOrWhiteSpace(item.ChannelId))
            .GroupBy(item => item.ChannelId!, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => "Channel / " + Identity(group.Single().Index), StringComparer.Ordinal);
        string? Track(string? id) => id is null ? null : trackIds.GetValueOrDefault(id) ?? $"Unresolved track ({side}): {id}";
        string? Channel(string? id) => id is null ? null : channelIds.GetValueOrDefault(id) ?? $"Unresolved channel ({side}): {id}";
        return project with
        {
            Tracks = project.Tracks.Select((track, index) => track with
            {
                Id = Identity(index), ChannelId = Channel(track.ChannelId),
                DestinationChannelId = Channel(track.DestinationChannelId), ParentTrackId = Track(track.ParentTrackId)
            }).ToArray(),
            Plugins = project.Plugins.Select(plugin => plugin with { TrackId = Track(plugin.TrackId) }).ToArray(),
            Routing = project.Routing.Select(edge => edge with
            {
                FromChannelId = Channel(edge.FromChannelId)!, ToChannelId = Channel(edge.ToChannelId)!
            }).ToArray()
        };
    }
    /// <summary>
    /// Match only a unique exported plugin class within a known owner. The same VST3 class
    /// identifier may occur many times, so it must never be called an exact instance UID.
    /// When owners/slots are not unique, fall back to aggregate counts.
    /// </summary>
    private static void ComparePlugins(
        IReadOnlyList<PluginReference> oldPlugins,
        IReadOnlyList<PluginReference> newPlugins,
        List<ProjectChange> changes)
    {
        var consumedOld = new HashSet<int>();
        var consumedNew = new HashSet<int>();
        static bool ValidOwnerAndClass(PluginReference p) =>
            !string.IsNullOrWhiteSpace(p.TrackId) &&
            !string.IsNullOrWhiteSpace(p.Id) &&
            !string.IsNullOrWhiteSpace(p.Format);
        static string OwnerClass(PluginReference p) =>
            string.Join("\u001F", p.TrackId, p.Format, p.Id);

        var oldClasses = Enumerable.Range(0, oldPlugins.Count)
            .Where(i => ValidOwnerAndClass(oldPlugins[i]))
            .GroupBy(i => OwnerClass(oldPlugins[i]))
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var newClasses = Enumerable.Range(0, newPlugins.Count)
            .Where(i => ValidOwnerAndClass(newPlugins[i]))
            .GroupBy(i => OwnerClass(newPlugins[i]))
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        foreach (var (key, oldIndices) in oldClasses)
        {
            if (oldIndices.Length != 1 ||
                !newClasses.TryGetValue(key, out var newIndices) || newIndices.Length != 1)
                continue;
            var oldIndex = oldIndices[0];
            var newIndex = newIndices[0];
            var oldPlugin = oldPlugins[oldIndex];
            var newPlugin = newPlugins[newIndex];
            consumedOld.Add(oldIndex);
            consumedNew.Add(newIndex);
            var entity = oldPlugin.Name + " @ " + oldPlugin.TrackId;
            const string confidence = "Unique exported owner/class; not a stable instance UID";
            void Add(string field, string? before, string? after)
            {
                if (before != after)
                    changes.Add(new ProjectChange("Plugin", entity, field, before, after, confidence));
            }
            Add("Name", oldPlugin.Name, newPlugin.Name);
            Add("Vendor", oldPlugin.Manufacturer, newPlugin.Manufacturer);
            Add("Version", oldPlugin.Version, newPlugin.Version);
            Add("Role", oldPlugin.Role, newPlugin.Role);
            Add("Insert slot", S(oldPlugin.Slot), S(newPlugin.Slot));
            Add("Enabled", S(oldPlugin.Enabled), S(newPlugin.Enabled));
        }

        // If two different plugins uniquely occupy exactly the same confirmed owner/slot,
        // report an insert-slot content change. Do not assert that the physical instance was
        // replaced, loaded, or that the slot uniquely identifies a VST3 instance.
        for (var oldIndex = 0; oldIndex < oldPlugins.Count; oldIndex++)
        {
            if (consumedOld.Contains(oldIndex)) continue;
            var prior = oldPlugins[oldIndex];
            if (string.IsNullOrWhiteSpace(prior.TrackId) || prior.Slot is null) continue;
            var oldSlotCandidates = Enumerable.Range(0, oldPlugins.Count)
                .Where(i => !consumedOld.Contains(i) && SameOwnerSlot(oldPlugins[i], prior)).ToArray();
            var newSlotCandidates = Enumerable.Range(0, newPlugins.Count)
                .Where(i => !consumedNew.Contains(i) && SameOwnerSlot(newPlugins[i], prior)).ToArray();
            if (oldSlotCandidates.Length != 1 || oldSlotCandidates[0] != oldIndex ||
                newSlotCandidates.Length != 1) continue;
            var newIndex = newSlotCandidates[0];
            var next = newPlugins[newIndex];
            if (string.Equals(prior.Id, next.Id, StringComparison.Ordinal) &&
                string.Equals(prior.Format, next.Format, StringComparison.Ordinal)) continue;
            consumedOld.Add(oldIndex);
            consumedNew.Add(newIndex);
            changes.Add(new ProjectChange("Plugin", prior.TrackId + " / slot " + prior.Slot,
                "Slot content", prior.Name + " (" + prior.Format + ")",
                next.Name + " (" + next.Format + ")",
                "Unique exported owner/slot; class changed, instance identity unknown"));
        }

        CountChanges(Enumerable.Range(0, oldPlugins.Count)
                .Where(i => !consumedOld.Contains(i)).Select(i => oldPlugins[i]),
            Enumerable.Range(0, newPlugins.Count)
                .Where(i => !consumedNew.Contains(i)).Select(i => newPlugins[i]),
            PluginKey, "Plugin", changes);
    }

    private static bool SameOwnerSlot(PluginReference candidate, PluginReference source) =>
        candidate.Slot is not null && candidate.Slot == source.Slot &&
        string.Equals(candidate.TrackId, source.TrackId, StringComparison.Ordinal);

    private static void CountChanges<T>(IEnumerable<T> before, IEnumerable<T> after,
        Func<T, string> key, string category, List<ProjectChange> changes)
    {
        var a = before.GroupBy(key).ToDictionary(g => g.Key, g => g.Count());
        var b = after.GroupBy(key).ToDictionary(g => g.Key, g => g.Count());
        foreach (var identity in a.Keys.Union(b.Keys).OrderBy(s => s, StringComparer.Ordinal))
        {
            a.TryGetValue(identity, out var x);
            b.TryGetValue(identity, out var y);
            if (x != y)
                changes.Add(new(category, identity, "Instance count", x.ToString(CultureInfo.InvariantCulture),
                    y.ToString(CultureInfo.InvariantCulture), "Confirmed exported reference"));
        }
    }
    private static string PluginKey(PluginReference p) => string.Join("|",
        p.TrackId ?? "?", p.Format, p.Id, p.Name, p.Version ?? "?", p.Role ?? "?",
        p.Slot?.ToString(CultureInfo.InvariantCulture) ?? "?", p.Enabled?.ToString() ?? "?");
    private static string EdgeKey(RoutingEdge e) => string.Join("|",
        e.FromChannelId, e.ToChannelId, e.Kind, S(e.Level) ?? "?",
        S(e.Enabled) ?? "?", e.Tap ?? "?");
    private static string? S(object? value) => value switch
    {
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => value?.ToString()
    };
}
