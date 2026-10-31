using CubaseInspector.Core;
using Microsoft.Win32;

namespace CubaseInspector.Infrastructure;

/// <summary>Enumerates installed VST bundle/file names, never loads or executes DLLs.</summary>
public sealed class PluginScannerService : IPluginScannerService
{
    private const int MaxEntries = 100_000;

    public Task<Result<IReadOnlyList<InstalledPlugin>>> ScanAsync(
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private static Result<IReadOnlyList<InstalledPlugin>> Scan(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return Result<IReadOnlyList<InstalledPlugin>>.Fail("WINDOWS_REQUIRED", "Plugin registry scanning requires Windows.");
        try
        {
            var roots = DiscoverScanRoots();
            var results = new List<InstalledPlugin>();
            var queue = new Queue<(string Path, int Depth)>(roots.Where(p => p.IsScannable).Select(p => (p.Path, 0)));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (queue.Count > 0 && seen.Count < MaxEntries)
            {
                ct.ThrowIfCancellationRequested();
                var (path, depth) = queue.Dequeue();
                if (!seen.Add(path) || depth > 20) continue;
                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        continue; // A user-defined/root registry path may itself be a junction.
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                    System.Security.SecurityException or ArgumentException) { continue; }
                progress?.Report(new ScanProgress(seen.Count, 0, path));
                IEnumerable<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(path).ToList(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        var extension = Path.GetExtension(entry);
                        if (extension.Equals(".vst3", StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new InstalledPlugin(Path.GetFileNameWithoutExtension(entry), entry, "VST3",
                                Vst3ModuleInfoReader.Read(entry)));
                            continue; // VST3 bundle should be counted once.
                        }
                        if ((attributes & FileAttributes.Directory) == 0 && extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new InstalledPlugin(Path.GetFileNameWithoutExtension(entry), entry, "Potential VST2 DLL"));
                        }
                        else if ((attributes & FileAttributes.Directory) != 0 && depth < 20) queue.Enqueue((entry, depth + 1));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* skip missing/denied files */ }
                }
            }
            return Result<IReadOnlyList<InstalledPlugin>>.Ok(results
                .GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList());
        }
        catch (OperationCanceledException) { return Result<IReadOnlyList<InstalledPlugin>>.Fail("CANCELLED", "Plugin scan cancelled."); }
    }

    /// <summary>Read-only source overview; Cubase preference files are not parsed.</summary>
    public static IReadOnlyList<PluginScanRootInfo> DiscoverScanRoots()
    {
        var candidates = new List<(string Path, string Source)>();
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add((Path.Combine(programs, "Common Files", "VST3"), "Windows default VST3"));
        candidates.Add((Path.Combine(local, "Programs", "Common", "VST3"), "Windows user VST3"));
        candidates.Add((Path.Combine(programs, "VSTPlugins"), "Legacy VST2 candidate"));
        candidates.Add((Path.Combine(x86, "Common Files", "VST3"), "Windows 32-bit VST3"));
        candidates.Add((Path.Combine(x86, "VSTPlugins"), "Legacy VST2 candidate"));
        try
        {
            var steinberg = Path.Combine(programs, "Steinberg");
            if (Directory.Exists(steinberg) && (File.GetAttributes(steinberg) & FileAttributes.ReparsePoint) == 0)
            {
                var newest = Directory.EnumerateDirectories(steinberg, "Cubase *").Take(64)
                    .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && Directory.Exists(Path.Combine(path, "VST3")))
                    .OrderByDescending(path => Version.TryParse(Path.GetFileName(path)[7..], out var version) ? version :
                        int.TryParse(Path.GetFileName(path)[7..], out var major) ? new Version(major, 0) : new Version(0, 0))
                    .FirstOrDefault();
                if (newest is not null) candidates.Add((Path.Combine(newest, "VST3"), "Newest standard Cubase installation · bundled VST3 resources"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        foreach (var path in PluginScanRootSettings.Load()) candidates.Add((path, "User-added folder"));
        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, label) in new[] {
                (Registry.CurrentUser, "HKCU"), (Registry.LocalMachine, "HKLM") })
            {
                foreach (var keyName in new[] { @"SOFTWARE\VST", @"SOFTWARE\Steinberg\VST2",
                    @"SOFTWARE\WOW6432Node\VST" })
                {
                    try
                    {
                        using var key = hive.OpenSubKey(keyName);
                        if (key is null) continue;
                        foreach (var name in key.GetValueNames())
                            if (name.Contains("Path", StringComparison.OrdinalIgnoreCase) &&
                                key.GetValue(name) is string path)
                                candidates.Add((path, "Registry " + label + @"\" + keyName));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                        System.Security.SecurityException) { }
                }
            }
        }
        return PluginScanRootInventory.Merge(candidates);
    }

    public IReadOnlyList<PluginCheck> Check(ProjectSnapshot project, IReadOnlyList<InstalledPlugin> installed)
    {
        var files = installed.GroupBy(p => Normalize(p.Name))
            .ToDictionary(g => g.Key, g => g.FirstOrDefault(), StringComparer.OrdinalIgnoreCase);
        var byCid = installed.SelectMany(bundle => (bundle.Classes ?? [])
                .Select(klass => (Bundle: bundle, Class: klass)))
            .GroupBy(pair => pair.Class.Cid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        return project.Plugins.Select(p =>
        {
            if (p.Evidence != EvidenceLevel.Confirmed)
                return new PluginCheck(p, PluginStatus.Unverified, null);
            // Exact 128-bit component class ID, not a DAW export's arbitrary instance ID.
            var cid = Vst3ClassIdentity.Canonical(p.Id);
            if (cid is not null && p.Format.Contains("vst3", StringComparison.OrdinalIgnoreCase) &&
                byCid.TryGetValue(cid, out var candidates) && candidates.Length == 1)
            {
                var match = candidates[0];
                return new PluginCheck(p, PluginStatus.InstalledByClassId,
                    match.Bundle.Path, match.Class);
            }
            // Filename matching is only a weak hint. Even a module sharing its
            // name is not a class-ID match if its actual CID is different.
            if (string.IsNullOrWhiteSpace(p.Name))
                return new PluginCheck(p, PluginStatus.Unverified, null);
            files.TryGetValue(Normalize(p.Name), out var nameMatch);
            return new PluginCheck(p, nameMatch is null ? PluginStatus.Unverified :
                PluginStatus.InstalledByName, nameMatch?.Path);
        }).ToList();
    }
    private static string Normalize(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
