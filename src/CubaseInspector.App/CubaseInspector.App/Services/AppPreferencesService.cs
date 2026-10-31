using System.Text.Json;

namespace CubaseInspector.App.Services;

/// <summary>Stores app appearance and local database location; project paths and scan results are not preferences.</summary>
public sealed class AppPreferencesService(string? preferencePath = null)
{
    private readonly string _path = preferencePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CubaseProjectExplorer", "preferences.json");

    public string Theme { get; private set; } = "Dark";
    public string? DatabasePath { get; private set; }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length > 4096) return;
            using var document = JsonDocument.Parse(File.ReadAllText(_path), new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;
            if (document.RootElement.TryGetProperty("Theme", out var theme) && theme.ValueKind == JsonValueKind.String)
            {
                var value = theme.GetString();
                if (value is "Dark" or "Light" or "System") Theme = value;
            }
            if (document.RootElement.TryGetProperty("DatabasePath", out var databasePath) &&
                databasePath.ValueKind == JsonValueKind.String && databasePath.GetString() is { Length: > 0 and <= 2048 } path)
                DatabasePath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                                   System.Security.SecurityException or ArgumentException)
        {
            // Bad local preferences fall back to defaults; they do not affect project parsing.
        }
    }

    public void SetTheme(string theme)
    {
        if (theme is not ("Dark" or "Light" or "System")) return;
        Theme = theme;
        Save();
    }

    public void SetDatabasePath(string path)
    {
        var previousPath = DatabasePath;
        DatabasePath = Path.GetFullPath(path);
        try { Save(); }
        catch
        {
            DatabasePath = previousPath;
            throw;
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { Theme, DatabasePath }));
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
