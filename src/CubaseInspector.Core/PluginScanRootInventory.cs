namespace CubaseInspector.Core;

/// <summary>A scan-root proposal; a path is not proof of a plugin installation.</summary>
public sealed record PluginScanRootInfo(string Path, string Source, bool Exists,
    bool Linked = false, bool Accessible = true)
{
    public bool IsScannable => Exists && !Linked && Accessible;
    public string Display => (Linked ? "Link/junction · skipped" :
        !Accessible ? "Inaccessible · skipped" :
        Exists ? "Available" : "Missing · skipped") + " | " + Source + " | " + Path;
}

public static class PluginScanRootInventory
{
    public static IReadOnlyList<PluginScanRootInfo> Merge(IEnumerable<(string Path, string Source)> candidates)
    {
        var roots = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (raw, source) in candidates.Take(400))
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 1024 || string.IsNullOrWhiteSpace(source)) continue;
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(raw.Trim(' ', '"'));
                if (!Path.IsPathFullyQualified(expanded)) continue;
                var full = Path.GetFullPath(expanded);
                if (!roots.TryGetValue(full, out var sources))
                    roots[full] = sources = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                sources.Add(source.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                PathTooLongException or IOException or UnauthorizedAccessException) { }
        }
        return roots.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => Inspect(x.Key, string.Join("; ", x.Value)))
            .ToArray();
    }

    private static PluginScanRootInfo Inspect(string path, string sources)
    {
        if (!Directory.Exists(path))
            return new(path, sources, false);
        try
        {
            return new(path, sources, true,
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException)
        {
            return new(path, sources, true, false, false);
        }
    }
}
