using System.Security;
using System.Security.Cryptography;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Finds exact byte-identical external audio files only after an explicit user request.</summary>
public sealed class ProjectMediaDuplicateService(IProjectMediaPreviewService previewService) : IProjectMediaDuplicateService
{
    public const int MaximumFiles = 500;
    public const long MaximumBytes = 1024L * 1024 * 1024;

    public async Task<MediaDuplicateScanReport> ScanAsync(IReadOnlyList<ProjectMediaReferenceSelection> selections,
        IProgress<MediaDuplicateScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var sources = BuildSources(selections);
        var ordered = sources.OrderBy(source => source.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var grouped = new Dictionary<string, List<MediaDuplicateSource>>(StringComparer.Ordinal);
        var analyzed = 0;
        var skipped = 0;
        long totalBytes = 0;
        var limitReached = ordered.Length > MaximumFiles;
        var completed = 0;
        var totalFiles = Math.Min(ordered.Length, MaximumFiles);

        foreach (var source in ordered.Take(MaximumFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreparedMediaPreview? preparedPreview = null;
            try
            {
                var firstUse = source.Uses[0];
                var project = selections.First(selection =>
                    string.Equals(selection.Project.FilePath, firstUse.ProjectPath, StringComparison.OrdinalIgnoreCase)).Project;
                var reference = project.MediaReferences![source.ReferenceIndex];
                var prepared = await previewService.PrepareAsync(project, reference, cancellationToken);
                if (!prepared.IsSuccess || prepared.Value is null)
                {
                    skipped++;
                    continue;
                }
                preparedPreview = prepared.Value;
                if (!preparedPreview.SourceUri.IsFile)
                {
                    skipped++;
                    continue;
                }
                var tempPath = preparedPreview.SourceUri.LocalPath;
                var file = new FileInfo(tempPath);
                if (file.Length <= 0 || file.Length > MaximumBytes - totalBytes)
                {
                    limitReached = true;
                    break;
                }
                await using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                totalBytes += file.Length;
                analyzed++;
                if (!grouped.TryGetValue(hash, out var group)) grouped[hash] = group = [];
                group.Add(new MediaDuplicateSource(source.Key, file.Length, source.Uses));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            { skipped++; }
            finally
            {
                if (preparedPreview is not null) previewService.Release(preparedPreview);
                completed++;
                progress?.Report(new MediaDuplicateScanProgress(completed, totalFiles, totalBytes));
            }
        }

        var duplicateGroups = grouped.Where(pair => pair.Value.Count > 1)
            .Select(pair => new MediaDuplicateGroup(pair.Key, pair.Value[0].Length,
                pair.Value.OrderBy(source => source.ResolvedPath, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderByDescending(group => group.Sources.Count * group.Length)
            .ThenBy(group => group.Sha256, StringComparer.Ordinal).ToArray();
        return new MediaDuplicateScanReport(analyzed, skipped, totalBytes, limitReached, duplicateGroups);
    }

    private static IReadOnlyList<SourceToHash> BuildSources(IReadOnlyList<ProjectMediaReferenceSelection> selections)
    {
        var sources = new Dictionary<string, SourceToHash>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            var project = selection.Project;
            if (project.Evidence != EvidenceLevel.Confirmed || project.MediaReferences is not { } media ||
                selection.ReferenceIndex < 0 || selection.ReferenceIndex >= media.Count) continue;
            var reference = media[selection.ReferenceIndex];
            if (reference.External != true || !TryResolve(project.FilePath, reference.Path, out var resolvedPath)) continue;

            if (!sources.TryGetValue(resolvedPath, out var source))
            {
                source = new SourceToHash(resolvedPath, selection.ReferenceIndex, []);
                sources[resolvedPath] = source;
            }
            var track = project.Tracks.GroupBy(item => item.Id, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Key == reference.TrackId && group.Count() == 1)?.Single().Name ?? "Track unknown";
            source.Uses.Add(new MediaDuplicateUse(project.Name, project.FilePath, reference.Path, track,
                Path.GetDirectoryName(resolvedPath) ?? "Folder unknown"));
        }
        return sources.Values.ToArray();
    }

    private static bool TryResolve(string projectPath, string declaredPath, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(declaredPath) || declaredPath.Length > 4096 || declaredPath.Contains('\0') ||
            (!Path.IsPathRooted(declaredPath) && Uri.TryCreate(declaredPath, UriKind.Absolute, out _))) return false;
        try
        {
            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            if (projectDirectory is null) return false;
            path = Path.GetFullPath(Path.IsPathRooted(declaredPath)
                ? declaredPath : Path.Combine(projectDirectory, declaredPath));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or SecurityException)
        { return false; }
    }

    private sealed record SourceToHash(string Key, int ReferenceIndex, List<MediaDuplicateUse> Uses);
}
