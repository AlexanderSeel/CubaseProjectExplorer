namespace CubaseInspector.Core;

/// <summary>Canonical comma-separated multi-select values used by Library facets and saved views.</summary>
public static class ProjectLibraryFacetSelection
{
    public static string Normalize(string? selection, IReadOnlyList<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        if (string.IsNullOrWhiteSpace(selection) || selection.Trim().Equals("All", StringComparison.OrdinalIgnoreCase))
            return "All";
        if (selection.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
            return "None";

        var requested = selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = allowed.Where(value => value is not "All" and not "None" && requested.Contains(value)).ToArray();
        if (selected.Length == 0) return "All";
        return selected.Length == allowed.Count(value => value is not "All" and not "None")
            ? "All" : string.Join(", ", selected);
    }

    public static bool Includes(string? selection, string value)
    {
        if (string.IsNullOrWhiteSpace(selection) || selection.Trim().Equals("All", StringComparison.OrdinalIgnoreCase))
            return true;
        if (selection.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
            return false;
        return selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => item.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                Enum.TryParse<EvidenceLevel>(value, true, out var level) &&
                item.Equals(EvidencePresentation.Label(level), StringComparison.OrdinalIgnoreCase));
    }

    public static string Label(string heading, string? selection) =>
        string.IsNullOrWhiteSpace(selection) || selection.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? heading + ": All"
            : selection.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? heading + ": None"
                : heading + ": " + selection;
}
