namespace CubaseInspector.Core;

/// <summary>Library-wide summaries of readable project snapshots, never the failed scan inputs.</summary>
public sealed record LibraryAnalytics(
    int Readable, int Confirmed, int Candidate, int UnknownEvidence, int UnknownTempo,
    int ConfirmedTracks, int CandidateTracks, int ConfirmedPluginReferences,
    int UnverifiedPluginMarkers, int FilesWithKnownSize, long KnownSizeBytes,
    DateTimeOffset? LatestProjectFileModifiedUtc,
    IReadOnlyList<ProjectChartBar> Formats, IReadOnlyList<ProjectChartBar> Evidence,
    IReadOnlyList<ProjectChartBar> ConfirmedPluginVendors);

public static class LibraryAnalyticsService
{
    public static LibraryAnalytics Summarize(IEnumerable<ProjectLibraryEntry> entries)
    {
        var files = entries.ToArray();
        var projects = files.Select(f => f.Project).ToArray();
        var confirmed = projects.Where(p => p.Evidence == EvidenceLevel.Confirmed).ToArray();
        var candidate = projects.Where(p => p.Evidence == EvidenceLevel.Candidate).ToArray();
        var knownSizes = files.Where(f => f.SizeBytes is >= 0).ToArray();
        var timestamps = files.Where(f => f.LastModifiedUtc.HasValue)
            .Select(f => f.LastModifiedUtc!.Value).ToArray();
        var pluginRefs = confirmed.SelectMany(p => p.Plugins)
            .Where(p => p.Evidence == EvidenceLevel.Confirmed).ToArray();

        static IReadOnlyList<ProjectChartBar> Bars(IEnumerable<(string Label, int Count, EvidenceLevel Evidence)> groups,
            int denominator, string unit)
        {
            if (denominator == 0) return [];
            return groups.OrderByDescending(g => g.Count)
                .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProjectChartBar(g.Label, g.Count, 100d * g.Count / denominator,
                    g.Evidence, $"{g.Count} {unit} ({100d * g.Count / denominator:0.#}%)"))
                .ToArray();
        }

        var formats = Bars(projects.GroupBy(p => Path.GetExtension(p.FilePath).TrimStart('.').ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key.Length == 0 ? "Unknown type" : g.Key, g.Count(), EvidenceLevel.Unknown)),
            projects.Length, "readable project files");
        var evidence = Bars(projects.GroupBy(p => p.Evidence)
            .Select(g => (g.Key.ToString(), g.Count(), g.Key)),
            projects.Length, "readable projects");
        var vendors = pluginRefs.GroupBy(p => string.IsNullOrWhiteSpace(p.Manufacturer)
                    ? "Unknown manufacturer" : p.Manufacturer.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => (Label: g.Key, Count: g.Count(), Evidence: EvidenceLevel.Confirmed))
                .OrderByDescending(g => g.Count)
                .Take(10).ToArray();
        return new LibraryAnalytics(
            projects.Length, confirmed.Length, candidate.Length,
            projects.Count(p => p.Evidence == EvidenceLevel.Unknown),
            projects.Count(p => p.Bpm is null),
            confirmed.Sum(p => p.Tracks.Count(t => t.Evidence == EvidenceLevel.Confirmed)),
            candidate.Sum(p => p.Tracks.Count(t => t.Evidence == EvidenceLevel.Candidate)),
            pluginRefs.Length,
            projects.Sum(p => p.Plugins.Count(plug => plug.Evidence != EvidenceLevel.Confirmed)),
            knownSizes.Length, knownSizes.Sum(f => f.SizeBytes!.Value),
            timestamps.Length == 0 ? null : timestamps.Max(),
            formats, evidence, Bars(vendors, pluginRefs.Length, "confirmed exported references"));
    }
}
