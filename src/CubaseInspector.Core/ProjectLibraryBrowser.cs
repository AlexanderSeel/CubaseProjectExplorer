using System.Globalization;

namespace CubaseInspector.Core;

/// <summary>One parsed project and its filesystem metadata. These are not plugin usage timestamps.</summary>
public sealed record ProjectLibraryEntry(ProjectSnapshot Project, long? SizeBytes, DateTimeOffset? LastModifiedUtc,
    string? SourceSha256 = null);

public sealed record ProjectLibraryQuery(
    string Search = "", string FileType = "All", string Evidence = "All",
    string SortBy = "Name", double? MinBpm = null, double? MaxBpm = null,
    bool IncludeUnknownBpm = true, string VersionContains = "", string FolderContains = "",
    string TrackType = "All", long? MinSizeBytes = null, long? MaxSizeBytes = null,
    DateTimeOffset? ModifiedSinceUtc = null, DateTimeOffset? ModifiedBeforeUtc = null,
    bool WarningsOnly = false, bool ReverseSort = false);

/// <summary>Pure, deterministic library filtering. BPM-unknown CPR files remain visible by default.</summary>
public static class ProjectLibraryBrowser
{
    /// <summary>Updates paths found by one scan while retaining projects from other roots and deduplicating exact file copies.</summary>
    public static IReadOnlyList<ProjectLibraryEntry> MergeScanResults(
        IEnumerable<ProjectLibraryEntry> existing, IEnumerable<ProjectLibraryEntry> scanned)
    {
        var existingEntries = existing.ToArray();
        var scannedEntries = scanned.ToArray();
        var existingPaths = existingEntries.Select(entry => NormalizePath(entry.Project.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scannedPaths = scannedEntries.Select(entry => NormalizePath(entry.Project.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, ProjectLibraryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in existingEntries) byPath[NormalizePath(entry.Project.FilePath)] = entry;
        foreach (var entry in scannedEntries) byPath[NormalizePath(entry.Project.FilePath)] = entry;
        var unique = byPath.Values
            .GroupBy(entry => NormalizeSha256(entry.SourceSha256) is { } hash
                ? "sha256:" + hash : "path:" + NormalizePath(entry.Project.FilePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count() == 1 ? group.First() : group.OrderBy(Preference)
                .ThenBy(entry => ExtensionPriority(entry.Project.FilePath))
                .ThenBy(entry => NormalizePath(entry.Project.FilePath), StringComparer.OrdinalIgnoreCase)
                .First());
        return unique.OrderBy(entry => entry.Project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Project.FilePath, StringComparer.OrdinalIgnoreCase).ToArray();

        int Preference(ProjectLibraryEntry entry)
        {
            var path = NormalizePath(entry.Project.FilePath);
            if (existingPaths.Contains(path) && File.Exists(entry.Project.FilePath)) return 0;
            if (scannedPaths.Contains(path) && File.Exists(entry.Project.FilePath)) return 1;
            if (existingPaths.Contains(path)) return 2;
            return 3;
        }

        static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path); }
            catch (Exception exception) when (exception is ArgumentException or IOException or
                NotSupportedException or System.Security.SecurityException) { return path.Trim(); }
        }

        static int ExtensionPriority(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cpr" => 0,
            ".dawproject" => 1,
            ".bak" => 2,
            _ => 3
        };
    }

    public static string? NormalizeSha256(string? checksum) =>
        checksum is { Length: 64 } && checksum.All(Uri.IsHexDigit) ? checksum.ToUpperInvariant() : null;

    public static IReadOnlyList<ProjectLibraryEntry> Filter(
        IEnumerable<ProjectLibraryEntry> entries, ProjectLibraryQuery query)
    {
        var search = (query.Search ?? "").Trim();
        var filtered = entries.Where(entry =>
        {
            var p = entry.Project;
            var fileType = Path.GetExtension(p.FilePath).TrimStart('.');
            if (!ProjectLibraryFacetSelection.Includes(query.FileType, fileType)) return false;
            if (!ProjectLibraryFacetSelection.Includes(query.Evidence, p.Evidence.ToString())) return false;
            if (!string.IsNullOrWhiteSpace(query.VersionContains) &&
                !(p.CreatorVersion?.Contains(query.VersionContains.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
                return false;
            if (!string.IsNullOrWhiteSpace(query.FolderContains) &&
                !(Path.GetDirectoryName(p.FilePath)?.Contains(query.FolderContains.Trim(),
                    StringComparison.OrdinalIgnoreCase) ?? false))
                return false;
            if (!string.Equals(query.TrackType, "All", StringComparison.OrdinalIgnoreCase) &&
                !p.Tracks.Any(t => t.Kind.Equals(query.TrackType, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (query.WarningsOnly && p.Warnings.Count == 0) return false;
            if (query.MinSizeBytes is long minSize &&
                (!entry.SizeBytes.HasValue || entry.SizeBytes.Value < minSize)) return false;
            if (query.MaxSizeBytes is long maxSize &&
                (!entry.SizeBytes.HasValue || entry.SizeBytes.Value > maxSize)) return false;
            if (query.ModifiedSinceUtc is { } since &&
                (!entry.LastModifiedUtc.HasValue || entry.LastModifiedUtc.Value < since)) return false;
            if (query.ModifiedBeforeUtc is { } before &&
                (!entry.LastModifiedUtc.HasValue || entry.LastModifiedUtc.Value >= before)) return false;
            if (search.Length > 0 &&
                !p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !p.FilePath.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !(p.CreatorVersion?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) &&
                !p.Format.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !p.Tracks.Any(t => t.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                !p.Plugins.Any(plug => plug.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (query.MinBpm is not null || query.MaxBpm is not null)
            {
                if (p.Bpm is null) return query.IncludeUnknownBpm;
                if (query.MinBpm is double min && p.Bpm.Value < min) return false;
                if (query.MaxBpm is double max && p.Bpm.Value > max) return false;
            }
            return true;
        });

        var sorted = query.SortBy switch
        {
            "Most recent" => filtered.OrderByDescending(e => e.LastModifiedUtc)
                .ThenBy(e => e.Project.Name, StringComparer.OrdinalIgnoreCase),
            "Largest" => filtered.OrderByDescending(e => e.SizeBytes)
                .ThenBy(e => e.Project.Name, StringComparer.OrdinalIgnoreCase),
            "Track records" => filtered.OrderByDescending(e => e.Project.Tracks.Count)
                .ThenBy(e => e.Project.Name, StringComparer.OrdinalIgnoreCase),
            "Version" => filtered.OrderBy(e => e.Project.CreatorVersion ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Project.Name, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderBy(e => e.Project.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Project.FilePath, StringComparer.OrdinalIgnoreCase)
        };
        return query.ReverseSort ? sorted.Reverse().ToArray() : sorted.ToArray();
    }

    public static ProjectLibraryEntry FromFile(ProjectSnapshot project,
        long? fallbackSizeBytes = null, DateTimeOffset? fallbackLastModifiedUtc = null,
        string? fallbackSourceSha256 = null)
    {
        var checksum = NormalizeSha256(project.SourceSha256) ?? NormalizeSha256(fallbackSourceSha256);
        try
        {
            var file = new FileInfo(project.FilePath);
            return file.Exists
                ? new ProjectLibraryEntry(project, file.Length, new DateTimeOffset(file.LastWriteTimeUtc), checksum)
                : new ProjectLibraryEntry(project, fallbackSizeBytes, fallbackLastModifiedUtc, checksum);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return new ProjectLibraryEntry(project, fallbackSizeBytes, fallbackLastModifiedUtc, checksum);
        }
    }
}
