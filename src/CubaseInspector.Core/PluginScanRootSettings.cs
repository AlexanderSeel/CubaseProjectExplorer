using System.Text.Json;

namespace CubaseInspector.Core;

/// <summary>User-owned scan roots. Missing/offline folders remain configured but are not scanned.</summary>
public sealed record PluginScanRootLoadResult(IReadOnlyList<string> Folders, bool IsWritable, string? Warning);

public static class PluginScanRootSettings
{
    public const int MaxRoots = 32;
    private const int MaxBytes = 32_768;
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CubaseProjectExplorer", "plugin-scan-folders.json");

    public static IReadOnlyList<string> Load(string? path = null) => Read(path).Folders;

    public static PluginScanRootLoadResult Read(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file)) return new([], true, null);
            if (new FileInfo(file).Length > MaxBytes)
                throw new InvalidDataException("Plugin folder settings exceed the size budget.");
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) ||
                !version.TryGetInt32(out var schema) || schema != 1 ||
                !root.TryGetProperty("folders", out var list) ||
                list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > MaxRoots)
                throw new InvalidDataException("Unsupported plugin folder settings schema.");
            var saved = new List<string>();
            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String ||
                    entry.GetString() is not { } value || string.IsNullOrWhiteSpace(value))
                    throw new InvalidDataException("Invalid saved plugin scan folder.");
                saved.Add(value);
            }
            var normalized = Normalize(saved);
            if (normalized.Count == 0 && saved.Count > 0)
                throw new InvalidDataException("Saved plugin scan paths cannot be interpreted.");
            // Deduplication is allowed; the original file is not rewritten during load.
            return new(normalized, true, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException or InvalidOperationException or
            ArgumentException or System.Security.SecurityException)
        {
            return new([], false, "Existing scan folder settings cannot be safely read (" +
                ex.GetType().Name + "); editing is disabled to protect the file.");
        }
    }

    /// <summary>Validate and preserve even temporarily disconnected folders.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> paths)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in paths.Take(MaxRoots + 1))
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 1024) continue;
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                if (!Path.IsPathFullyQualified(expanded)) continue;
                unique.Add(Path.GetFullPath(expanded));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                PathTooLongException or IOException or UnauthorizedAccessException) { }
            if (unique.Count >= MaxRoots) break;
        }
        return unique.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void Save(IEnumerable<string> paths, string? path = null)
    {
        var file = path ?? DefaultPath;
        var existing = Read(file);
        if (!existing.IsWritable)
            throw new InvalidOperationException(existing.Warning);
        var roots = Normalize(paths);
        var directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
        Directory.CreateDirectory(directory);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { version = 1, folders = roots }));
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
