using System.Text;
namespace CubaseInspector.Core;

public enum LocalPluginEvidence { NotChecked, FilenameMatchOnly, Unverified, ModuleClassIdMatch }

public sealed record PluginDependency(
    string Id, string Name, string? Manufacturer, string Format, string? Version,
    int Instances, IReadOnlyList<string> Tracks, LocalPluginEvidence LocalEvidence, string? FilenameMatchPath);

public sealed record PreflightReport(string ProjectName, EvidenceLevel SourceEvidence,
    IReadOnlyList<PluginDependency> Plugins, int UnresolvedCandidateMarkers, IReadOnlyList<string> Warnings);

/// <summary>Read-only preflight: filesystem filename matches never prove installed VST class identity.</summary>
public sealed class ProjectPreflightService
{
    public PreflightReport Analyze(ProjectSnapshot snapshot,
        IReadOnlyList<PluginCheck>? localChecks = null, string? localScanError = null)
    {
        var warnings = snapshot.Warnings.ToList();
        var candidates = snapshot.Plugins.Count(p => p.Evidence != EvidenceLevel.Confirmed);
        if (snapshot.Evidence != EvidenceLevel.Confirmed)
            warnings.Add("Native CPR structure is unverified: binary markers are not actionable plugin dependencies.");
        if (localChecks is null)
            warnings.Add("Installed-plugin inventory was not checked; availability cannot be determined.");
        else
            warnings.Add("Local bundle moduleinfo.json CIDs are checked where present; filename fallback is unverified identity. A class match is not proof of loadability, architecture, binary integrity or version compatibility.");
        if (!string.IsNullOrWhiteSpace(localScanError))
            warnings.Add("Local plugin scan failed: " + localScanError);

        var trackNames = snapshot.Tracks.GroupBy(t => t.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var checks = localChecks?.ToLookup(c => c.Reference);
        PluginDependency[] dependencies = [];
        if (snapshot.Evidence == EvidenceLevel.Confirmed)
            dependencies = snapshot.Plugins.Where(p => p.Evidence == EvidenceLevel.Confirmed)
                .GroupBy(p => new { p.Id, p.Name, p.Manufacturer, p.Format, p.Version })
                .Select(group =>
                {
                    var matches = checks is null ? Array.Empty<PluginCheck>()
                        : group.SelectMany(p => checks[p]).ToArray();
                    var hit = matches.FirstOrDefault(c => c.Status == PluginStatus.InstalledByClassId)
                        ?? matches.FirstOrDefault(c => c.Status == PluginStatus.InstalledByName);
                    var evidence = localChecks is null ? LocalPluginEvidence.NotChecked
                        : hit?.Status == PluginStatus.InstalledByClassId ? LocalPluginEvidence.ModuleClassIdMatch
                        : hit?.Status == PluginStatus.InstalledByName ? LocalPluginEvidence.FilenameMatchOnly
                        : LocalPluginEvidence.Unverified;
                    var tracks = group.Select(p => p.TrackId is null ? "Unassigned" :
                        trackNames.GetValueOrDefault(p.TrackId, "Unknown track: " + p.TrackId))
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
                    return new PluginDependency(group.Key.Id, group.Key.Name, group.Key.Manufacturer,
                        group.Key.Format, group.Key.Version, group.Count(), tracks, evidence, hit?.MatchedPath);
                }).OrderByDescending(p => p.Instances).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        return new PreflightReport(snapshot.Name, snapshot.Evidence, dependencies, candidates, warnings);
    }

    public string Markdown(PreflightReport report)
    {
        var builder = new StringBuilder("# Plugin dependency preflight").AppendLine().AppendLine()
            .AppendLine("- Project: " + Md(report.ProjectName))
            .AppendLine("- Evidence: " + report.SourceEvidence)
            .AppendLine("- Confirmed plugin identities: " + report.Plugins.Count)
            .AppendLine("- Unresolved candidate markers: " + report.UnresolvedCandidateMarkers)
            .AppendLine().AppendLine("| Plugin | ID | Vendor | Format | Version | Instances | Tracks | Local status |")
            .AppendLine("|---|---|---|---|---|---:|---|---|");
        foreach (var p in report.Plugins)
            builder.AppendLine("| " + Md(p.Name) + " | " + Md(p.Id) + " | " + Md(p.Manufacturer)
                + " | " + Md(p.Format) + " | " + Md(p.Version) + " | " + p.Instances
                + " | " + Md(string.Join(", ", p.Tracks)) + " | " + p.LocalEvidence + " |");
        builder.AppendLine().AppendLine("## Limitations");
        foreach (var warning in report.Warnings) builder.AppendLine("- " + Md(warning));
        return builder.ToString();
    }

    public string Csv(PreflightReport report)
    {
        var builder = new StringBuilder("Name,PluginId,Manufacturer,Format,Version,Instances,Tracks,LocalEvidence,FilenameMatchPath").AppendLine();
        foreach (var p in report.Plugins)
            builder.AppendLine(string.Join(",", new[] { p.Name, p.Id, p.Manufacturer, p.Format, p.Version,
                p.Instances.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Join("; ", p.Tracks), p.LocalEvidence.ToString(), p.FilenameMatchPath }.Select(CsvCell)));
        return builder.ToString();
    }

    private static string Md(string? value) =>
        (value ?? "Unknown").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string CsvCell(string? input)
    {
        var value = input ?? "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
