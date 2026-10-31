using System.Globalization;
using System.Text;

namespace CubaseInspector.Core;

public sealed record PluginCatalogCoverageRow(string Plugin, string? Vendor, string Format,
    string Evidence, int Observations, string ReviewState, string? CatalogName,
    string? CatalogVendor, string? CatalogSource);

/// <summary>Catalog review workflow states. These describe third-party metadata, never VST3 identity.</summary>
public static class PluginCatalogCoverage
{
    public const string NoCatalog = "Catalog not loaded";
    public const string NameHint = "Name hint";
    public const string Reviewed = "Reviewed";
    public const string Rejected = "Rejected";
    public const string Missing = "No match";
    public const string Stale = "Saved match unavailable";
    public static IReadOnlyList<string> Filters { get; } =
        ["All", "Needs review", "Reviewed", "Rejected", "Saved match unavailable"];

    public static string State(bool catalogLoaded, PluginCatalogDecision? decision,
        PluginMetadataEntry? matched)
    {
        if (decision?.Rejected == true) return Rejected;
        if (decision is not null) return matched is null ? Stale : Reviewed;
        if (!catalogLoaded) return NoCatalog;
        return matched is null ? Missing : NameHint;
    }

    public static bool Matches(string? filter, string state) => filter switch
    {
        "Needs review" => state != Reviewed && state != Rejected,
        "Reviewed" => state == Reviewed,
        "Rejected" => state == Rejected,
        "Saved match unavailable" => state == Stale,
        _ => true
    };

    public static string Csv(IEnumerable<PluginCatalogCoverageRow> rows)
    {
        var sb = new StringBuilder("Plugin,ProjectVendor,Format,ProjectEvidence,SourceObservations,CatalogReviewState,CatalogName,CatalogVendor,CatalogSource").AppendLine();
        foreach (var item in rows)
            sb.AppendLine(string.Join(",", [
                Cell(item.Plugin), Cell(item.Vendor), Cell(item.Format), Cell(item.Evidence),
                item.Observations.ToString(CultureInfo.InvariantCulture), Cell(item.ReviewState),
                Cell(item.CatalogName), Cell(item.CatalogVendor), Cell(item.CatalogSource)
            ]));
        return sb.ToString();
    }

    private static string Cell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
