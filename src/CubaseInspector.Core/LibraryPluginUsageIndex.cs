namespace CubaseInspector.Core;

public sealed record LibraryPluginUsageRow(
    string Name, string Format, string Manufacturer, int ProjectCount, int ReferenceCount,
    int DistinctExportedIds, IReadOnlyList<string> ProjectPaths);

public sealed record LibraryCandidateMarkerRow(
    string Token, string ExampleName, int ProjectCount, int OccurrenceCount,
    IReadOnlyList<string> ProjectPaths);

public sealed record LibraryPluginUsageIndex(
    int StoredProjectCount, int ConfirmedProjectCount, int CandidateReferenceCount,
    int CandidateWithoutTokenCount, int UnknownReferenceCount,
    IReadOnlyList<LibraryPluginUsageRow> ConfirmedReferences,
    IReadOnlyList<LibraryCandidateMarkerRow> CandidateMarkers);

/// <summary>
/// Ranks reference labels seen in saved snapshots. It does not resolve names to products,
/// infer canonical identities from export-local IDs, or describe actual last use.
/// </summary>
public static class LibraryPluginUsageIndexer
{
    public static LibraryPluginUsageIndex Build(IEnumerable<ProjectSnapshot> source)
    {
        var projects = source.ToArray();
        var confirmedProjects = projects.Where(project => project.Evidence == EvidenceLevel.Confirmed).ToArray();
        var confirmed = confirmedProjects
            .SelectMany(project => project.Plugins
                .Where(plugin => plugin.Evidence == EvidenceLevel.Confirmed && !string.IsNullOrWhiteSpace(plugin.Name))
                .Select(plugin => (Project: project, Plugin: plugin)))
            .GroupBy(item => (Name: Normalize(item.Plugin.Name), Format: Normalize(item.Plugin.Format),
                Manufacturer: Normalize(item.Plugin.Manufacturer)))
            .Select(group =>
            {
                var paths = group.Select(item => item.Project.FilePath).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                var first = group.First().Plugin;
                return new LibraryPluginUsageRow(first.Name.Trim(), first.Format.Trim(),
                    string.IsNullOrWhiteSpace(first.Manufacturer) ? "Unknown" : first.Manufacturer.Trim(),
                    paths.Length, group.Count(), group.Select(item => item.Plugin.Id)
                        .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Count(), paths);
            })
            .OrderByDescending(row => row.ProjectCount).ThenByDescending(row => row.ReferenceCount)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        var candidateReferences = projects.SelectMany(project => project.Plugins
            .Where(plugin => plugin.Evidence == EvidenceLevel.Candidate)
            .Select(plugin => (Project: project, Plugin: plugin))).ToArray();
        var candidateWithoutTokenCount = candidateReferences.Count(item => string.IsNullOrWhiteSpace(item.Plugin.Id));
        var unknownReferenceCount = projects.Sum(project => project.Plugins.Count(plugin => plugin.Evidence == EvidenceLevel.Unknown));
        var candidates = candidateReferences.Where(item => !string.IsNullOrWhiteSpace(item.Plugin.Id))
            .GroupBy(item => item.Plugin.Id.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                var paths = group.Select(item => item.Project.FilePath).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                var exampleName = group.Select(item => item.Plugin.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Name unavailable";
                return new LibraryCandidateMarkerRow(group.Key, exampleName, paths.Length, group.Count(), paths);
            })
            .OrderByDescending(row => row.ProjectCount).ThenByDescending(row => row.OccurrenceCount)
            .ThenBy(row => row.Token, StringComparer.Ordinal).ToArray();

        return new LibraryPluginUsageIndex(projects.Length, confirmedProjects.Length,
            candidateReferences.Length, candidateWithoutTokenCount, unknownReferenceCount, confirmed, candidates);
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? "" : value.Trim().ToUpperInvariant();
}
