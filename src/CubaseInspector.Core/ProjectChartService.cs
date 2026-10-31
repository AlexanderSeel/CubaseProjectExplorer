namespace CubaseInspector.Core;

public sealed record ProjectChartBar(
    string Label, int Count, double Percentage, EvidenceLevel Evidence, string CountLabel);

public sealed record ProjectChartSummary(
    IReadOnlyList<ProjectChartBar> TrackTypes, IReadOnlyList<ProjectChartBar> PluginFormats,
    string Explanation);

/// <summary>Evidence-aware inventory distributions; percentages are within known parsed references only.</summary>
public static class ProjectChartService
{
    public static ProjectChartSummary Build(ProjectSnapshot project)
    {
        var trackBars = Bars(project.Tracks.GroupBy(t => t.Kind, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Count: g.Count())),
            project.Evidence, project.Evidence == EvidenceLevel.Confirmed ? "tracks" : "candidate track records");

        // Raw binary CPR markers are not identified plugin instances and cannot be meaningfully
        // classified by format/manufacturer; keep the chart empty rather than fabricating ratios.
        var plugins = project.Evidence == EvidenceLevel.Confirmed
            ? Bars(project.Plugins.Where(p => p.Evidence == EvidenceLevel.Confirmed)
                .GroupBy(p => p.Format, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Count: g.Count())),
                EvidenceLevel.Confirmed, "plugin references")
            : [];
        var note = project.Evidence == EvidenceLevel.Confirmed
            ? "Counts reflect parsed DAWproject track and plugin references; local installation is not validated."
            : "CPR data shows candidate track-record counts only; plugin-marker distributions are excluded.";
        return new ProjectChartSummary(trackBars, plugins, note);
    }

    private static IReadOnlyList<ProjectChartBar> Bars(
        IEnumerable<(string Name, int Count)> source, EvidenceLevel evidence, string unit)
    {
        var sorted = source.OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var total = sorted.Sum(x => x.Count);
        return total == 0 ? [] : sorted.Select(x =>
            new ProjectChartBar(x.Name, x.Count, 100d * x.Count / total, evidence,
                $"{x.Count} {unit} ({100d * x.Count / total:0.#}%)"))
            .ToArray();
    }
}
