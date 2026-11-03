namespace CubaseInspector.Core;

/// <summary>Accessible directions for the active Library sort column.</summary>
public static class ProjectLibrarySortPresentation
{
    public static string Header(string sortKey, string activeKey, bool reverse, string title)
    {
        if (!string.Equals(sortKey, activeKey, StringComparison.Ordinal)) return title;
        bool defaultDescending = sortKey is "Largest" or "Most recent" or "Track records";
        return title + (defaultDescending != reverse ? " ↓" : " ↑");
    }
}
