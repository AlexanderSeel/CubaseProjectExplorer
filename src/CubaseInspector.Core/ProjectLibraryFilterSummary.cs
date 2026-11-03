using System.Globalization;

namespace CubaseInspector.Core;

/// <summary>Human-readable, provenance-neutral descriptions of active library predicates.
/// Sort is deliberately not a filter and is never counted here.</summary>
public static class ProjectLibraryFilterSummary
{
    public static IReadOnlyList<string> Describe(ProjectLibraryQuery q)
    {
        var filters = new List<string>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) filters.Add(label + ": " + value.Trim());
        }

        Add("Search", q.Search);
        if (!IsAll(q.FileType)) Add("Type", q.FileType);
        if (!IsAll(q.Evidence)) Add("Evidence", q.Evidence);
        Add("Version", q.VersionContains);
        Add("Folder", q.FolderContains);
        if (!IsAll(q.TrackType)) Add("Track", q.TrackType);
        if (q.MinSizeBytes is { } min) filters.Add("Size ≥ " + MiB(min));
        if (q.MaxSizeBytes is { } max) filters.Add("Size ≤ " + MiB(max));
        if (q.ModifiedSinceUtc is { } since)
            filters.Add("Modified from " + since.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (q.ModifiedBeforeUtc is { } before)
            filters.Add("Modified before " + before.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (q.WarningsOnly) filters.Add("Parser warnings only");
        if (q.MinBpm is { } minBpm) filters.Add("BPM ≥ " + minBpm.ToString("0.##", CultureInfo.InvariantCulture));
        if (q.MaxBpm is { } maxBpm) filters.Add("BPM ≤ " + maxBpm.ToString("0.##", CultureInfo.InvariantCulture));
        if (!q.IncludeUnknownBpm && (q.MinBpm.HasValue || q.MaxBpm.HasValue))
            filters.Add("Exclude unknown BPM");
        return filters;
    }

    private static bool IsAll(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("All", StringComparison.OrdinalIgnoreCase);

    private static string MiB(long bytes) =>
        (bytes / 1048576d).ToString("0.##", CultureInfo.InvariantCulture) + " MiB";
}
