namespace CubaseInspector.Core;

/// <summary>
/// Search and rank exported plugin references within the scanned projects only.
/// Rarely referenced does not imply installed, unused or removable.
/// </summary>
public sealed record PluginUsageQuery(
    string Search = "", string Format = "All", string Vendor = "All",
    string Sort = "Most projects", bool SingleProjectOnly = false);

public static class PluginUsageBrowser
{
    public static readonly IReadOnlyList<string> Sorts =
        ["Most projects", "Fewest projects", "Most instances", "Latest project file modified", "Name"];

    public static IReadOnlyList<string> Formats(IEnumerable<PluginUsage> usage) =>
        Facets(usage.Select(x => Display(x.Format)));

    public static IReadOnlyList<string> Vendors(IEnumerable<PluginUsage> usage) =>
        Facets(usage.Select(x => Display(x.Manufacturer)));

    private static IReadOnlyList<string> Facets(IEnumerable<string> values) =>
        new[] { "All" }.Concat(values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)).ToArray();

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();

    public static IReadOnlyList<PluginUsage> Filter(
        IEnumerable<PluginUsage> source, PluginUsageQuery query)
    {
        var term = (query.Search ?? "").Trim();
        var filtered = source.Where(p =>
            (string.IsNullOrWhiteSpace(query.Format) || query.Format == "All" ||
             Display(p.Format).Equals(query.Format, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(query.Vendor) || query.Vendor == "All" ||
             Display(p.Manufacturer).Equals(query.Vendor, StringComparison.OrdinalIgnoreCase)) &&
            (!query.SingleProjectOnly || p.ProjectCount == 1) &&
            (term.Length == 0 ||
             p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             p.PluginId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             Display(p.Manufacturer).Contains(term, StringComparison.OrdinalIgnoreCase) ||
             p.ProjectPaths.Any(path => path.Contains(term, StringComparison.OrdinalIgnoreCase))));
        IOrderedEnumerable<PluginUsage> sorted = query.Sort switch
        {
            "Fewest projects" => filtered.OrderBy(p => p.ProjectCount).ThenBy(p => p.InstanceCount),
            "Most instances" => filtered.OrderByDescending(p => p.InstanceCount).ThenByDescending(p => p.ProjectCount),
            "Latest project file modified" => filtered.OrderByDescending(p => p.LatestProjectFileModifiedUtc.HasValue)
                .ThenByDescending(p => p.LatestProjectFileModifiedUtc),
            "Name" => filtered.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(p => p.ProjectCount).ThenByDescending(p => p.InstanceCount)
        };
        return sorted.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Format, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
