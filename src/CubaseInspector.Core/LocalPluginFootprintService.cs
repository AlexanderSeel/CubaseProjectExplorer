namespace CubaseInspector.Core;

/// <summary>Read-only logical file bytes for uniquely CID-matched local plugin bundles.
/// These bytes are NOT allocated disk blocks, installer size, or safe reclaimable space.</summary>
public sealed record LocalBundleFootprint(string Path, long LogicalBytes, int Files,
    bool Complete, string? Warning, int ConfirmedReferences);
public sealed record LocalPluginFootprintReport(IReadOnlyList<LocalBundleFootprint> Bundles,
    long CompleteLogicalBytes, int ExactReferences, int ExcludedReferences,
    bool Complete, string Notice);

public static class LocalPluginFootprintService
{
    private const int MaxEntriesPerBundle = 200_000;
    private const int MaxDepth = 32;

    public static LocalPluginFootprintReport Analyze(IEnumerable<PluginCheck> checks,
        CancellationToken cancellationToken = default)
    {
        var all = checks.ToArray();
        // Do not ascribe disk space to filename hints, CPR candidates or ambiguous CIDs.
        var exact = all.Where(x => x.Status == PluginStatus.InstalledByClassId &&
            x.Reference.Evidence == EvidenceLevel.Confirmed &&
            !string.IsNullOrWhiteSpace(x.MatchedPath)).ToArray();
        var validPaths = new List<string>();
        var invalidPaths = 0;
        foreach (var check in exact)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { validPaths.Add(Path.GetFullPath(check.MatchedPath!)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                PathTooLongException or IOException) { invalidPaths++; }
        }
        var bundles = validPaths.GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => Measure(group.Key, group.Count(), cancellationToken)).ToArray();
        return new LocalPluginFootprintReport(bundles,
            bundles.Where(x => x.Complete).Sum(x => x.LogicalBytes),
            validPaths.Count, all.Length - validPaths.Count, invalidPaths == 0 && bundles.All(x => x.Complete),
            "Logical bytes of uniquely class-ID-matched local paths only. Shared bundles counted once. " +
            "Incomplete bundles and invalid paths are excluded from totals; files are never modified. " +
            "Size is not physical allocated space or safely reclaimable disk space.");
    }

    private static LocalBundleFootprint Measure(string path, int references, CancellationToken ct)
    {
        long bytes = 0;
        int files = 0, entries = 0;
        bool skippedReparse = false;
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    return new(path, 0, 0, false, "Reparse point skipped", references);
                return new(path, info.Length, 1, true, null, references);
            }
            if (!Directory.Exists(path))
                return new(path, 0, 0, false, "Local path not found", references);
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((path, 0));
            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var (dir, depth) = queue.Dequeue();
                var directory = new DirectoryInfo(dir);
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return new(path, bytes, files, false, "Reparse directory skipped", references);
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (++entries > MaxEntriesPerBundle)
                        return new(path, bytes, files, false, "File-entry budget exceeded", references);
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // Never traverse junctions or symlinks. The measured bytes are only partial.
                        skippedReparse = true;
                        continue;
                    }
                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        if (depth >= MaxDepth)
                            return new(path, bytes, files, false, "Directory-depth budget exceeded", references);
                        queue.Enqueue((entry.FullName, depth + 1));
                    }
                    else
                    {
                        bytes = checked(bytes + new FileInfo(entry.FullName).Length);
                        files++;
                    }
                }
            }
            return skippedReparse
                ? new(path, bytes, files, false,
                    "Reparse-point children excluded: bundle measurement is incomplete", references)
                : new(path, bytes, files, true, null, references);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException or NotSupportedException or OverflowException)
        {
            return new(path, bytes, files, false, ex.GetType().Name + ": unable to measure complete bundle", references);
        }
    }
}
