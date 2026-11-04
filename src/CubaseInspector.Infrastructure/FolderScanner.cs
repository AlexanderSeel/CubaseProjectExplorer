using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

public sealed class FolderScanner(IProjectInspector inspector) : IFolderScanner
{
    // Directory enumeration and several reader stages are synchronous CPU/filesystem work.
    // Keep the complete scan pipeline off the UI synchronization context; IProgress<T>
    // still marshals updates to the caller's context when it is a Progress<T> instance.
    public Task<Result<FolderReport>> ScanAsync(string path, IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default, ProjectScanTypes? projectTypes = null,
        bool retainFullSnapshots = true,
        IReadOnlyDictionary<string, ProjectLibraryEntry>? reusableProjects = null)
        => Task.Run(() => ScanCoreAsync(path, progress, cancellationToken,
            projectTypes ?? ProjectScanTypes.All, retainFullSnapshots, reusableProjects));

    private async Task<Result<FolderReport>> ScanCoreAsync(string path, IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken, ProjectScanTypes projectTypes, bool retainFullSnapshots,
        IReadOnlyDictionary<string, ProjectLibraryEntry>? reusableProjects)
    {
        if (!projectTypes.AnyEnabled)
            return Result<FolderReport>.Fail("NO_PROJECT_TYPES", "Enable at least one project file type before scanning.");
        if (!Directory.Exists(path)) return Result<FolderReport>.Fail("FOLDER_NOT_FOUND", "Folder not found.");
        var files = new List<string>();
        var reusedProjects = new List<ProjectLibraryEntry>();
        var errors = new System.Collections.Concurrent.ConcurrentBag<(string Path, string Error)>();
        var skippedUnrelatedBackups = 0;
        try
        {
            // Discovery is indeterminate: total candidate files are not yet known.
            progress?.Report(new ScanProgress(0, 0, path, Stage: ScanStage.Discovering));
            // No recursive symlink traversal; bounded walk prevents infinite or runaway scans.
            var exploredDirectories = 0;
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((path, 0));
            while (queue.Count > 0 && files.Count + reusedProjects.Count < 10000)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (current, depth) = queue.Dequeue();
                exploredDirectories++;
                if (exploredDirectories == 1 || exploredDirectories % 8 == 0)
                    progress?.Report(new ScanProgress(exploredDirectories, files.Count + reusedProjects.Count,
                        current, Stage: ScanStage.Discovering));
                try
                {
                    // Enumerate lazily so cancellation is checked between entries and
                    // large directories do not need a second in-memory path list.
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        FileAttributes attrs;
                        try { attrs = File.GetAttributes(entry); }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            errors.Add((entry, "ENTRY_ACCESS: " + e.Message));
                            continue;
                        }
                        if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attrs & FileAttributes.Directory) != 0 && depth < 12) queue.Enqueue((entry, depth + 1));
                        else if ((attrs & FileAttributes.Directory) == 0)
                        {
                            if ((projectTypes.Cpr && entry.EndsWith(".cpr", StringComparison.OrdinalIgnoreCase)) ||
                                (projectTypes.Dawproject && entry.EndsWith(".dawproject", StringComparison.OrdinalIgnoreCase)))
                                AddProjectOrReuse(entry);
                            else if (projectTypes.Bak && entry.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                            {
                                if (CprFileIdentity.HasCprHeader(entry)) AddProjectOrReuse(entry);
                                else skippedUnrelatedBackups++;
                            }
                        }
                        if (files.Count + reusedProjects.Count >= 10000) break;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { errors.Add((current, "FOLDER_ACCESS: " + e.Message)); }
            }
            // Publish the determinate phase before the first parse, even for empty folders.
            var totalProjects = files.Count + reusedProjects.Count;
            progress?.Report(new ScanProgress(reusedProjects.Count, totalProjects, path, Stage: ScanStage.Inspecting,
                Reused: reusedProjects.Count));
            var results = new ProjectSnapshot?[files.Count];
            var summaries = retainFullSnapshots ? null : new ProjectLibraryEntry?[files.Count];
            var assetIndexes = retainFullSnapshots ? null : new ProjectAssetIndex?[files.Count];
            void StoreResult(int index, ProjectSnapshot project)
            {
                if (retainFullSnapshots)
                {
                    results[index] = project;
                    return;
                }
                summaries![index] = ProjectLibraryBrowser.ToSummary(ProjectLibraryBrowser.FromFile(project));
                assetIndexes![index] = ProjectAssetIndexer.Build(project);
                // Project opening always re-runs the reader. Persist a compact scan
                // snapshot so thousands of routing, track and event records do not
                // remain live through the end-of-scan library save. Keep plug-in
                // references for the separate library-wide plug-in inventory.
                results[index] = project with
                {
                    Tracks = [], Routing = [], Warnings = [], MediaReferences = [], TimelineItems = [],
                    Metadata = null, MidiNotes = [], NativeMetadataCandidates = [],
                    NativeMixerObservations = [], EqBands = [], WarpPoints = [],
                    NativeMidiNoteCandidates = [], NativeAudioMediaCandidates = [], AutomationLanes = []
                };
            }
            // DAWproject inspection materializes a bounded XML document plus its
            // normalized records. Keep only one such tree live at a time even
            // when the surrounding CPR scan uses several workers.
            using var dawprojectReadGate = new SemaphoreSlim(1, 1);
            var completed = reusedProjects.Count;
            var interrupted = false;
            try
            {
                await Parallel.ForEachAsync(Enumerable.Range(0, files.Count), new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    // CPR parsing can produce sizeable evidence snapshots. One reader
                    // keeps the transient parser footprint bounded; progress remains
                    // responsive and checksums are streamed separately.
                    MaxDegreeOfParallelism = 1
                }, async (index, ct) =>
                {
                    progress?.Report(new ScanProgress(Volatile.Read(ref completed), totalProjects,
                        files[index], Stage: ScanStage.Inspecting, Reused: reusedProjects.Count));
                    Result<ProjectSnapshot> result;
                    if (Path.GetExtension(files[index]).Equals(".dawproject", StringComparison.OrdinalIgnoreCase))
                    {
                        await dawprojectReadGate.WaitAsync(ct);
                        try { result = await inspector.InspectAsync(files[index], ct); }
                        finally { dawprojectReadGate.Release(); }
                    }
                    else result = await inspector.InspectAsync(files[index], ct);
                    ProjectSnapshot? project = null;
                    AppError? failure = result.Error?.Code == "CANCELLED" ? null : result.Error;
                    if (result.IsSuccess && result.Value is not null)
                    {
                        project = result.Value;
                        progress?.Report(new ScanProgress(completed, totalProjects, files[index], Stage: ScanStage.Hashing,
                            Reused: reusedProjects.Count));
                        try
                        {
                            var checksum = await ProjectContentChecksum.ComputeAsync(files[index], ct,
                                (processedBytes, totalBytes) => progress?.Report(new ScanProgress(
                                    Volatile.Read(ref completed), totalProjects, files[index], Stage: ScanStage.Hashing,
                                    BytesProcessed: processedBytes, TotalBytes: totalBytes, Reused: reusedProjects.Count)));
                            project = project with { SourceSha256 = checksum };
                            StoreResult(index, project);
                        }
                        catch (ProjectChecksumSkippedException exception)
                        {
                            var warning = "Source SHA-256 was skipped for this large file; duplicate detection falls back to file path. " + exception.Message;
                            StoreResult(index, project with { Warnings = project.Warnings.Append(warning).ToArray() });
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                            or ArgumentException or NotSupportedException or System.Security.SecurityException)
                        {
                            var warning = "Source SHA-256 could not be calculated; duplicate detection falls back to file path. " + exception.Message;
                            project = project with { Warnings = project.Warnings.Append(warning).ToArray() };
                            StoreResult(index, project);
                            failure = new AppError("CHECKSUM_FAILED", warning);
                            errors.Add((files[index], "CHECKSUM_FAILED: " + warning));
                        }
                    }
                    else if (failure is not null)
                        errors.Add((files[index], failure.Message));
                    progress?.Report(new ScanProgress(Interlocked.Increment(ref completed), totalProjects, files[index],
                        retainFullSnapshots ? project : null, failure, Reused: reusedProjects.Count));
                });
            }
            catch (OperationCanceledException) { interrupted = true; }
            var reusedSummaries = reusedProjects.ToArray();
            var allProjects = results.OfType<ProjectSnapshot>().Concat(reusedSummaries.Select(entry => entry.Project)).ToList();
            var allSummaries = summaries is null ? null : summaries.OfType<ProjectLibraryEntry>().Concat(reusedSummaries).ToArray();
            return Result<FolderReport>.Ok(new FolderReport(allProjects,
                errors.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList(),
                interrupted || totalProjects >= 10000, totalProjects, completed, skippedUnrelatedBackups,
                allSummaries,
                assetIndexes?.OfType<ProjectAssetIndex>().ToArray(), projectTypes, reusedProjects.Count));

            void AddProjectOrReuse(string filePath)
            {
                if (TryReusableProject(filePath, out var cached)) reusedProjects.Add(cached);
                else files.Add(filePath);
            }

            bool TryReusableProject(string filePath, out ProjectLibraryEntry cached)
            {
                cached = null!;
                if (retainFullSnapshots || reusableProjects is null ||
                    !reusableProjects.TryGetValue(Path.GetFullPath(filePath), out var entry) ||
                    ProjectLibraryBrowser.NormalizeSha256(entry.SourceSha256 ?? entry.Project.SourceSha256) is null ||
                    entry.SizeBytes is not long size || entry.LastModifiedUtc is not DateTimeOffset modified)
                    return false;
                try
                {
                    var file = new FileInfo(filePath);
                    if (file.Length != size || file.LastWriteTimeUtc != modified.UtcDateTime) return false;
                    cached = entry;
                    return true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { return false; }
            }
        }
        catch (OperationCanceledException) { return Result<FolderReport>.Fail("CANCELLED", "Folder scan cancelled."); }
        catch (UnauthorizedAccessException e) { return Result<FolderReport>.Fail("ACCESS_DENIED", e.Message); }
        catch (IOException e) { return Result<FolderReport>.Fail("IO_ERROR", e.Message); }
    }
}
