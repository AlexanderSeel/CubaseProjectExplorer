namespace CubaseInspector.Core;

public sealed record StudioIoNode(string Id, string Name, string Kind, string Description,
    string Source, long? SourceOffset = null, IReadOnlyList<string>? StoredFields = null);
public sealed record StudioIoLink(string From, string To, string Label, string Source, long? SourceOffset = null);
public sealed record StudioIoDiagram(IReadOnlyList<StudioIoNode> Nodes, IReadOnlyList<StudioIoLink> Links,
    int AudioPaths, int MidiNotes, int SavedPorts);

/// <summary>Studio patchbay data from explicit saved references, port descriptions and record containment.</summary>
public static class StudioIoDiagramBuilder
{
    public static StudioIoDiagram Build(ProjectSnapshot project)
    {
        var routing = CprSignalFlowPresentation.Create(project);
        var nodes = new Dictionary<string, StudioIoNode>(StringComparer.Ordinal);
        var links = new List<StudioIoLink>();
        foreach (var channel in RoutingFocusBuilder.Channels(routing))
        {
            var observation = project.NativeMixerObservations?.FirstOrDefault(item => item.ArchiveOffset == channel.Track?.SourceOffset);
            nodes[channel.Id] = new(channel.Id, channel.Name, channel.Track is null ? "Bus reference" : "Channel",
                observation is null ? channel.Kind : $"Volume {observation.VolumeDb?.ToString("0.##") ?? "—"} dB · mute {observation.Mute?.ToString() ?? "—"} · solo {observation.Solo?.ToString() ?? "—"}",
                observation?.SourceStream ?? channel.Track?.SourceRecordClass ?? "Saved routing", channel.Track?.SourceOffset,
                observation?.CandidateFields?.Select(field => $"{field.Path} = {field.Value} [{field.Type}] · 0x{field.ArchiveOffset:X}").ToArray());
        }
        foreach (var route in routing.Routing)
            links.Add(new(route.FromChannelId, route.ToChannelId, route.Kind, route.SourcePath ?? "Declared channel route", route.SourceOffset));

        var savedPorts = 0;
        foreach (var observation in project.NativeMixerObservations ?? [])
        {
            var owner = routing.Tracks.FirstOrDefault(track => track.SourceOffset == observation.ArchiveOffset)?.ChannelId;
            if (owner is null) continue;
            foreach (var field in observation.CandidateFields ?? [])
            {
                if (field.Type != "string" || !(field.Path.StartsWith("OwnInputBus/Connections[", StringComparison.OrdinalIgnoreCase) ||
                    field.Path.StartsWith("OutputBus/Connections[", StringComparison.OrdinalIgnoreCase))) continue;
                var parts = field.Value.Split('|', 3);
                var direction = parts.Length == 3 ? parts[0] : "";
                var id = $"saved-port:{observation.ArchiveOffset:X}:{field.ArchiveOffset:X}";
                var host = field.Path.StartsWith("OutputBus/", StringComparison.OrdinalIgnoreCase)
                    ? routing.Routing.FirstOrDefault(edge => edge.FromChannelId == owner && edge.Kind == "Output")?.ToChannelId ?? owner : owner;
                nodes[id] = new(id, parts.Length == 3 ? parts[2] : field.Value,
                    direction == "I" ? "Saved input port" : direction == "O" ? "Saved output port" : "Saved port description",
                    parts.Length == 3 ? parts[1] : "Stored connection string", observation.SourceStream + "/" + field.Path,
                    field.ArchiveOffset, [field.Value]);
                if (direction == "I") links.Add(new(id, host, "Saved input assignment", field.Path, field.ArchiveOffset));
                else if (direction == "O") links.Add(new(host, id, "Saved output assignment", field.Path, field.ArchiveOffset));
                savedPorts++;
            }
        }

        var tracksById = project.Tracks.GroupBy(track => track.Id).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First());
        var mediaIndex = 0;
        foreach (var media in (project.MediaReferences ?? []).Take(2000))
        {
            var id = "media:" + mediaIndex++;
            nodes[id] = new(id, Path.GetFileName(media.Path), "Audio media", media.Path,
                media.External == false ? "Embedded project media" : "Declared media reference");
            if (media.TrackId is not null && tracksById.TryGetValue(media.TrackId, out var track) && track.ChannelId is { } channel && nodes.ContainsKey(channel))
                links.Add(new(id, channel, "Declared audio reference", "Project media TrackId"));
        }
        foreach (var media in (project.NativeAudioMediaCandidates ?? []).Take(2000))
        {
            var id = "native-media:" + media.FileOffset;
            nodes[id] = new(id, Path.GetFileName(media.DeclaredPath), "Audio path", media.DeclaredPath,
                media.SourceRecord, media.FileOffset, [media.DeclaredPath, "Track record context: " + (media.CandidateTrackName ?? "Not decoded")]);
        }
        foreach (var group in (project.NativeMidiNoteCandidates ?? []).GroupBy(note => (note.CandidateTrackOffset, note.CandidatePartOffset)).Take(2000))
        {
            var first = group.First();
            var id = $"native-midi:{group.Key.CandidateTrackOffset}:{group.Key.CandidatePartOffset}";
            nodes[id] = new(id, first.CandidatePartName ?? first.CandidateTrackName ?? "MIDI note stream", "MIDI",
                $"{group.Count():N0} notes · pitch {group.Min(note => note.MidiPitch)}–{group.Max(note => note.MidiPitch)} · stored timing",
                first.SourceLayout, first.FileOffset);
        }
        foreach (var group in (project.MidiNotes ?? []).GroupBy(note => note.TrackId).Take(2000))
        {
            var id = "midi:" + group.Key;
            tracksById.TryGetValue(group.Key, out var track);
            nodes[id] = new(id, track?.Name ?? group.Key, "MIDI", $"{group.Count():N0} exported notes", "Declared MIDI track ownership");
            if (track?.ChannelId is { } channel && nodes.ContainsKey(channel)) links.Add(new(id, channel, "Declared MIDI ownership", "MIDI TrackId"));
        }
        return new(nodes.Values.ToArray(), links, (project.MediaReferences?.Count ?? 0) + (project.NativeAudioMediaCandidates?.Count ?? 0),
            (project.MidiNotes?.Count ?? 0) + (project.NativeMidiNoteCandidates?.Count ?? 0), savedPorts);
    }
}
