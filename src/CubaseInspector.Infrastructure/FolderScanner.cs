using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

public sealed class FolderScanner(IProjectInspector inspector) : IFolderScanner
{
    // Directory enumeration and several reader stages are synchronous CPU/filesystem work.
    // Keep the complete scan pipeline off the UI synchronization context; IProgress<T>
    // still marshals updates to the caller's context when it is a Progress<T> instance.
    public Task<Result<FolderReport>> ScanAsync(string path, IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => ScanCoreAsync(path, progress, cancellationToken));

    private async Task<Result<FolderReport>> ScanCoreAsync(string path, IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path)) return Result<FolderReport>.Fail("FOLDER_NOT_FOUND", "Folder not found.");
        var files = new List<string>();
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
            while (queue.Count > 0 && files.Count < 10000)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (current, depth) = queue.Dequeue();
                exploredDirectories++;
                if (exploredDirectories == 1 || exploredDirectories % 8 == 0)
                    progress?.Report(new ScanProgress(exploredDirectories, files.Count,
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
                            if (entry.EndsWith(".cpr", StringComparison.OrdinalIgnoreCase) ||
                                entry.EndsWith(".dawproject", StringComparison.OrdinalIgnoreCase))
                                files.Add(entry);
                            else if (entry.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                            {
                                if (CprFileIdentity.HasCprHeader(entry)) files.Add(entry);
                                else skippedUnrelatedBackups++;
                            }
                        }
                        if (files.Count >= 10000) break;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { errors.Add((current, "FOLDER_ACCESS: " + e.Message)); }
            }
            // Publish the determinate phase before the first parse, even for empty folders.
            progress?.Report(new ScanProgress(0, files.Count, path, Stage: ScanStage.Inspecting));
            var results = new ProjectSnapshot?[files.Count];
            var completed = 0;
            var interrupted = false;
            try
            {
                await Parallel.ForEachAsync(Enumerable.Range(0, files.Count), new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount)
                }, async (index, ct) =>
                {
                    var result = await inspector.InspectAsync(files[index], ct);
                    ProjectSnapshot? project = null;
                    AppError? failure = result.Error?.Code == "CANCELLED" ? null : result.Error;
                    if (result.IsSuccess && result.Value is not null)
                    {
                        project = result.Value;
                        progress?.Report(new ScanProgress(completed, files.Count, files[index], Stage: ScanStage.Hashing));
                        try
                        {
                            var checksum = await ProjectContentChecksum.ComputeAsync(files[index], ct);
                            project = project with { SourceSha256 = checksum };
                            results[index] = project;
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                            or ArgumentException or NotSupportedException or System.Security.SecurityException)
                        {
                            var warning = "Source SHA-256 could not be calculated; duplicate detection falls back to file path. " + exception.Message;
                            project = project with { Warnings = project.Warnings.Append(warning).ToArray() };
                            results[index] = project;
                            failure = new AppError("CHECKSUM_FAILED", warning);
                            errors.Add((files[index], "CHECKSUM_FAILED: " + warning));
                        }
                    }
                    else if (failure is not null)
                        errors.Add((files[index], failure.Message));
                    progress?.Report(new ScanProgress(Interlocked.Increment(ref completed), files.Count, files[index],
                        project, failure));
                });
            }
            catch (OperationCanceledException) { interrupted = true; }
            return Result<FolderReport>.Ok(new FolderReport(results.OfType<ProjectSnapshot>().ToList(),
                errors.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList(),
                interrupted || files.Count >= 10000, files.Count, completed, skippedUnrelatedBackups));
        }
        catch (OperationCanceledException) { return Result<FolderReport>.Fail("CANCELLED", "Folder scan cancelled."); }
        catch (UnauthorizedAccessException e) { return Result<FolderReport>.Fail("ACCESS_DENIED", e.Message); }
        catch (IOException e) { return Result<FolderReport>.Fail("IO_ERROR", e.Message); }
    }
}
