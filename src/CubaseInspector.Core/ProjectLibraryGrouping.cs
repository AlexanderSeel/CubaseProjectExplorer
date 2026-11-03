namespace CubaseInspector.Core;

/// <summary>Presentation-only grouping; never interprets CPR candidate fields as facts.</summary>
public sealed record ProjectLibraryGroup(string? Key, string Label, int Count)
{
    public string Display => $"{Label} ({Count})";
}

public sealed record ProjectLibraryGroupResult(
    IReadOnlyList<ProjectLibraryGroup> Groups, bool Limited);

public static class ProjectLibraryGrouping
{
    public static readonly IReadOnlyList<string> Modes =
        ["None", "Folder", "Type", "Evidence", "Version", "Modified year", "Modified month"];

    private const int MaximumVisibleGroups = 250;

    public static ProjectLibraryGroupResult Build(
        IReadOnlyList<ProjectLibraryEntry> entries, string mode, string root)
    {
        if (mode == "None") return new ProjectLibraryGroupResult([], false);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var key = Key(entry, mode, root);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var groups = counts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumVisibleGroups)
            .Select(pair => new ProjectLibraryGroup(pair.Key,
                pair.Key.Length == 0 ? "(scan root)" : pair.Key, pair.Value))
            .ToList();
        groups.Insert(0, new ProjectLibraryGroup(null, "All groups", entries.Count));
        return new ProjectLibraryGroupResult(groups, counts.Count > MaximumVisibleGroups);
    }

    public static bool Includes(ProjectLibraryEntry entry, string mode, string root, string? key) =>
        key is null || string.Equals(Key(entry, mode, root), key, StringComparison.OrdinalIgnoreCase);

    public static string Key(ProjectLibraryEntry entry, string mode, string root)
    {
        var project = entry.Project;
        return mode switch
        {
            "Type" => Path.GetExtension(project.FilePath).TrimStart('.').ToUpperInvariant(),
            "Evidence" => project.Evidence.ToString(),
            "Version" => string.IsNullOrWhiteSpace(project.CreatorVersion) ? "Unknown version"
                : project.CreatorVersion.Trim(),
            "Folder" => RelativeDirectory(root, project.FilePath),
            "Modified year" => entry.LastModifiedUtc?.ToLocalTime().ToString("yyyy") ?? "Unknown modification date",
            "Modified month" => entry.LastModifiedUtc?.ToLocalTime().ToString("yyyy-MM") ?? "Unknown modification date",
            _ => ""
        };
    }

    public static string RelativeDirectory(string root, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Unknown folder";
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir)) return "Unknown folder";
            if (string.IsNullOrWhiteSpace(root)) return dir;
            var relative = Path.GetRelativePath(root, dir);
            if (relative == ".") return "";
            // Guard against project files outside the scanned root and different-drive roots.
            var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (first == ".." || Path.IsPathRooted(relative)) return "Outside scan root";
            return relative;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return "Unknown folder";
        }
    }
}
