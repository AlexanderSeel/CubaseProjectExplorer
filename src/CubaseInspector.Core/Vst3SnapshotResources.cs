using System.Globalization;

namespace CubaseInspector.Core;

/// <summary>Image-only CID associations from standard bundle resources, never loadability declarations.</summary>
public static class Vst3SnapshotResources
{
    public static IReadOnlyDictionary<string, string> Build(IEnumerable<InstalledPlugin> bundles, CancellationToken ct = default)
    {
        var candidates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var bundle in bundles.Where(bundle => bundle.Format.Equals("VST3", StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var directory = bundle.Path;
                var safe = Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0;
                foreach (var part in new[] { "Contents", "Resources", "Snapshots" })
                {
                    directory = Path.Combine(directory, part);
                    safe &= Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0;
                    if (!safe) break;
                }
                if (!safe) continue;
                var byCid = new Dictionary<string, List<(string Path, double Scale)>>(StringComparer.Ordinal);
                foreach (var file in Directory.EnumerateFiles(directory, "*.png").Take(512))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.Length < 41 || Vst3ClassIdentity.Canonical(name[..32]) is not { } cid ||
                        !name[32..].StartsWith("_snapshot", StringComparison.OrdinalIgnoreCase)) continue;
                    var suffix = name[41..];
                    var scale = 1d;
                    if (suffix.Length > 0 && (suffix[0] != '_' || !suffix.EndsWith('x') ||
                        !double.TryParse(suffix[1..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out scale) ||
                        !double.IsFinite(scale) || scale is <= 0 or > 8)) continue;
                    var validated = VerifiedVst3Artwork.ValidateSnapshot(bundle.Path, Path.GetRelativePath(bundle.Path, file));
                    if (validated is null) continue;
                    if (!byCid.TryGetValue(cid, out var images)) byCid[cid] = images = [];
                    images.Add((validated, scale));
                }
                foreach (var (cid, images) in byCid)
                {
                    if (!candidates.TryGetValue(cid, out var paths)) candidates[cid] = paths = new(StringComparer.OrdinalIgnoreCase);
                    paths.Add(images.OrderBy(image => Math.Abs(image.Scale - 1)).ThenBy(image => image.Path, StringComparer.OrdinalIgnoreCase).First().Path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        }
        return candidates.Where(pair => pair.Value.Count == 1).ToDictionary(pair => pair.Key, pair => pair.Value.Single(), StringComparer.Ordinal);
    }

    public static string? Find(IEnumerable<PluginReference> references, IReadOnlyDictionary<string, string> resources)
    {
        var items = references.ToArray();
        if (items.Length == 0 || items.Any(reference => reference.Evidence != EvidenceLevel.Confirmed ||
            !(reference.Format.Equals("VST3", StringComparison.OrdinalIgnoreCase) || reference.Format.Equals("Vst3Plugin", StringComparison.OrdinalIgnoreCase)))) return null;
        var cids = items.Select(reference => Vst3ClassIdentity.Canonical(reference.Id)).Distinct().ToArray();
        return cids.Length == 1 && cids[0] is { } cid ? resources.GetValueOrDefault(cid) : null;
    }
}
