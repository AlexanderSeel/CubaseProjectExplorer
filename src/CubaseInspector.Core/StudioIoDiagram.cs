namespace CubaseInspector.Core;

public sealed record StudioIoNode(string Id, string Name, string Kind, string Description,
    string Source, long? SourceOffset = null, IReadOnlyList<string>? StoredFields = null,
    EvidenceLevel Evidence = EvidenceLevel.Unknown);
public sealed record StudioIoLink(string From, string To, string Label, string Source,
    long? SourceOffset = null, EvidenceLevel Evidence = EvidenceLevel.Unknown);
public sealed record StudioIoDiagram(IReadOnlyList<StudioIoNode> Nodes, IReadOnlyList<StudioIoLink> Links,
    int AudioPaths, int MidiNotes, int SavedPorts);

/// <summary>Project flow projection. Relationships use declared IDs or exact source containment; nearby names are never joined.</summary>
public static class StudioIoDiagramBuilder
{
    public static StudioIoDiagram Build(ProjectSnapshot project)
    {
        var routing = CprSignalFlowPresentation.Create(project);
        var nodes = new Dictionary<string, StudioIoNode>(StringComparer.Ordinal);
        var links = new List<StudioIoLink>();
        var arrangementTracks = project.Tracks.Where(track => track.SourceRecordClass != "CPR named record").ToArray();
        var tracksById = arrangementTracks.GroupBy(track => track.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var tracksBySourceOffset = arrangementTracks.Where(track => track.SourceOffset is not null)
            .GroupBy(track => track.SourceOffset!.Value)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());

        foreach (var track in arrangementTracks)
        {
            var id = TrackNodeId(track.Id);
            nodes[id] = new(id, track.Name, track.Kind,
                $"{track.Evidence} track record" + (track.SourceEndOffset is long end ? $" · source extent 0x{track.SourceOffset:X}–0x{end:X}" : ""),
                track.SourceRecordClass ?? "Declared track", track.SourceOffset,
                Evidence: track.Evidence);
        }

        foreach (var channel in RoutingFocusBuilder.Channels(routing))
        {
            var observation = project.NativeMixerObservations?.FirstOrDefault(item => item.ArchiveOffset == channel.Track?.SourceOffset);
            nodes[channel.Id] = new(channel.Id, channel.Name, channel.Track is null ? "Bus reference" : channel.Kind,
                observation is null ? channel.Kind : MixerDescription(observation),
                observation?.SourceStream ?? channel.Track?.SourceRecordClass ?? "Saved routing", channel.Track?.SourceOffset,
                observation?.CandidateFields?.Select(FormatField).ToArray(), channel.Track?.Evidence ?? EvidenceLevel.Unknown);
        }

        foreach (var record in routing.Tracks.Where(track => track.SourceRecordClass == "CPR named record" && track.ParentTrackId is not null))
        {
            var parentId = TrackNodeId(record.ParentTrackId!);
            if (nodes.ContainsKey(parentId) && record.ChannelId is { } channel && nodes.ContainsKey(channel))
                links.Add(new(parentId, channel, "Mixer record inside track object", "CPR object containment · exact byte extent",
                    record.SourceOffset, EvidenceLevel.Candidate));
        }

        foreach (var track in arrangementTracks.Where(track => track.Evidence == EvidenceLevel.Confirmed && track.ChannelId is not null))
        {
            var channel = track.ChannelId!;
            var ownedPlugins = project.Plugins.Select((plugin, index) => (plugin, index))
                .Where(item => item.plugin.Evidence == EvidenceLevel.Confirmed && item.plugin.TrackId == track.Id)
                .OrderBy(item => item.plugin.Slot ?? int.MaxValue).ThenBy(item => item.index).ToArray();
            var previous = TrackNodeId(track.Id);
            foreach (var (plugin, index) in ownedPlugins)
            {
                var id = $"plugin:{track.Id}:{index}";
                nodes[id] = new(id, plugin.Name, plugin.Role ?? "Plug-in",
                    $"{plugin.Format} · slot {plugin.Slot?.ToString() ?? "not declared"}" + (plugin.Enabled is bool enabled ? $" · {(enabled ? "enabled" : "bypassed")}" : ""),
                    "Declared plug-in TrackId and slot", plugin.SourceByteOffset, Evidence: EvidenceLevel.Confirmed);
                links.Add(new(previous, id, $"Insert slot {plugin.Slot?.ToString() ?? "?"}",
                    "Project plug-in TrackId", plugin.SourceByteOffset, EvidenceLevel.Confirmed));
                previous = id;
            }
            if (nodes.ContainsKey(channel))
                links.Add(new(previous, channel, ownedPlugins.Length == 0 ? "Track channel ID" : "Channel after declared insert chain",
                    "Project track ChannelId", Evidence: EvidenceLevel.Confirmed));
        }

        var signalOutputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var observation in project.NativeMixerObservations ?? [])
        {
            var owner = routing.Tracks.FirstOrDefault(track => track.SourceOffset == observation.ArchiveOffset)?.ChannelId;
            if (owner is null || !nodes.ContainsKey(owner)) continue;

            var insertFields = (observation.CandidateFields ?? []).Where(field =>
                    field.Path.StartsWith("InsertFolder/", StringComparison.OrdinalIgnoreCase) &&
                    field.Path.EndsWith("/Plugin Name", StringComparison.OrdinalIgnoreCase) &&
                    field.Type == "string" && !string.IsNullOrWhiteSpace(field.Value))
                .GroupBy(field => field.Path[..^"/Plugin Name".Length], StringComparer.Ordinal)
                .Select(group => group.First()).Select(field => (Field: field, Slot: SlotIndex(field.Path)))
                .OrderBy(item => item.Slot ?? int.MaxValue).ThenBy(item => item.Field.ArchiveOffset).ToArray();
            var insertOrderKnown = insertFields.All(item => item.Slot is not null) &&
                insertFields.Select(item => item.Slot!.Value).Distinct().Count() == insertFields.Length;
            string? previousInsert = null;
            foreach (var (field, slot) in insertFields)
            {
                var id = $"native-insert:{observation.ArchiveOffset:X}:{field.ArchiveOffset:X}";
                nodes[id] = new(id, field.Value, "Mixer insert",
                    $"Insert slot {slot?.ToString() ?? "not decoded"} · plug-in name stored in the same InsertFolder record",
                    observation.SourceStream + "/" + field.Path, field.ArchiveOffset,
                    FieldsForPath(observation, field.Path[..^"/Plugin Name".Length]), EvidenceLevel.Candidate);
                links.Add(new(insertOrderKnown ? previousInsert ?? owner : owner, id, $"Insert slot {slot?.ToString() ?? "?"}",
                    observation.SourceStream + "/" + field.Path, field.ArchiveOffset, EvidenceLevel.Candidate));
                previousInsert = id;
            }

            var outputId = ChannelOutputId(owner);
            nodes[outputId] = new(outputId, nodes[owner].Name + " · signal out", "Channel output",
                insertFields.Length == 0 ? "Signal leaves this saved channel record" : insertOrderKnown
                    ? $"After {insertFields.Length:N0} stored insert slot(s)" : $"{insertFields.Length:N0} insert name(s); stored processing order is incomplete",
                observation.SourceStream + "/channel output stage", observation.ArchiveOffset,
                Evidence: EvidenceLevel.Candidate);
            links.Add(new(insertOrderKnown ? previousInsert ?? owner : owner, outputId,
                insertFields.Length == 0 ? "Channel signal output" : insertOrderKnown ? "Signal after stored insert slots" : "Channel signal output; insert order unknown",
                observation.SourceStream + "/InsertFolder", observation.ArchiveOffset, EvidenceLevel.Candidate));
            signalOutputs[owner] = outputId;

            foreach (var field in (observation.CandidateFields ?? []).Where(field =>
                         field.Path.Equals("postInsertsPreFader", StringComparison.OrdinalIgnoreCase)))
            {
                var id = $"native-insert-position:{observation.ArchiveOffset:X}:{field.ArchiveOffset:X}";
                nodes[id] = new(id, "postInsertsPreFader", "Insert placement setting",
                    $"Stored value: {field.Value} [{field.Type}] · Cubase reads this property after InsertFolder; exact routing/tap semantics are not verified",
                    observation.SourceStream + "/" + field.Path, field.ArchiveOffset, [FormatField(field)], EvidenceLevel.Candidate);
                links.Add(new(owner, id, "Saved insert placement property", observation.SourceStream + "/" + field.Path,
                    field.ArchiveOffset, EvidenceLevel.Candidate));
            }

            var eqFields = (observation.CandidateFields ?? []).Where(field =>
                field.Path.StartsWith("EQ/", StringComparison.OrdinalIgnoreCase) ||
                field.Path.StartsWith("ChannelEQ/", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (eqFields.Length > 0)
            {
                var id = $"native-eq:{observation.ArchiveOffset:X}";
                nodes[id] = new(id, "Channel EQ", "Saved EQ settings",
                    $"{eqFields.Length:N0} typed EQ values · exact saved names and values shown in source details",
                    observation.SourceStream + "/EQ", eqFields.Min(field => field.ArchiveOffset),
                    eqFields.Select(FormatField).ToArray(), EvidenceLevel.Candidate);
                links.Add(new(owner, id, "Channel EQ fields", "Nested EQ records in mixer observation",
                    eqFields.Min(field => field.ArchiveOffset), EvidenceLevel.Candidate));
            }
        }

        foreach (var channel in RoutingFocusBuilder.Channels(routing))
        {
            if (signalOutputs.ContainsKey(channel.Id)) continue;
            var id = ChannelOutputId(channel.Id);
            nodes[id] = new(id, channel.Name + " · signal out", "Channel output",
                "Signal leaves this channel; no insert order was decoded for this node",
                channel.Track?.SourceRecordClass ?? "Saved channel", channel.Track?.SourceOffset,
                Evidence: channel.Track?.Evidence ?? EvidenceLevel.Unknown);
            links.Add(new(channel.Id, id, "Channel signal output",
                channel.Track?.SourceRecordClass ?? "Saved channel stage", channel.Track?.SourceOffset,
                channel.Track?.Evidence ?? EvidenceLevel.Unknown));
            signalOutputs[channel.Id] = id;
        }

        foreach (var route in routing.Routing)
            links.Add(new(signalOutputs.GetValueOrDefault(route.FromChannelId, route.FromChannelId), route.ToChannelId, route.Kind,
                route.SourcePath ?? "Declared channel route", route.SourceOffset, route.Evidence));

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
                    field.ArchiveOffset, [field.Value], EvidenceLevel.Candidate);
                if (direction == "I") links.Add(new(id, host, "Saved input assignment", field.Path, field.ArchiveOffset, EvidenceLevel.Candidate));
                else if (direction == "O") links.Add(new(host, id, "Saved output assignment", field.Path, field.ArchiveOffset, EvidenceLevel.Candidate));
                savedPorts++;
            }
        }

        var mediaIndex = 0;
        foreach (var media in (project.MediaReferences ?? []).Take(2000))
        {
            var id = "media:" + mediaIndex++;
            nodes[id] = new(id, Path.GetFileName(media.Path), "Audio media", media.Path,
                media.External == false ? "Embedded project media" : "Declared media reference", Evidence: EvidenceLevel.Confirmed);
            if (media.TrackId is not null && tracksById.TryGetValue(media.TrackId, out var track))
                links.Add(new(id, TrackNodeId(track.Id), "Declared audio reference", "Project media TrackId", Evidence: EvidenceLevel.Confirmed));
        }
        foreach (var media in (project.NativeAudioMediaCandidates ?? []).Take(2000))
        {
            var id = "native-media:" + media.FileOffset;
            nodes[id] = new(id, Path.GetFileName(media.DeclaredPath), "Audio path", media.DeclaredPath,
                media.SourceRecord, media.FileOffset, [media.DeclaredPath, "Enclosing track offset: " + (media.CandidateTrackOffset is long trackOffset ? $"0x{trackOffset:X}" : "not read")],
                EvidenceLevel.Candidate);
            if (media.CandidateTrackOffset is long offset && tracksBySourceOffset.TryGetValue(offset, out var track))
                links.Add(new(id, TrackNodeId(track.Id), "Audio event inside track object", "Exact enclosing track byte offset", media.FileOffset, EvidenceLevel.Candidate));
        }
        foreach (var group in (project.NativeMidiNoteCandidates ?? []).GroupBy(note =>
                     (CandidateTrackOffset: note.CandidateTrackOffset, CandidatePartOffset: note.CandidatePartOffset,
                         UnassignedLayout: note.CandidateTrackOffset is null && note.CandidatePartOffset is null ? note.SourceLayout : "")).Take(2000))
        {
            var first = group.First();
            var id = $"native-midi:{project.FilePath}:{group.Key.CandidateTrackOffset}:{group.Key.CandidatePartOffset}:{group.Key.UnassignedLayout}";
            nodes[id] = new(id, first.CandidatePartName ?? first.CandidateTrackName ?? "MIDI part", "Native MIDI part",
                $"{group.Count():N0} notes · pitch {group.Min(note => note.MidiPitch)}–{group.Max(note => note.MidiPitch)} · stored timing",
                first.SourceLayout, first.FileOffset,
                [$"Track label: {first.CandidateTrackName ?? "not found"}", $"Part offset: {first.CandidatePartOffset?.ToString("X") ?? "not available"}"],
                EvidenceLevel.Candidate);
            if (first.CandidateTrackOffset is long offset && tracksBySourceOffset.TryGetValue(offset, out var track))
                links.Add(new(id, TrackNodeId(track.Id), "Notes nested under track object", "Exact enclosing track byte offset", first.FileOffset, EvidenceLevel.Candidate));
        }
        foreach (var group in (project.MidiNotes ?? []).GroupBy(note => note.TrackId).Take(2000))
        {
            if (!tracksById.TryGetValue(group.Key, out var track)) continue;
            var id = "midi:" + group.Key;
            nodes[id] = new(id, track.Name, "Declared MIDI events", $"{group.Count():N0} notes", "MIDI event TrackId", Evidence: EvidenceLevel.Confirmed);
            links.Add(new(id, TrackNodeId(track.Id), "MIDI events for track", "Declared MIDI TrackId", Evidence: EvidenceLevel.Confirmed));
        }

        return new(nodes.Values.ToArray(), links, (project.MediaReferences?.Count ?? 0) + (project.NativeAudioMediaCandidates?.Count ?? 0),
            (project.MidiNotes?.Count ?? 0) + (project.NativeMidiNoteCandidates?.Count ?? 0), savedPorts);
    }

    private static string TrackNodeId(string id) => "track:" + id;
    private static string ChannelOutputId(string id) => "channel-output:" + id;
    private static string MixerDescription(CprMixerObservation observation)
    {
        var fields = observation.CandidateFields ?? [];
        var inserts = fields.Count(field => field.Path.StartsWith("InsertFolder/", StringComparison.OrdinalIgnoreCase) &&
            field.Path.EndsWith("/Plugin Name", StringComparison.OrdinalIgnoreCase));
        var eq = fields.Count(field => field.Path.StartsWith("EQ/", StringComparison.OrdinalIgnoreCase) ||
            field.Path.StartsWith("ChannelEQ/", StringComparison.OrdinalIgnoreCase));
        return $"Volume {observation.VolumeDb?.ToString("0.##") ?? "—"} dB · {inserts:N0} insert names · {eq:N0} EQ values · mute {observation.Mute?.ToString() ?? "—"} · solo {observation.Solo?.ToString() ?? "—"}";
    }

    private static string FormatField(CprNamedRecordValue field) =>
        $"{field.Path} = {field.Value} [{field.Type}] · 0x{field.ArchiveOffset:X}";

    private static IReadOnlyList<string> FieldsForPath(CprMixerObservation observation, string path) =>
        (observation.CandidateFields ?? []).Where(field => field.Path.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
            .Select(FormatField).ToArray();

    private static int? SlotIndex(string path)
    {
        var marker = path.IndexOf("Slot[", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return null;
        var start = marker + 5;
        var end = path.IndexOf(']', start);
        return end > start && int.TryParse(path.AsSpan(start, end - start), out var slot) && slot >= 0 ? slot : null;
    }
}
