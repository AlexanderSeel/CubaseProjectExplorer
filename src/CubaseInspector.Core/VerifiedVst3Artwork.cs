namespace CubaseInspector.Core;

/// <summary>Read-only image from a uniquely matched, locally declared VST3 class.
/// Local paths are never inferred from catalog names or native CPR candidate bytes.</summary>
public static class VerifiedVst3Artwork
{
    private const long MaxPngBytes = 8L * 1024 * 1024;

    public static string? FindLocalSnapshot(IEnumerable<PluginCheck> source)
    {
        // Do not choose an arbitrary image when a group resolves to different bundles/classes.
        var snapshots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var check in source)
        {
            if (check.Reference.Evidence != EvidenceLevel.Confirmed ||
                check.Status != PluginStatus.InstalledByClassId ||
                !(check.Reference.Format.Equals("VST3", StringComparison.OrdinalIgnoreCase) ||
                  check.Reference.Format.Equals("Vst3Plugin", StringComparison.OrdinalIgnoreCase)) ||
                check.MatchedClass is not { } klass ||
                string.IsNullOrWhiteSpace(check.MatchedPath)) continue;
            var expected = Vst3ClassIdentity.Canonical(check.Reference.Id);
            var actual = Vst3ClassIdentity.Canonical(klass.Cid);
            if (expected is null || expected != actual) continue;
            if (FindClassSnapshot(check.MatchedPath, klass) is { } safePath)
                snapshots.Add(safePath);
            if (snapshots.Count > 1) return null;
        }
        return snapshots.Count == 1 ? snapshots.First() : null;
    }

    public static string? FindClassSnapshot(string bundle, InstalledPluginClass klass)
    {
        if (ValidateSnapshot(bundle, klass.SnapshotPath) is { } advertised) return advertised;
        var cid = Vst3ClassIdentity.Canonical(klass.Cid);
        if (cid is null) return null;
        // Steinberg's standard CID-named resources may exist without a Snapshots
        // property in moduleinfo.json. The class CID still comes from metadata.
        foreach (var name in new[] { cid, cid.ToLowerInvariant() })
            foreach (var suffix in new[] { "_snapshot.png", "_snapshot_2.0x.png", "_snapshot_2.00x.png" })
                if (ValidateSnapshot(bundle, Path.Combine("Contents", "Resources", "Snapshots", name + suffix)) is { } standard)
                    return standard;
        return null;
    }

    /// <summary>Reject traversal, links, oversize and missing resources, including ancestor links.</summary>
    public static string? ValidateSnapshot(string? bundle, string? relative)
    {
        if (string.IsNullOrWhiteSpace(bundle) || string.IsNullOrWhiteSpace(relative) ||
            relative.Length > 512 || !relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(relative)) return null;
        try
        {
            var root = Path.GetFullPath(bundle);
            if (!Directory.Exists(root)) return null;
            var path = Path.GetFullPath(Path.Combine(root, relative));
            var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return null;

            // Walking ancestors ensures a symlink/junction cannot redirect the relative snapshot outside the bundle.
            var relativePath = Path.GetRelativePath(root, path);
            var parts = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 16) return null;
            var current = root;
            foreach (var part in parts)
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            }

            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxPngBytes) return null;
            return info.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        { return null; }
    }
}
