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

        var tracks = records.Select(item => new TrackInfo(item.Id, item.Record.Name, "Mixer record", item.Id,
            null, null, item.Record.Mute, item.Record.Solo, EvidenceLevel.Candidate,
            "CPR named record", item.Record.ArchiveOffset, Pan: item.Record.Pan)).ToArray();
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
                    field.Path.Equals("OutputBus/Bus UID", StringComparison.OrdinalIgnoreCase);
                var send = field.Path.StartsWith("SendFolder/", StringComparison.OrdinalIgnoreCase) &&
                    (field.Path.EndsWith("/Output/Value", StringComparison.OrdinalIgnoreCase) ||
                     field.Path.EndsWith("/Output/Bus UID", StringComparison.OrdinalIgnoreCase) ||
                     field.Path.EndsWith("/OutputBus/Value", StringComparison.OrdinalIgnoreCase));
                if ((!output && !send) || !TryUid(field, out var uid)) continue;
                var key = (item.Record.SourceStream, uid);
                var target = owners.TryGetValue(key, out var matches) && matches.Count == 1
                    ? matches.Single() : $"Bus UID {uid} [{item.Record.SourceStream}]";
                var namePath = field.Path[..field.Path.LastIndexOf('/')] + "/Name";
                var names = (item.Record.CandidateFields ?? []).Where(value =>
                        value.Path.Equals(namePath, StringComparison.OrdinalIgnoreCase) && value.Type == "string")
                    .Select(value => value.Value).Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal).ToArray();
                edges.Add(new(item.Id, target, send ? "Send" : "Output", Evidence: EvidenceLevel.Candidate,
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
