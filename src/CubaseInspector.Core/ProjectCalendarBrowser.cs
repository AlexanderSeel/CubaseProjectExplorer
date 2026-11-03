namespace CubaseInspector.Core;

/// <summary>Calendar dates are derived from project-file modification timestamps only.</summary>
public static class ProjectCalendarBrowser
{
    public static IReadOnlyList<ProjectLibraryEntry> OnLocalFileModifiedDate(
        IEnumerable<ProjectLibraryEntry> entries, DateOnly localDate) =>
        entries.Where(entry => entry.LastModifiedUtc is { } modified &&
                DateOnly.FromDateTime(modified.ToLocalTime().DateTime) == localDate)
            .OrderBy(entry => entry.Project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Project.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyDictionary<DateOnly, int> CountsByLocalFileModifiedDate(
        IEnumerable<ProjectLibraryEntry> entries) => entries
        .Where(entry => entry.LastModifiedUtc.HasValue)
        .GroupBy(entry => DateOnly.FromDateTime(entry.LastModifiedUtc!.Value.ToLocalTime().DateTime))
        .ToDictionary(group => group.Key, group => group.Count());

    public static IReadOnlyList<ProjectLibraryEntry> WithoutKnownFileModifiedDate(
        IEnumerable<ProjectLibraryEntry> entries) => entries
        .Where(entry => !entry.LastModifiedUtc.HasValue)
        .OrderBy(entry => entry.Project.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(entry => entry.Project.FilePath, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
