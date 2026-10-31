namespace CubaseInspector.Core;

public sealed record RoutingFocusChannel(string Id, string Name, string Kind, TrackInfo? Track,
    IReadOnlyList<PluginReference> Devices);
public sealed record RoutingFocusConnection(RoutingFocusChannel Channel, RoutingEdge Edge);
public sealed record RoutingFocus(RoutingFocusChannel Selected, IReadOnlyList<RoutingFocusConnection> Incoming,
    IReadOnlyList<RoutingFocusConnection> Outgoing, IReadOnlyList<RoutingFocusChannel> OutputPath,
    bool ContainsCycle, IReadOnlyList<string> Warnings);

/// <summary>Direct source connections around a selected channel, retaining their evidence and identifiers.</summary>
public static class RoutingFocusBuilder
{
    public static IReadOnlyList<RoutingFocusChannel> Channels(ProjectSnapshot project)
    {
        var tracks = project.Tracks.Where(track => !string.IsNullOrWhiteSpace(track.ChannelId))
            .GroupBy(track => track.ChannelId!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray());
        var uniqueTrackIds = project.Tracks.GroupBy(track => track.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var devices = project.Plugins.Where(plugin => plugin.Evidence == EvidenceLevel.Confirmed && plugin.TrackId is not null)
            .GroupBy(plugin => plugin.TrackId!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.OrderBy(plugin => plugin.Slot).ToArray());
        var ids = tracks.Keys.Concat(project.Routing.SelectMany(edge => new[] { edge.FromChannelId, edge.ToChannelId }))
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal);
        var endpointLabels = project.Routing.Where(edge => !string.IsNullOrWhiteSpace(edge.TargetLabel))
            .GroupBy(edge => edge.ToChannelId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetLabel!).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        return ids.Select(id =>
        {
            var matches = tracks.GetValueOrDefault(id) ?? [];
            var track = matches.Length == 1 ? matches[0] : null;
            var labels = endpointLabels.GetValueOrDefault(id) ?? [];
            var endpointName = labels.Length == 1 ? labels[0] + " · " + id : id;
            return new RoutingFocusChannel(id, track?.Name ?? endpointName + (matches.Length > 1 ? " (ambiguous)" : " (unresolved)"),
                track?.ChannelRole ?? track?.Kind ?? "Unresolved", track,
                track is not null && uniqueTrackIds.Contains(track.Id) ? devices.GetValueOrDefault(track.Id) ?? [] : []);
        }).OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static Result<RoutingFocus> Build(ProjectSnapshot project, string channelId)
    {
        var channels = Channels(project).ToDictionary(channel => channel.Id, StringComparer.Ordinal);
        if (!channels.TryGetValue(channelId, out var selected))
            return Result<RoutingFocus>.Fail("UNKNOWN_CHANNEL", "Choose a channel or routing endpoint read from this project.");
        var incoming = project.Routing.Where(edge => edge.ToChannelId == channelId && channels.ContainsKey(edge.FromChannelId))
            .Select(edge => new RoutingFocusConnection(channels[edge.FromChannelId], edge)).ToArray();
        var outgoing = project.Routing.Where(edge => edge.FromChannelId == channelId && channels.ContainsKey(edge.ToChannelId))
            .Select(edge => new RoutingFocusConnection(channels[edge.ToChannelId], edge)).ToArray();
        var outputs = project.Routing.Where(edge => edge.Kind.Equals("Output", StringComparison.OrdinalIgnoreCase))
            .GroupBy(edge => edge.FromChannelId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ToChannelId).Distinct(StringComparer.Ordinal).ToArray());
        var path = new List<RoutingFocusChannel> { selected };
        var seen = new HashSet<string>(StringComparer.Ordinal) { channelId };
        var current = channelId;
        var cycle = false;
        var warnings = new List<string>();
        while (outputs.TryGetValue(current, out var destinations))
        {
            if (destinations.Length != 1) { warnings.Add("Multiple output destinations; signal path stops at this channel."); break; }
            var next = destinations[0];
            if (!seen.Add(next)) { cycle = true; warnings.Add("Output cycle detected; signal path stops before repeating a channel."); break; }
            if (path.Count >= 128) { warnings.Add("Signal path limited to 128 channels."); break; }
            if (!channels.TryGetValue(next, out var target)) break;
            path.Add(target);
            current = next;
        }
        if (selected.Track is null || incoming.Concat(outgoing).Any(connection => connection.Channel.Track is null))
            warnings.Add("Some channel identities are missing or ambiguous. Their device ownership is not assigned.");
        var trace = new RoutingGraphService().Trace(project, channelId);
        if (trace.Value?.ContainsCycle == true && !cycle)
        {
            cycle = true;
            warnings.Add("A reachable routing cycle exists, including output/send connections.");
        }
        return Result<RoutingFocus>.Ok(new(selected, incoming, outgoing, path, cycle, warnings));
    }
}
