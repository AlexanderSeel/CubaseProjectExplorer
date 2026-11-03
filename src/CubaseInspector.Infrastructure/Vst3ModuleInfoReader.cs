using System.Text.Json;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Reads documented VST3 moduleinfo.json sidecars. Never loads the plugin executable,
/// traverses symlinks, or interprets candidate CPR bytes as VST3 identities.
/// </summary>
public static class Vst3ModuleInfoReader
{
    private const long MaxBytes = 2_000_000;

    public static IReadOnlyList<InstalledPluginClass> Read(string vst3Path)
    {
        if (!Directory.Exists(vst3Path)) return [];
        try
        {
            if ((File.GetAttributes(vst3Path) & FileAttributes.ReparsePoint) != 0)
                return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException) { return []; }
        foreach (var relative in new[] {
            Path.Combine("Contents", "Resources", "moduleinfo.json"),
            Path.Combine("Contents", "moduleinfo.json") })
        {
            var path = Path.Combine(vst3Path, relative);
            try
            {
                // Reject symlinks/junctions in intermediate Contents/Resources directories too.
                // A safe final file alone does not ensure the path stayed inside the VST3 bundle.
                var parent = vst3Path;
                var linkedParent = false;
                var parts = relative.Split(Path.DirectorySeparatorChar);
                foreach (var folder in parts.Take(parts.Length - 1))
                {
                    parent = Path.Combine(parent, folder);
                    if (!Directory.Exists(parent) ||
                        (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    {
                        linkedParent = true;
                        break;
                    }
                }
                if (linkedParent || !File.Exists(path)) continue;
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    info.Length <= 0 || info.Length > MaxBytes) continue;
                return Parse(File.ReadAllText(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or JsonException or ArgumentException)
            {
                // A broken/inaccessible optional sidecar never prevents scanning the bundle.
            }
        }
        return [];
    }

    public static IReadOnlyList<InstalledPluginClass> Parse(string json)
    {
        if (json.Length > MaxBytes) return [];
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 32
        });
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("Classes", out var classes) ||
            classes.ValueKind != JsonValueKind.Array) return [];
        var output = new List<InstalledPluginClass>();
        foreach (var entry in classes.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !string.Equals(Str(entry, "Category"), "Audio Module Class", StringComparison.OrdinalIgnoreCase))
                continue;
            var cid = Vst3ClassIdentity.Canonical(Str(entry, "CID"));
            if (cid is null) continue;
            var snapshot = Snapshot(entry, cid);
            output.Add(new InstalledPluginClass(cid,
                Str(entry, "Name") ?? "Unnamed VST3 class",
                Str(entry, "Vendor") ?? (doc.RootElement.TryGetProperty("Factory Info", out var factory) ?
                    Str(factory, "Vendor") : null),
                Str(entry, "Version"), "Audio Module Class", snapshot));
            if (output.Count == 256) break;
        }
        return output.GroupBy(x => x.Cid, StringComparer.Ordinal)
            .Select(g => g.First()).ToArray();
    }

    private static string? Snapshot(JsonElement entry, string cid)
    {
        if (!entry.TryGetProperty("Snapshots", out var list) || list.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var snap in list.EnumerateArray())
        {
            var value = Str(snap, "Path");
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
                Path.IsPathRooted(value) || value.Contains("..", StringComparison.Ordinal) ||
                !value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            // The plugin bundle owns the advertised image, not an unverified HTTP asset.
            return value.Replace('/', Path.DirectorySeparatorChar);
        }
        return null;
    }

    private static string? Str(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.String
            ? child.GetString() : null;
}
