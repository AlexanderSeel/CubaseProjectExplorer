namespace CubaseInspector.Core;

/// <summary>Separates lane records from arrangement tracks, preserving the evidence for their owner.</summary>
public static class ProjectAutomation
{
    public static bool IsLane(TrackInfo track) => track.SourceRecordClass == "MAutomationTrackEvent" ||
        track.Kind.Equals("Automation", StringComparison.OrdinalIgnoreCase);

    public static ProjectSnapshot Normalize(ProjectSnapshot project)
    {
        var laneTracks = project.Tracks.Where(IsLane).ToArray();
        if (laneTracks.Length == 0) return project;
        var tracks = project.Tracks.Where(track => !IsLane(track)).ToArray();
        var byId = tracks.GroupBy(track => track.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        // CPR traversal itself caps track hints at 4096. Refuse containment
        // inference above that bound instead of truncating possible owners.
        var candidates = tracks.Where(track => track.SourceOffset is >= 0 && track.SourceEndOffset > track.SourceOffset)
            .Take(4097).ToArray();
        if (candidates.Length > 4096) candidates = [];
        var lanes = new List<AutomationLaneInfo>(project.AutomationLanes ?? []);
        foreach (var lane in laneTracks)
        {
            string? owner = null;
            var source = "Owner not decoded; lane retained separately from tracks";
            if (lane.Evidence == EvidenceLevel.Confirmed && lane.ParentTrackId is { } parent && byId.ContainsKey(parent))
            {
                owner = parent;
                source = "Declared parent track ID in project export";
            }
            else if (lane.Evidence == EvidenceLevel.Candidate && lane.SourceOffset is long start &&
                     lane.SourceEndOffset is long end && start >= 0 && end > start)
            {
                var enclosing = candidates.Where(track => track.SourceOffset < start && track.SourceEndOffset >= end &&
                        track.Evidence == EvidenceLevel.Candidate && track.SourceRecordClass != "CPR named record")
                    .OrderBy(track => track.SourceEndOffset!.Value - track.SourceOffset!.Value).Take(2).ToArray();
                if (enclosing.Length > 0 && (enclosing.Length == 1 ||
                    enclosing[0].SourceEndOffset!.Value - enclosing[0].SourceOffset!.Value <
                    enclosing[1].SourceEndOffset!.Value - enclosing[1].SourceOffset!.Value) &&
                    byId.ContainsKey(enclosing[0].Id))
                {
                    owner = enclosing[0].Id;
                    source = "Candidate owner from exact enclosing CPR track byte extent; automation target semantics unverified";
                }
            }
            lanes.Add(new(lane.Id, lane.Name, owner, lane.Evidence, source, lane.SourceOffset, lane.SourceRecordClass, lane.SourceEndOffset));
        }
        return project with { Tracks = tracks, AutomationLanes = lanes.AsReadOnly() };
    }
}
