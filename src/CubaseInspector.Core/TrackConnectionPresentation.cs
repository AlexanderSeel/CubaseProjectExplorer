namespace CubaseInspector.Core;

/// <summary>A single stored project connection shown in the context of one track.</summary>
public sealed record TrackConnectionInfo(string Direction, string Kind, string Endpoint,
    EvidenceLevel Evidence, string? SourcePath, long? SourceOffset,
    double? Level = null, bool? Enabled = null, string? Tap = null);

/// <summary>Projects explicit graph edges onto one track without inferring missing routes.</summary>
public static class TrackConnectionPresentation
{
    private static readonly HashSet<string> RouteKinds = new(StringComparer.Ordinal)
    {
        "Input", "Output", "Direct output", "Send", "Record", "Child bus", "Parent bus"
    };

    public static IReadOnlyList<TrackConnectionInfo> ForTrack(ProjectSnapshot project, TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);

        var flow = CprSignalFlowPresentation.Create(project);
        var channelIds = ResolveChannelIds(flow, track);

        var names = flow.Tracks.Where(item => item.ChannelId is not null)
            .GroupBy(item => item.ChannelId!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Name, StringComparer.Ordinal);
        var result = new List<TrackConnectionInfo>();
        foreach (var edge in flow.Routing)
        {
            if (!RouteKinds.Contains(edge.Kind)) continue;
            var forward = channelIds.Contains(edge.FromChannelId);
            var reverse = channelIds.Contains(edge.ToChannelId);
            if (!forward && !reverse) continue;

            var direction = edge.Kind switch
            {
                "Input" or "Saved track input" or "Saved input assignment" => "Input",
                "Output" or "Direct output" or "Saved output assignment" => "Output",
                "Send" => "Send",
                "Record" => "Record",
                "Child bus" => "Child bus",
                "Parent bus" => "Parent bus",
                _ => forward ? "Output" : "Input"
            };
            var endpointId = forward ? edge.ToChannelId : edge.FromChannelId;
            var endpoint = !string.IsNullOrWhiteSpace(edge.TargetLabel)
                ? edge.TargetLabel
                : names.GetValueOrDefault(endpointId) ?? UnresolvedEndpoint(endpointId);
            result.Add(new(direction, edge.Kind, endpoint, edge.Evidence, edge.SourcePath,
                edge.SourceOffset, edge.Level, edge.Enabled, edge.Tap));
        }
        AddStudioConnections(project, track, channelIds, result);
        return result.OrderBy(item => DirectionOrder(item.Direction))
            .ThenBy(item => item.Endpoint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceOffset).ToArray();
    }

    private static void AddStudioConnections(ProjectSnapshot project, TrackInfo track,
        HashSet<string> channelIds, List<TrackConnectionInfo> result)
    {
        var diagram = StudioIoDiagramBuilder.Build(project);
        var trackNodeId = "track:" + track.Id;
        var nodes = diagram.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var link in diagram.Links)
        {
            if (link.Label is not ("Saved track input" or "Saved input assignment" or
                "Declared audio reference" or "Audio event inside track object")) continue;
            var incoming = link.To == trackNodeId || channelIds.Contains(link.To);
            var outgoing = link.From == trackNodeId || channelIds.Contains(link.From);
            if (!incoming && !outgoing) continue;
            var endpointId = incoming ? link.From : link.To;
            if (!nodes.TryGetValue(endpointId, out var node)) continue;
            var direction = link.Label switch
            {
                "Saved track input" or "Saved input assignment" => "Input",
                _ => "Media"
            };
            var endpoint = link.Label is "Declared audio reference" or "Audio event inside track object"
                ? node.Description : node.Name;
            result.Add(new(direction, link.Label, endpoint, link.Evidence,
                link.Source, link.SourceOffset));
        }
    }

    private static HashSet<string> ResolveChannelIds(ProjectSnapshot flow, TrackInfo track)
    {
        if (track.Evidence == EvidenceLevel.Confirmed)
            return track.ChannelId is { Length: > 0 } channel ? [channel] : [];

        var ownedRecords = flow.Tracks.Where(item => item.SourceRecordClass == "CPR named record" &&
            item.ParentTrackId == track.Id && item.ChannelId is not null).ToArray();
        if (ownedRecords.Length != 1) return [];
        return [ownedRecords[0].ChannelId!];
    }

    private static int DirectionOrder(string direction) => direction switch
    {
        "Input" => 0,
        "Output" => 1,
        "Send" => 2,
        "Media" => 3,
        "Record" => 4,
        "Child bus" => 5,
        "Parent bus" => 6,
        _ => 7
    };

    private static string UnresolvedEndpoint(string id) => id.StartsWith("Bus UID ", StringComparison.Ordinal)
        ? $"Unresolved {id}" : "Unresolved endpoint";
}
