namespace CubaseInspector.Infrastructure;

public sealed record LibraryDatabasePathResolution(string DatabasePath, string? Warning);

public static class LibraryDatabasePathResolver
{
    public static LibraryDatabasePathResolution Resolve(string? configuredPath, string localApplicationDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataPath);
        var fallback = Path.GetFullPath(Path.Combine(localApplicationDataPath, "CubaseProjectExplorer", "library.db"));
        if (string.IsNullOrWhiteSpace(configuredPath)) return new(fallback, null);

        try
        {
            var configured = Path.GetFullPath(configuredPath);
            var root = Path.GetPathRoot(configured);
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) return new(configured, null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Keep the selected path in preferences; only the active session falls back.
        }

        return new(fallback,
            $"The configured library location is unavailable: {configuredPath}. This session is using {fallback}. The saved location is retained and will be used when it becomes available again.");
    }
}
