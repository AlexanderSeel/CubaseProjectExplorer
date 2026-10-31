namespace CubaseInspector.Core;

/// <summary>User decisions refer to positions in an exact pair of parsed snapshots.</summary>
public sealed record TrackMatchDecision(int BeforeIndex, int? AfterIndex);

public static class ProjectTrackMatching
{
    public static IReadOnlyList<TrackMatchDecision> Suggest(ProjectSnapshot before, ProjectSnapshot after)
    {
        static string Key(TrackInfo track) => string.Join("\u001f", track.Name, track.Kind);
        var oldGroups = before.Tracks.GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var newGroups = Enumerable.Range(0, after.Tracks.Count).GroupBy(i => Key(after.Tracks[i]), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return before.Tracks.Select((track, index) =>
        {
            var key = Key(track);
            var candidates = newGroups.GetValueOrDefault(key) ?? [];
            return new TrackMatchDecision(index, oldGroups[key] == 1 && candidates.Length == 1 ? candidates[0] : null);
        }).ToArray();
    }

    public static void Validate(ProjectSnapshot before, ProjectSnapshot after, IReadOnlyList<TrackMatchDecision> decisions)
    {
        if (decisions.Count != before.Tracks.Count ||
            decisions.Select(decision => decision.BeforeIndex).Distinct().Count() != before.Tracks.Count ||
            decisions.Any(decision => decision.BeforeIndex < 0 || decision.BeforeIndex >= before.Tracks.Count ||
                decision.AfterIndex is int index && (index < 0 || index >= after.Tracks.Count)))
            throw new ArgumentException("Each project A track needs one valid matching decision.");
        var targets = decisions.Where(decision => decision.AfterIndex.HasValue).Select(decision => decision.AfterIndex!.Value).ToArray();
        if (targets.Distinct().Count() != targets.Length)
            throw new ArgumentException("A project B track can match only one project A track. Choose another track or leave it unmatched.");
    }
}
