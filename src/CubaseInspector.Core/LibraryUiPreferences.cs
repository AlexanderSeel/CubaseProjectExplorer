using System.Text.Json;

namespace CubaseInspector.Core;

/// <summary>
/// Non-sensitive UI layout choices only; no file paths, project metadata or scan results persisted.
/// </summary>
public sealed record LibraryUiPreferences(int ViewMode = 1, double DetailsWidth = 312, int Density = 1,
    double[]? ColumnWeights = null, string ColumnPreset = "Balanced", int Version = 2)
{
    private const int MaximumFileBytes = 16_384;

    public sealed record LoadResult(LibraryUiPreferences Settings, bool CanWrite, string? Warning = null);

    public LibraryUiPreferences Normalized() => new(
        Math.Clamp(ViewMode, 0, 2),
        double.IsFinite(DetailsWidth) ? Math.Clamp(DetailsWidth, 230, 530) : 312,
        Math.Clamp(Density, 0, 2),
        ColumnWeights is null ? null : LibraryColumnLayout.Normalize(ColumnWeights),
        LibraryColumnLayout.Presets.Contains(ColumnPreset) ? ColumnPreset : "Balanced",
        Version: 2);

    public static LibraryUiPreferences ParseOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return (JsonSerializer.Deserialize<LibraryUiPreferences>(json) ?? new()).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return new();
        }
    }

    public static LoadResult ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return new(new(), true);
            if (new FileInfo(path).Length > MaximumFileBytes)
                return new(new(), false, "Layout preferences exceed the size limit; they were left unchanged.");
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return new(new(), false, "Layout preferences are empty and were left unchanged.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new(new(), false, "Layout preferences have an unsupported shape and were left unchanged.");
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "ViewMode", "DetailsWidth", "Density", "ColumnWeights", "ColumnPreset", "Version" };
            var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!known.Contains(property.Name) || !observed.Add(property.Name))
                    return new(new(), false,
                        "Layout preferences contain unknown or duplicate fields and were left unchanged.");
            var settings = ParseOrDefault(json);
            // Version 1 shipped Cards as the default. The new library is designed
            // around a dense details view, so migrate that legacy default once.
            var properties = document.RootElement.EnumerateObject().ToArray();
            var hasVersion = properties.Any(property =>
                string.Equals(property.Name, "Version", StringComparison.OrdinalIgnoreCase));
            var hasExplicitViewMode = properties.Any(property =>
                string.Equals(property.Name, "ViewMode", StringComparison.OrdinalIgnoreCase));
            if (!hasVersion && !hasExplicitViewMode)
                settings = settings with { ViewMode = 1, Version = 2 };
            return new(settings.Normalized(), true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                                   NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            return new(new(), false,
                "Layout preferences could not be read safely (" + ex.GetType().Name + "); they were left unchanged.");
        }
    }

    public static void SaveFile(string path, LibraryUiPreferences settings, bool existingSettingsWritable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!existingSettingsWritable)
            throw new InvalidOperationException("Existing layout preferences could not be read safely; refusing to overwrite them.");
        var absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var temp = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, settings.ToJson());
            File.Move(temp, absolute, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(Normalized(), new JsonSerializerOptions
    {
        WriteIndented = true
    });
}
