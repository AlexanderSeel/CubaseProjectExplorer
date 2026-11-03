using System.Text.Json;

namespace CubaseInspector.Core;

/// <summary>
/// Explicitly user-saved filter form fields; never stores scanned project records.
/// Search/folder terms are saved only when the user chooses Save view.
/// </summary>
public sealed record LibraryFilterState(
    string Search = "", string FileType = "All", string Evidence = "All", string Sort = "Name",
    bool ReverseSort = false, string Version = "", string Folder = "", string TrackType = "All",
    string MinSizeMiB = "", string MaxSizeMiB = "", string MinBpm = "", string MaxBpm = "",
    bool IncludeUnknownBpm = true, bool WarningsOnly = false,
    DateTimeOffset? ModifiedFrom = null, DateTimeOffset? ModifiedThrough = null,
    string GroupMode = "None", string? GroupKey = null)
{
    public LibraryFilterState Normalized() => this with
    {
        Search = Trim(Search), Version = Trim(Version), Folder = Trim(Folder),
        MinSizeMiB = Trim(MinSizeMiB), MaxSizeMiB = Trim(MaxSizeMiB),
        MinBpm = Trim(MinBpm), MaxBpm = Trim(MaxBpm),
        FileType = ProjectLibraryFacetSelection.Normalize(FileType, ["All", "CPR", "BAK", "DAWPROJECT"]),
        Evidence = ProjectLibraryFacetSelection.Normalize(Evidence, ["All", "Confirmed", "Candidate", "Unknown"]),
        Sort = Choose(Sort, ["Name", "Most recent", "Largest", "Track records", "Version"]),
        TrackType = Choose(TrackType,
            ["All", "Audio", "MIDI", "Instrument", "Folder", "Sampler", "Group", "FX", "Master", "Track", "Channel"]),
        GroupMode = Choose(GroupMode, ProjectLibraryGrouping.Modes),
        GroupKey = Choose(GroupMode, ProjectLibraryGrouping.Modes) == "None" || GroupKey is null
            ? null : Trim(GroupKey)
    };

    private static string Trim(string? s) =>
        (s ?? "").Trim().Length <= 160 ? (s ?? "").Trim() : (s ?? "").Trim()[..160];

    private static string Choose(string? value, IReadOnlyList<string> allowed) =>
        allowed.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? allowed[0];
}

public sealed record LibrarySavedView(string Name, LibraryFilterState Filters);
public sealed record LibrarySavedViewsLoadResult(
    IReadOnlyList<LibrarySavedView> Views, bool CanWrite, string? Warning = null);

/// <summary>Bounded JSON catalog, separate from layout-only preferences.</summary>
public static class LibrarySavedViews
{
    public const int MaximumViews = 20;
    private const int MaximumJsonLength = 128_000;
    private static readonly HashSet<string> FilterProperties = new(StringComparer.Ordinal)
    {
        "Search", "FileType", "Evidence", "Sort", "ReverseSort", "Version", "Folder",
        "TrackType", "MinSizeMiB", "MaxSizeMiB", "MinBpm", "MaxBpm", "IncludeUnknownBpm",
        "WarningsOnly", "ModifiedFrom", "ModifiedThrough", "GroupMode", "GroupKey"
    };

    public static LibrarySavedViewsLoadResult ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return new([], true);
            if (new FileInfo(path).Length > MaximumJsonLength)
                return new([], false, "Saved views exceed the size limit; the existing file was left unchanged.");
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return new([], false, "Saved views are empty; the existing file was left unchanged.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() > MaximumViews)
                return new([], false, "Saved views have an unsupported shape or count; the existing file was left unchanged.");
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !HasUniqueProperties(item, ["Name", "Filters"]) ||
                    !item.TryGetProperty("Name", out var name) || name.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("Filters", out var filters) || filters.ValueKind != JsonValueKind.Object ||
                    filters.EnumerateObject().Any(property => !FilterProperties.Contains(property.Name)))
                    return new([], false, "Saved views contain unknown or malformed fields; the existing file was left unchanged.");
            }
            return new(ParseOrEmpty(json), true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                                   NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            return new([], false,
                "Saved views could not be read safely (" + ex.GetType().Name + "); the existing file was left unchanged.");
        }
    }
    public static IReadOnlyList<LibrarySavedView> ParseOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 128_000) return [];
        try
        {
            var input = JsonSerializer.Deserialize<LibrarySavedView[]>(json);
            if (input is null) return [];
            var result = new List<LibrarySavedView>();
            foreach (var item in input)
            {
                if (item is null || item.Filters is null) continue;
                var name = CleanName(item.Name);
                if (name is null || result.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                result.Add(new LibrarySavedView(name, item.Filters.Normalized()));
                if (result.Count == MaximumViews) break;
            }
            return result;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return [];
        }
    }

    public static IReadOnlyList<LibrarySavedView> Save(
        IEnumerable<LibrarySavedView> existing, string? name, LibraryFilterState filters)
    {
        var clean = CleanName(name) ??
            throw new ArgumentException("Enter a preset name (1–40 printable characters).", nameof(name));
        var items = existing.Take(MaximumViews).ToList();
        var index = items.FindIndex(x => x.Name.Equals(clean, StringComparison.OrdinalIgnoreCase));
        var entry = new LibrarySavedView(clean, filters.Normalized());
        if (index >= 0) items[index] = entry;
        else
        {
            if (items.Count >= MaximumViews)
                throw new InvalidOperationException("Maximum 20 saved views; delete one before adding another.");
            items.Add(entry);
        }
        return items;
    }

    public static string ToJson(IEnumerable<LibrarySavedView> views) =>
        JsonSerializer.Serialize(views.Take(MaximumViews), new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Atomic local-preset save; a failed write never truncates the previous file.</summary>
    public static void SaveFile(string path, IEnumerable<LibrarySavedView> views)
        => SaveFile(path, views, existingSettingsWritable: true);

    public static void SaveFile(string path, IEnumerable<LibrarySavedView> views,
        bool existingSettingsWritable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!existingSettingsWritable)
            throw new InvalidOperationException("Existing saved views could not be read safely; refusing to overwrite them.");
        var absolute = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(absolute)!;
        Directory.CreateDirectory(folder);
        var temp = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, ToJson(views));
            File.Move(temp, absolute, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static bool HasUniqueProperties(JsonElement element, IReadOnlyCollection<string> expected)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                return false;
        return seen.Count == expected.Count;
    }

    private static string? CleanName(string? value)
    {
        var name = value?.Trim();
        return string.IsNullOrWhiteSpace(name) || name.Length > 40 || name.Any(char.IsControl)
            ? null : name;
    }
}
