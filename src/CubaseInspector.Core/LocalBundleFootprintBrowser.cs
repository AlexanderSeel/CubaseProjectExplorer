namespace CubaseInspector.Core;
public static class LocalBundleFootprintBrowser
{
    public static IReadOnlyList<LocalBundleFootprint> Filter(LocalPluginFootprintReport? report,
        string? search, string? sort, bool incompleteOnly)
    {
        if (report is null) return [];
        var term = (search ?? "").Trim();
        var source = report.Bundles.Where(b => (!incompleteOnly || !b.Complete) &&
            (term.Length == 0 || b.Path.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             (b.Warning?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)));
        return sort switch
        {
            "Largest" => source.OrderByDescending(b => b.Complete ? b.LogicalBytes : -1L)
                .ThenBy(b => b.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            "Most references" => source.OrderByDescending(b => b.ConfirmedReferences)
                .ThenBy(b => b.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            _ => source.OrderBy(b => b.Path, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
