namespace CubaseInspector.Core;

/// <summary>
/// Composes an export of independently sourced project, local module and external
/// discovery observations. Never infers a plugin instance identity from a name.
/// </summary>
public static class PluginProfileReport
{
    public static string AssessCatalog(string? name, string? manufacturer,
        string? format, PluginMetadataEntry? catalog)
    {
        if (catalog is null)
            return "No external catalog entry selected. Local and project observations remain independent.";
        if (string.IsNullOrWhiteSpace(name))
            return "External catalog description only. No project or local source selected.";
        var nameMatches = catalog.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            catalog.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        var vendorKnown = !string.IsNullOrWhiteSpace(manufacturer);
        var vendorMatches = vendorKnown &&
            catalog.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase);
        if (!nameMatches)
            return "Catalog product name/aliases do not exactly match the source name. " +
                "This is a manually selected, unverified catalog association.";
        var prefix = vendorMatches
            ? "Catalog name/alias and manufacturer match the source."
            : vendorKnown
                ? "Catalog name/alias matches, but the manufacturer differs."
                : "Catalog name/alias matches; source manufacturer is unspecified.";
        var compatibleFormat = !string.IsNullOrWhiteSpace(format) &&
            catalog.Formats.Any(f => f.Equals(format, StringComparison.OrdinalIgnoreCase));
        return prefix + (compatibleFormat ? " The source format is advertised." : "") +
            " PluginDB does not verify the source VST3 class ID, binary, or Cubase instance.";
    }

    public static string Markdown(string? origin, string? sourceName, string? manufacturer,
        string? format, string? sourceId, string? localCid, string? bundlePath,
        string? evidence, IReadOnlyList<string> projectPaths, PluginMetadataEntry? catalog)
    {
        var sb = new System.Text.StringBuilder("# Plugin evidence profile")
            .AppendLine().AppendLine()
            .AppendLine("Observations are presented separately by provenance; matching names do NOT establish VST3 instance identity.")
            .AppendLine().AppendLine("## Selected source")
            .AppendLine()
            .AppendLine("- Origin: " + Clean(origin))
            .AppendLine("- Name: " + Clean(sourceName))
            .AppendLine("- Manufacturer: " + Clean(manufacturer))
            .AppendLine("- Format: " + Clean(format))
            .AppendLine("- Exported/source ID: " + Clean(sourceId))
            .AppendLine("- Locally declared component CID: " + Clean(localCid))
            .AppendLine("- Local bundle path: " + Clean(bundlePath))
            .AppendLine("- Evidence: " + Clean(evidence))
            .AppendLine().AppendLine("## Indexed referencing project files")
            .AppendLine();
        if (projectPaths.Count == 0) sb.AppendLine("No indexed source paths supplied.");
        foreach (var path in projectPaths.Take(1000)) sb.AppendLine("- " + Clean(path));
        sb.AppendLine().AppendLine("## Optional third-party catalog description")
            .AppendLine();
        if (catalog is null)
        {
            sb.AppendLine("No catalog profile selected.");
        }
        else
        {
            sb.AppendLine("- Source: " + Clean(catalog.Source))
                .AppendLine("- Product: " + Clean(catalog.Name))
                .AppendLine("- Manufacturer: " + Clean(catalog.Manufacturer))
                .AppendLine("- Category: " + Clean(catalog.Category))
                .AppendLine("- Subcategory: " + Clean(catalog.Subcategory))
                .AppendLine("- Advertised formats: " + Clean(catalog.FormatLabel))
                .AppendLine("- Tags: " + Clean(catalog.TagLabel))
                .AppendLine("- Product URL: " + Clean(catalog.Website))
                .AppendLine("- Manual URL: " + Clean(catalog.ManualUrl))
                .AppendLine("- Video URL: " + Clean(catalog.VideoUrl))
                .AppendLine("- External image URL (not redistributed): " + Clean(catalog.ImageUrl))
                .AppendLine()
                .AppendLine("Description: " + Clean(catalog.Description));
        }
        sb.AppendLine().AppendLine("## Association assessment")
            .AppendLine()
            .AppendLine(AssessCatalog(sourceName, manufacturer, format, catalog));
        return sb.ToString();
    }

    private static string Clean(string? value) => (value ?? "Unknown")
        .Replace("\\", "\\\\").Replace("\r", " ").Replace("\n", " ")
        .Replace("|", "\\|").Replace("[", "\\[").Replace("]", "\\]");
}
