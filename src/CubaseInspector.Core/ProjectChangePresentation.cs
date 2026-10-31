namespace CubaseInspector.Core;

/// <summary>View-only interpretation of A/B changes. Confirmed DAWproject fields
/// and candidate CPR UID observations have separate evidence labels. A missing
/// source value is not numeric zero or proof a plugin was removed.</summary>
public sealed record ProjectChangeDisplay(
    string Category, string Entity, string Property, string BeforeText, string AfterText,
    string MatchQuality, string ChangeLabel);

public static class ProjectChangePresentation
{
    public static ProjectChangeDisplay Describe(ProjectChange change)
    {
        var label = change.Category == "Ambiguous" ? "Review identity"
            : change.Category == "Plugin" && change.Property == "Slot content" ? "Insert changed"
            : change.Category == "Plugin" && change.Property == "Insert slot" ? "Slot moved"
            : change.Property == "Added" ? "Added"
            : change.Property == "Removed" ? "Removed"
            : "Changed";
        return new ProjectChangeDisplay(change.Category, change.Entity, change.Property,
            change.Before ?? "— (not supplied)", change.After ?? "— (not supplied)",
            change.MatchQuality, label);
    }

    public static IReadOnlyList<ProjectChangeDisplay> Filter(
        IEnumerable<ProjectChange> changes, string? category, string? search)
    {
        var term = (search ?? "").Trim();
        return changes
            .Where(c => string.IsNullOrWhiteSpace(category) || category == "All" ||
                        c.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(c => term.Length == 0 ||
                        new[] { c.Category, c.Entity, c.Property, c.Before, c.After, c.MatchQuality }
                            .Any(v => v?.Contains(term, StringComparison.OrdinalIgnoreCase) == true))
            .Select(Describe).ToArray();
    }
}
