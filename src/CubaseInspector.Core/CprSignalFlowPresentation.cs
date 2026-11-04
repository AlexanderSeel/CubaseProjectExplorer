using System.Globalization;

namespace CubaseInspector.Core;

/// <summary>A read-only view of CPR named records and explicit bus references. Does not assign records by name.</summary>
public static class CprSignalFlowPresentation
{
    private readonly record struct BusKey(string Stream, long? ArchiveOffset, uint Uid);

    public static ProjectSnapshot Create(ProjectSnapshot project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project = ProjectAutomation.Normalize(project);
        if (project.Evidence == EvidenceLevel.Confirmed) return project;
        var records = (project.NativeMixerObservations ?? []).Take(4096)
            .Select((record, index) => (Record: record, Id: $"cpr-mixer-{index}-{record.ArchiveOffset:X}"))
            .ToArray();
        if (records.Length == 0)
            return project with { Tracks = project.Tracks.Select(track => track with
                { ChannelId = track.ChannelId ?? "cpr-observation-" + track.Id }).ToArray() };

        var tracks = records.Select(item =>
        {
            var candidateFields = item.Record.CandidateFields ?? [];
            var busType = candidateFields.FirstOrDefault(field =>
                field.Path.Equals("Bus Type", StringComparison.OrdinalIgnoreCase));
            var isBus = busType is not null && candidateFields.Any(IsBusUid);
            var lastFieldOffset = candidateFields.Count == 0 ? item.Record.ArchiveOffset
                : candidateFields.Max(field => field.ArchiveOffset);
            var owners = project.Tracks.Where(track => track.Evidence == EvidenceLevel.Candidate &&
                track.SourceOffset is long start && track.SourceEndOffset is long end &&
                item.Record.ArchiveOffset >= start && lastFieldOffset < end)
                .OrderBy(track => track.SourceEndOffset!.Value - track.SourceOffset!.Value).ToArray();
            var parent = owners.Length > 0 && (owners.Length == 1 ||
                owners[0].SourceEndOffset!.Value - owners[0].SourceOffset!.Value <
                owners[1].SourceEndOffset!.Value - owners[1].SourceOffset!.Value)
                ? owners[0].Id : null;
            var kind = isBus ? "Bus" : "Mixer record";
            return new TrackInfo(item.Id, item.Record.Name, kind, item.Id,
                null, null, item.Record.Mute, item.Record.Solo, EvidenceLevel.Candidate,
                "CPR named record", item.Record.ArchiveOffset, ParentTrackId: parent, Pan: item.Record.Pan);
        }).ToArray();
        var owners = new Dictionary<BusKey, HashSet<string>>();
        var busDefinitions = new Dictionary<BusKey, HashSet<string>>();
        foreach (var item in records)
            foreach (var field in UnambiguousFields(item.Record.CandidateFields ?? []))
                if (IsBusUid(field) && TryUid(field, out var definitionUid) &&
                    (item.Record.CandidateFields ?? []).Any(candidate =>
                        candidate.Path.Equals("Bus Type", StringComparison.OrdinalIgnoreCase)))
                {
                    var key = new BusKey(item.Record.SourceStream, item.Record.SourceArchiveOffset, definitionUid);
                    if (!busDefinitions.TryGetValue(key, out var definitions)) busDefinitions[key] = definitions = [];
                    definitions.Add(item.Id);
                }
                else if (IsOwnBus(field.Path) && TryUid(field, out var uid))
                {
                    var key = new BusKey(item.Record.SourceStream, item.Record.SourceArchiveOffset, uid);
                    if (!owners.TryGetValue(key, out var ids)) owners[key] = ids = new(StringComparer.Ordinal);
                    ids.Add(item.Id);
                }

        var edges = new List<RoutingEdge>(project.Routing);
        foreach (var item in records)
            foreach (var field in UnambiguousFields(item.Record.CandidateFields ?? []))
            {
                var output = field.Path.Equals("OutputBus/Value", StringComparison.OrdinalIgnoreCase) ||
                    field.Path.Equals("OutputBus/Bus UID", StringComparison.OrdinalIgnoreCase) ||
                    field.Path.Equals("OutputBusValue", StringComparison.OrdinalIgnoreCase) ||
                    field.Path.Equals("OutputBusValue/Value", StringComparison.OrdinalIgnoreCase);
                var input = field.Path.Equals("InputBusValue", StringComparison.OrdinalIgnoreCase) ||
                    field.Path.Equals("InputBusValue/Value", StringComparison.OrdinalIgnoreCase);
                var direct = field.Path.StartsWith("directRoutingFolder/", StringComparison.OrdinalIgnoreCase) &&
                    IsBusDestination(field.Path);
                var send = field.Path.StartsWith("SendFolder/", StringComparison.OrdinalIgnoreCase) &&
                    IsBusDestination(field.Path);
                var childBus = IsChildBus(field.Path);
                var recorder = IsRecorderBus(field.Path);
                if ((!input && !output && !send && !direct && !childBus && !recorder) || !TryUid(field, out var uid)) continue;
<<<<<<< main
                var key = new BusKey(item.Record.SourceStream, item.Record.SourceArchiveOffset, uid);
                var target = ResolveBus(key, busDefinitions, owners) ?? UnresolvedBusId(key);
=======
                // Cubase 15 reference collector 0x143513190 explicitly skips
                // zero for these two flat properties. Other UID shapes retain
                // their own unresolved semantics; no universal zero rule.
                if (uid == 0 && (input || field.Path.Equals("OutputBusValue", StringComparison.OrdinalIgnoreCase))) continue;
                var key = (item.Record.SourceStream, uid);
                var target = ResolveBus(key, busDefinitions, owners) ?? $"Bus UID {uid} [{item.Record.SourceStream}]";
>>>>>>> origin/main
                if (childBus && target.StartsWith("Bus UID ", StringComparison.Ordinal))
                    target = BusNodeId(key);
                var namePath = NamePathFor(field.Path);
                var names = (item.Record.CandidateFields ?? []).Where(value =>
                        value.Path.Equals(namePath, StringComparison.OrdinalIgnoreCase) && value.Type == "string")
                    .Select(value => value.Value).Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (childBus)
                    edges.Add(new(item.Id, target, "Child bus", Evidence: EvidenceLevel.Candidate,
                        SourceOffset: field.ArchiveOffset, SourcePath: item.Record.SourceStream + "/" + field.Path,
                        TargetLabel: names.Length == 1 ? names[0] : $"Child bus {uid}"));
                else if (recorder)
                    edges.Add(new(item.Id, target, "Record", Evidence: EvidenceLevel.Candidate,
                        SourceOffset: field.ArchiveOffset, SourcePath: item.Record.SourceStream + "/" + field.Path,
                        TargetLabel: names.Length == 1 ? names[0] : $"Recorder bus {uid}"));
                else
                    edges.Add(new(input ? target : item.Id, input ? item.Id : target,
                    input ? "Input" : send ? "Send" : direct ? "Direct output" : "Output", Evidence: EvidenceLevel.Candidate,
                    SourceOffset: field.ArchiveOffset, SourcePath: item.Record.SourceStream + "/" + field.Path,
                    TargetLabel: names.Length == 1 ? names[0] : null));
            }
        foreach (var item in records)
            foreach (var field in UnambiguousFields(item.Record.CandidateFields ?? []))
            {
                if (!field.Path.Equals("Parent Bus UID", StringComparison.OrdinalIgnoreCase) || !TryUid(field, out var uid)) continue;
                var key = new BusKey(item.Record.SourceStream, item.Record.SourceArchiveOffset, uid);
                var target = ResolveBus(key, busDefinitions, owners) ?? UnresolvedBusId(key);
                edges.Add(new(item.Id, target, "Parent bus", Evidence: EvidenceLevel.Candidate,
                    SourceOffset: field.ArchiveOffset, SourcePath: item.Record.SourceStream + "/" + field.Path));
            }
        return project with { Tracks = [.. project.Tracks, .. tracks], Routing = edges };
    }

    private static string? ResolveBus(BusKey key,
        IReadOnlyDictionary<BusKey, HashSet<string>> definitions,
        IReadOnlyDictionary<BusKey, HashSet<string>> owners)
    {
        if (definitions.TryGetValue(key, out var declared))
            return declared.Count == 1 ? declared.Single() : null;
        return owners.TryGetValue(key, out var matches) && matches.Count == 1 ? matches.Single() : null;
    }

    private static IEnumerable<CprNamedRecordValue> UnambiguousFields(IReadOnlyList<CprNamedRecordValue> fields) =>
        fields.GroupBy(field => field.Path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.All(field => field.Type == group.First().Type && field.Value == group.First().Value))
            .Select(group => group.First());

    private static bool IsBusUid(CprNamedRecordValue field) =>
        field.Path.Equals("Bus UID", StringComparison.OrdinalIgnoreCase) ||
        field.Path.Equals("Bus UID/Value", StringComparison.OrdinalIgnoreCase);

    private static bool IsOwnBus(string path) =>
        path.Equals("OwnInputBus/Bus UID", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("OwnInputBus/Bus UID/Value", StringComparison.OrdinalIgnoreCase);

    private static bool IsRecorderBus(string path) =>
        path.Equals("RecorderBus/Bus UID", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("RecorderBus/Bus UID/Value", StringComparison.OrdinalIgnoreCase);

    private static bool IsChildBus(string path) =>
        (path.StartsWith("OwnInputBus/Child Bus/", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith("OwnInputBus/Child Bus[", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith("RecorderBus/Child Bus/", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith("RecorderBus/Child Bus[", StringComparison.OrdinalIgnoreCase)) &&
        (path.EndsWith("/Bus UID", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith("/Bus UID/Value", StringComparison.OrdinalIgnoreCase));

    private static bool IsBusDestination(string path) =>
        path.EndsWith("/Output/Value", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("/Output/Bus UID", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("/OutputBus/Value", StringComparison.OrdinalIgnoreCase);

    private static string NamePathFor(string path)
    {
        var childBus = path.IndexOf("/Bus UID", StringComparison.OrdinalIgnoreCase);
        if (childBus >= 0) return path[..childBus] + "/Name";
        var output = path.LastIndexOf("/Output", StringComparison.OrdinalIgnoreCase);
        if (output >= 0) return path[..output] + "/Output/Name";
        var separator = path.LastIndexOf('/');
        return separator >= 0 ? path[..separator] + "/Name" : path + "/Name";
    }

    private static string BusNodeId(BusKey key) =>
        $"cpr-bus:{Uri.EscapeDataString(key.Stream)}:{key.Uid}" +
        (key.ArchiveOffset is long offset ? $":archive-{offset:X}" : "");

    private static string UnresolvedBusId(BusKey key) =>
        $"Bus UID {key.Uid} [{key.Stream}" +
        (key.ArchiveOffset is long offset ? $" @0x{offset:X}]" : "]");

    private static bool TryUid(CprNamedRecordValue field, out uint uid)
    {
        uid = 0;
        if (field.Type is not ("i64" or "u32" or "string")) return false;
        // The native connection inventory explicitly excludes zero selections.
        // Keep their stored fields, without turning unused slots into edges.
        return uint.TryParse(field.Value, NumberStyles.None, CultureInfo.InvariantCulture, out uid) && uid != 0;
    }
}
