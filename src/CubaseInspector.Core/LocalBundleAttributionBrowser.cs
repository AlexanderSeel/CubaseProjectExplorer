namespace CubaseInspector.Core;

/// <summary>
/// Keeps measured bundle rows, their verified source attribution and CSV review in sync.
/// A project/plugin query filters bundles; it never changes measured logical byte totals.
/// </summary>
public static class LocalBundleAttributionBrowser
{
    public static IReadOnlyList<LocalBundleAttribution> Filter(
        LocalPluginFootprintReport? report, IEnumerable<LocalBundleAttribution> attribution,
        string? search, string? sort, bool incompleteOnly)
    {
        if (report is null) return [];
        var term = (search ?? "").Trim();
        var rows = attribution.GroupBy(x => x.Bundle.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return LocalBundleFootprintBrowser.Filter(report, null, sort, incompleteOnly)
            .Select(bundle => rows.TryGetValue(bundle.Path, out var source)
                ? source : new LocalBundleAttribution(bundle, []))
            .Where(row => term.Length == 0 ||
                row.Bundle.Path.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (row.Bundle.Warning?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                row.Uses.Any(use =>
                    use.ProjectPath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    use.PluginName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    use.ClassId.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}
