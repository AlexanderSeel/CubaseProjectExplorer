using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Creates a separate ZIP handoff containing explicitly external audio files and a manifest.</summary>
public sealed class ProjectMediaPackageService(IProjectMediaPreviewService previewService) : IProjectMediaPackageService
{
    public const int MaximumReferences = 2_000;
    public const long MaximumPackageBytes = 4L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<Result<MediaPackageReport>> CreateAsync(ProjectSnapshot project, string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (project.Evidence != EvidenceLevel.Confirmed ||
            !Path.GetExtension(project.FilePath).Equals(".dawproject", StringComparison.OrdinalIgnoreCase))
            return Result<MediaPackageReport>.Fail("MEDIA_PACKAGE_UNSUPPORTED", "Media collection is available only for confirmed DAWproject exports.");
        if (string.IsNullOrWhiteSpace(destinationPath))
            return Result<MediaPackageReport>.Fail("MEDIA_PACKAGE_DESTINATION_REQUIRED", "Choose a destination ZIP file.");

        string destination;
        try { destination = Path.GetFullPath(destinationPath); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { return Result<MediaPackageReport>.Fail("MEDIA_PACKAGE_DESTINATION_INVALID", "The selected destination path is invalid."); }
        try
        {
            if (string.Equals(destination, Path.GetFullPath(project.FilePath), StringComparison.OrdinalIgnoreCase))
                return Result<MediaPackageReport>.Fail("MEDIA_PACKAGE_SOURCE_PROTECTED", "The package destination cannot replace the source project.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { return Result<MediaPackageReport>.Fail("MEDIA_PACKAGE_SOURCE_INVALID", "The source project path is invalid."); }

        var references = project.MediaReferences ?? [];
        string? sourceDirectory;
        try { sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(project.FilePath)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { return Result<MediaPackageReport>.Fail("MEDIA_PACKAGE_SOURCE_INVALID", "The source project folder is invalid."); }
        if (sourceDirectory is not null && references.Any(reference => reference.External == true &&
                TryResolveExternal(sourceDirectory, reference.Path, out var mediaPath) &&
                string.Equals(destination, mediaPath, StringComparison.OrdinalIgnoreCase)))
            return Result<MediaPackageReport>.Fail("MEDIA_PACKAGE_SOURCE_PROTECTED", "The package destination matches a referenced media file.");

        var referenceResults = new List<MediaPackageReferenceResult>(Math.Min(references.Count, MaximumReferences + 1));
        var includedByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tempPackage = Path.Combine(Path.GetDirectoryName(destination)!, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        long totalBytes = 0;
        var includedFiles = 0;
        var destinationConflictsWithMedia = false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var file = new FileStream(tempPackage, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                var limit = Math.Min(references.Count, MaximumReferences);
                for (var index = 0; index < limit; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var reference = references[index];
                    var safePath = SafeReferencePath(reference.Path);
                    if (reference.External == false)
                    {
                        referenceResults.Add(new(safePath, reference.TrackId, "Embedded", null,
                            "The media is declared inside the source project and was not copied separately."));
                        continue;
                    }
                    if (reference.External is null)
                    {
                        referenceResults.Add(new(safePath, reference.TrackId, "Unknown", null,
                            "The export does not identify this media reference as external."));
                        continue;
                    }
                    if (includedByPath.TryGetValue(reference.Path, out var existingEntry))
                    {
                        referenceResults.Add(new(safePath, reference.TrackId, "Included duplicate reference", existingEntry,
                            "The same declared path was already added once to the package."));
                        continue;
                    }
                    if (totalBytes >= MaximumPackageBytes)
                    {
                        referenceResults.Add(new(safePath, reference.TrackId, "Not checked", null,
                            "The package byte limit was reached."));
                        continue;
                    }

                    var prepared = await previewService.PrepareAsync(project, reference, cancellationToken);
                    if (!prepared.IsSuccess || prepared.Value is null)
                    {
                        referenceResults.Add(new(safePath, reference.TrackId, "Unavailable", null,
                            prepared.Error?.Code + ": " + prepared.Error?.Message));
                        continue;
                    }
                    try
                    {
                        if (!prepared.Value.SourceUri.IsFile)
                        {
                            referenceResults.Add(new(safePath, reference.TrackId, "Unsupported", null,
                                "The prepared media source is not a local file."));
                            continue;
                        }
                        var sourcePath = Path.GetFullPath(prepared.Value.SourceUri.LocalPath);
                        if (string.Equals(destination, sourcePath, StringComparison.OrdinalIgnoreCase))
                        {
                            referenceResults.Add(new(safePath, reference.TrackId, "Not packaged", null,
                                "The selected package destination matches this referenced media file."));
                            destinationConflictsWithMedia = true;
                            break;
                        }
                        var sourceInfo = new FileInfo(sourcePath);
                        if (sourceInfo.Length <= 0 || sourceInfo.Length > ProjectMediaPreviewService.MaximumPreviewBytes)
                        {
                            referenceResults.Add(new(safePath, reference.TrackId, "Size limit", null,
                                "Each collected audio file must be no larger than 150 MiB."));
                            continue;
                        }
                        if (sourceInfo.Length > MaximumPackageBytes - totalBytes)
                        {
                            referenceResults.Add(new(safePath, reference.TrackId, "Not checked", null,
                                "Adding this file would exceed the 4 GiB package limit."));
                            continue;
                        }

                        var entryPath = CreateEntryPath(reference.Path);
                        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                        try
                        {
                            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                            await using var output = entry.Open();
                            var buffer = new byte[64 * 1024];
                            long copied = 0;
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var read = await input.ReadAsync(buffer, cancellationToken);
                                if (read == 0) break;
                                copied += read;
                                if (copied > ProjectMediaPreviewService.MaximumPreviewBytes || copied > sourceInfo.Length)
                                    throw new InvalidDataException("The source changed or exceeded the per-file limit while being packaged.");
                                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            }
                            if (copied != sourceInfo.Length || input.Length != sourceInfo.Length)
                                throw new InvalidDataException("The source length changed while being packaged.");
                            totalBytes += copied;
                            includedFiles++;
                            includedByPath[reference.Path] = entryPath;
                            referenceResults.Add(new(safePath, reference.TrackId, "Included", entryPath,
                                "Copied from an explicitly external, supported reference."));
                        }
                        catch
                        {
                            entry.Delete();
                            throw;
                        }
                    }
                    finally { previewService.Release(prepared.Value); }
                }
                if (references.Count > MaximumReferences)
                    referenceResults.Add(new("[additional references omitted]", null, "Not checked", null,
                        $"The report limit is {MaximumReferences:N0} references; {references.Count - MaximumReferences:N0} additional references were not processed."));

                var manifest = new PackageManifest(project.Name, "DAWproject", DateTimeOffset.UtcNow,
                    includedFiles, totalBytes, referenceResults);
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using (var output = manifestEntry.Open())
                    await JsonSerializer.SerializeAsync(output, manifest, JsonOptions, cancellationToken);
            }

            if (destinationConflictsWithMedia)
            {
                DeleteTemporary(tempPackage);
                return Result<MediaPackageReport>.Fail("MEDIA_PACKAGE_SOURCE_PROTECTED", "The package destination matches a referenced media file; no package was written.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPackage, destination, overwrite: true);
            return Result<MediaPackageReport>.Ok(new(destination, includedFiles, totalBytes, referenceResults));
        }
        catch (OperationCanceledException)
        {
            DeleteTemporary(tempPackage);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            DeleteTemporary(tempPackage);
            return Result<MediaPackageReport>.Fail("MEDIA_PACKAGE_FAILED", ex.Message);
        }
    }

    private static string CreateEntryPath(string declaredPath)
    {
        var normalized = declaredPath.Replace('/', '\\').Replace('\\', Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(normalized);
        foreach (var invalid in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(invalid, '_');
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..") fileName = "sample.audio";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(declaredPath)))[..16].ToLowerInvariant();
        return "media/" + hash + "_" + fileName;
    }

    private static string SafeReferencePath(string path) => Path.IsPathRooted(path) || Uri.TryCreate(path, UriKind.Absolute, out _)
        ? "[absolute or URI path omitted]" : path;

    private static bool TryResolveExternal(string projectDirectory, string declaredPath, out string resolvedPath)
    {
        resolvedPath = "";
        if (string.IsNullOrWhiteSpace(declaredPath) || declaredPath.Length > 4096 || declaredPath.Contains('\0') ||
            (!Path.IsPathRooted(declaredPath) && Uri.TryCreate(declaredPath, UriKind.Absolute, out _))) return false;
        try
        {
            resolvedPath = Path.GetFullPath(Path.IsPathRooted(declaredPath)
                ? declaredPath : Path.Combine(projectDirectory, declaredPath));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { return false; }
    }

    private static void DeleteTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record PackageManifest(string ProjectName, string Format, DateTimeOffset CreatedUtc,
        int IncludedFileCount, long IncludedBytes, IReadOnlyList<MediaPackageReferenceResult> References);
}
