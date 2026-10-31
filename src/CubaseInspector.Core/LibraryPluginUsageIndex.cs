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
        var accumulator = new LibraryPluginUsageAccumulator();
        foreach (var project in source) accumulator.Add(project);
        return accumulator.Build();
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? "" : value.Trim().ToUpperInvariant();

    public sealed class LibraryPluginUsageAccumulator
    {
        private sealed class ConfirmedAggregate(PluginReference first)
        {
            public PluginReference First { get; } = first;
            public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);
            public int ReferenceCount { get; set; }
        }

        private sealed class CandidateAggregate
        {
            public string ExampleName { get; set; } = "Name unavailable";
            public bool HasExampleName { get; set; }
            public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int OccurrenceCount { get; set; }
        }

        private readonly Dictionary<(string Name, string Format, string Manufacturer), ConfirmedAggregate> _confirmed = [];
        private readonly Dictionary<string, CandidateAggregate> _candidates = new(StringComparer.Ordinal);
        private int _storedProjects;
        private int _confirmedProjects;
        private int _candidateReferences;
        private int _candidateWithoutTokenCount;
        private int _unknownReferenceCount;

        public void Add(ProjectSnapshot project)
        {
            ArgumentNullException.ThrowIfNull(project);
            _storedProjects++;
            if (project.Evidence == EvidenceLevel.Confirmed) _confirmedProjects++;

            foreach (var plugin in project.Plugins)
            {
                if (project.Evidence == EvidenceLevel.Confirmed && plugin.Evidence == EvidenceLevel.Confirmed &&
                    !string.IsNullOrWhiteSpace(plugin.Name))
                {
                    var key = (Normalize(plugin.Name), Normalize(plugin.Format), Normalize(plugin.Manufacturer));
                    if (!_confirmed.TryGetValue(key, out var aggregate))
                        _confirmed.Add(key, aggregate = new ConfirmedAggregate(plugin));
                    aggregate.ReferenceCount++;
                    aggregate.Paths.Add(project.FilePath);
                    if (!string.IsNullOrWhiteSpace(plugin.Id)) aggregate.Ids.Add(plugin.Id);
                }

                if (plugin.Evidence == EvidenceLevel.Candidate)
                {
                    _candidateReferences++;
                    if (string.IsNullOrWhiteSpace(plugin.Id))
                    {
                        _candidateWithoutTokenCount++;
                        continue;
                    }
                    var token = plugin.Id.Trim();
                    if (!_candidates.TryGetValue(token, out var aggregate))
                        _candidates.Add(token, aggregate = new CandidateAggregate());
                    aggregate.Paths.Add(project.FilePath);
                    aggregate.OccurrenceCount++;
                    if (!aggregate.HasExampleName && !string.IsNullOrWhiteSpace(plugin.Name))
                    {
                        aggregate.ExampleName = plugin.Name;
                        aggregate.HasExampleName = true;
                    }
                }
                else if (plugin.Evidence == EvidenceLevel.Unknown)
                    _unknownReferenceCount++;
            }
        }

        public LibraryPluginUsageIndex Build()
        {
            var confirmed = _confirmed.Select(pair =>
            {
                var aggregate = pair.Value;
                var paths = aggregate.Paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                return new LibraryPluginUsageRow(aggregate.First.Name.Trim(), aggregate.First.Format.Trim(),
                    string.IsNullOrWhiteSpace(aggregate.First.Manufacturer) ? "Unknown" : aggregate.First.Manufacturer.Trim(),
                    paths.Length, aggregate.ReferenceCount, aggregate.Ids.Count, paths);
            }).OrderByDescending(row => row.ProjectCount).ThenByDescending(row => row.ReferenceCount)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();

            var candidates = _candidates.Select(pair =>
            {
                var aggregate = pair.Value;
                var paths = aggregate.Paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                return new LibraryCandidateMarkerRow(pair.Key, aggregate.ExampleName, paths.Length,
                    aggregate.OccurrenceCount, paths);
            }).OrderByDescending(row => row.ProjectCount).ThenByDescending(row => row.OccurrenceCount)
                .ThenBy(row => row.Token, StringComparer.Ordinal).ToArray();

            return new LibraryPluginUsageIndex(_storedProjects, _confirmedProjects, _candidateReferences,
                _candidateWithoutTokenCount, _unknownReferenceCount, confirmed, candidates);
        }
    }
}
