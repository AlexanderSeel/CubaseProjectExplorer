using System.Globalization;

namespace CubaseInspector.Core;

/// <summary>A read-only view of CPR named records and explicit bus references. Does not assign records by name.</summary>
public static class CprSignalFlowPresentation
{
    public static ProjectSnapshot Create(ProjectSnapshot project)
    {
        ArgumentNullException.ThrowIfNull(project);
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
            return new TrackInfo(item.Id, item.Record.Name, "Mixer record", item.Id,
                null, null, item.Record.Mute, item.Record.Solo, EvidenceLevel.Candidate,
                "CPR named record", item.Record.ArchiveOffset, ParentTrackId: parent, Pan: item.Record.Pan);
        }).ToArray();
        var owners = new Dictionary<(string Stream, uint Uid), HashSet<string>>();
        foreach (var item in records)
            foreach (var field in item.Record.CandidateFields ?? [])
                if (IsOwnBus(field.Path) && TryUid(field, out var uid))
                {
                    var key = (item.Record.SourceStream, uid);
                    if (!owners.TryGetValue(key, out var ids)) owners[key] = ids = new(StringComparer.Ordinal);
                    ids.Add(item.Id);
                }

        var edges = new List<RoutingEdge>(project.Routing);
        foreach (var item in records)
            foreach (var field in item.Record.CandidateFields ?? [])
            {
                var output = field.Path.Equals("OutputBus/Value", StringComparison.OrdinalIgnoreCase) ||
                    field.Path.Equals("OutputBus/Bus UID", StringComparison.OrdinalIgnoreCase) ||
                    field.Path.Equals("OutputBusValue", StringComparison.OrdinalIgnoreCase);
                var input = field.Path.Equals("InputBusValue", StringComparison.OrdinalIgnoreCase);
                var send = field.Path.StartsWith("SendFolder/", StringComparison.OrdinalIgnoreCase) &&
                    (field.Path.EndsWith("/Output/Value", StringComparison.OrdinalIgnoreCase) ||
                     field.Path.EndsWith("/Output/Bus UID", StringComparison.OrdinalIgnoreCase) ||
                     field.Path.EndsWith("/OutputBus/Value", StringComparison.OrdinalIgnoreCase));
                if ((!input && !output && !send) || !TryUid(field, out var uid)) continue;
                var key = (item.Record.SourceStream, uid);
                var target = owners.TryGetValue(key, out var matches) && matches.Count == 1
                    ? matches.Single() : $"Bus UID {uid} [{item.Record.SourceStream}]";
                var nameSeparator = field.Path.LastIndexOf('/');
                var namePath = nameSeparator >= 0 ? field.Path[..nameSeparator] + "/Name" : field.Path + "/Name";
                var names = (item.Record.CandidateFields ?? []).Where(value =>
                        value.Path.Equals(namePath, StringComparison.OrdinalIgnoreCase) && value.Type == "string")
                    .Select(value => value.Value).Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal).ToArray();
                edges.Add(new(input ? target : item.Id, input ? item.Id : target,
                    input ? "Input" : send ? "Send" : "Output", Evidence: EvidenceLevel.Candidate,
                    SourceOffset: field.ArchiveOffset, SourcePath: item.Record.SourceStream + "/" + field.Path,
                    TargetLabel: names.Length == 1 ? names[0] : null));
            }
        return project with { Tracks = [.. project.Tracks.Where(track => !string.IsNullOrWhiteSpace(track.ChannelId)), .. tracks], Routing = edges };
    }

    private static bool IsOwnBus(string path) =>
        path.Equals("OwnInputBus/Bus UID", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("OwnInputBus/Bus UID/Value", StringComparison.OrdinalIgnoreCase);

    private static bool TryUid(CprNamedRecordValue field, out uint uid)
    {
        uid = 0;
        if (field.Type is not ("i64" or "u32" or "string")) return false;
        return uint.TryParse(field.Value, NumberStyles.None, CultureInfo.InvariantCulture, out uid);
    }
}
