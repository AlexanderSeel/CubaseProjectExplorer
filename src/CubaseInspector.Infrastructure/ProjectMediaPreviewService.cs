using System.IO.Compression;
using System.Security;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Prepares a user-selected audio reference for preview without modifying its source project.</summary>
public sealed class ProjectMediaPreviewService : IProjectMediaPreviewService
{
    public const long MaximumPreviewBytes = 150L * 1024 * 1024;
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".wave", ".aif", ".aiff", ".mp3", ".ogg", ".flac", ".m4a", ".aac" };

    public async Task<Result<PreparedMediaPreview>> PrepareAsync(ProjectSnapshot project,
        ProjectMediaReference reference, CancellationToken cancellationToken = default)
    {
        var confirmedExport = project.Evidence == EvidenceLevel.Confirmed &&
            Path.GetExtension(project.FilePath).Equals(".dawproject", StringComparison.OrdinalIgnoreCase);
        var nativeExternalCandidate = project.Evidence == EvidenceLevel.Candidate &&
            CprFileIdentity.IsCprOrValidBackup(project.FilePath) &&
            CprFileIdentity.HasCprHeader(project.FilePath) && reference.External == true;
        if (!confirmedExport && !nativeExternalCandidate)
            return Result<PreparedMediaPreview>.Fail("MEDIA_PREVIEW_UNSUPPORTED", "Preview requires a confirmed DAWproject media reference or a native CPR external-media candidate.");
        var extension = Path.GetExtension(reference.Path);
        if (!AudioExtensions.Contains(extension))
            return Result<PreparedMediaPreview>.Fail("MEDIA_FORMAT_UNSUPPORTED", "This media file type is not enabled for preview.");
        if (reference.External is null)
            return Result<PreparedMediaPreview>.Fail("MEDIA_LOCATION_UNKNOWN", "The export does not say whether this reference is embedded or external.");

        if (reference.External == true)
        {
            if (!TryResolveExternal(project.FilePath, reference.Path, out var path))
                return Result<PreparedMediaPreview>.Fail("MEDIA_PATH_UNSUPPORTED", "The declared external path is not supported.");
            var inspected = InspectFile(path);
            if (inspected.Error is not null) return Result<PreparedMediaPreview>.Fail(inspected.Error.Code, inspected.Error.Message);
            return await CopyExternalFileAsync(path, extension, inspected.Length, cancellationToken);
        }

        return await CopyEmbeddedEntryAsync(project.FilePath, reference.Path, extension, cancellationToken);
    }

    public void Release(PreparedMediaPreview? preview)
    {
        if (preview?.TemporaryFilePath is not { } path) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool TryResolveExternal(string projectPath, string declaredPath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(declaredPath) || declaredPath.Length > 4096 || declaredPath.Contains('\0') ||
            (!Path.IsPathRooted(declaredPath) && Uri.TryCreate(declaredPath, UriKind.Absolute, out _))) return false;
        try
        {
            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            if (baseDirectory is null) return false;
            fullPath = Path.GetFullPath(Path.IsPathRooted(declaredPath)
                ? declaredPath : Path.Combine(baseDirectory, declaredPath));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or SecurityException)
        { return false; }
    }

    private static (AppError? Error, long Length) InspectFile(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
                return (new("MEDIA_PATH_UNSUPPORTED", "The resolved path has no filesystem root."), 0);
            var segments = path[root.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            for (var i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return (new("MEDIA_LINK_UNSUPPORTED", "Preview does not traverse link/reparse path components."), 0);
                if (i < segments.Length - 1 && (attributes & FileAttributes.Directory) == 0)
                    return (new("MEDIA_MISSING", "A folder in the media path does not exist."), 0);
                if (i == segments.Length - 1 && (attributes & FileAttributes.Directory) != 0)
                    return (new("MEDIA_NOT_FILE", "The media reference points to a directory."), 0);
            }
            if (segments.Length == 0) return (new("MEDIA_NOT_FILE", "The media reference points to a filesystem root."), 0);
            var length = new FileInfo(path).Length;
            if (length is <= 0 or > MaximumPreviewBytes)
                return (new("MEDIA_SIZE_LIMIT", $"Preview files must contain 1 byte to {MaximumPreviewBytes / (1024 * 1024)} MiB."), length);
            return (null, length);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return (new("MEDIA_MISSING", "No file exists at the declared media path."), 0); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        { return (new("MEDIA_INACCESSIBLE", "Access to the declared media file was denied."), 0); }
        catch (IOException)
        { return (new("MEDIA_INACCESSIBLE", "The file system could not prepare this media file."), 0); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        { return (new("MEDIA_PATH_UNSUPPORTED", "The declared media path is not supported."), 0); }
    }

    private static async Task<Result<PreparedMediaPreview>> CopyEmbeddedEntryAsync(string projectPath,
        string declaredPath, string extension, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(declaredPath) || declaredPath.Length > 4096 ||
            Path.IsPathRooted(declaredPath) || Uri.TryCreate(declaredPath, UriKind.Absolute, out _))
            return Result<PreparedMediaPreview>.Fail("MEDIA_ARCHIVE_PATH_UNSUPPORTED", "The embedded archive path is invalid.");
        var entryName = declaredPath.Replace('\\', '/');
        if (entryName.Split('/').Any(segment => segment is ".." or ""))
            return Result<PreparedMediaPreview>.Fail("MEDIA_ARCHIVE_PATH_UNSUPPORTED", "The embedded archive path contains an unsupported component.");
        string? tempPath = null;
        try
        {
            await using var archiveFile = new FileStream(projectPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read, leaveOpen: false);
            var matches = archive.Entries.Where(entry => entry.FullName == entryName).Take(2).ToArray();
            if (matches.Length != 1)
                return Result<PreparedMediaPreview>.Fail(matches.Length == 0 ? "MEDIA_ENTRY_MISSING" : "MEDIA_ENTRY_AMBIGUOUS",
                    matches.Length == 0 ? "The embedded audio entry is missing from the project archive." : "The archive contains duplicate entries for this media path.");
            var entry = matches[0];
            if (entry.Length is <= 0 or > MaximumPreviewBytes ||
                (entry.Length > 4096 && entry.Length > Math.Max(1, entry.CompressedLength) * 200))
                return Result<PreparedMediaPreview>.Fail("MEDIA_SIZE_LIMIT", "The embedded audio entry exceeds preview size or compression-ratio limits.");

            var tempDirectory = Path.Combine(Path.GetTempPath(), "CubaseProjectExplorer", "SamplePreview");
            Directory.CreateDirectory(tempDirectory);
            tempPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + extension);
            await using var input = entry.Open();
            await using var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > MaximumPreviewBytes || total > entry.Length)
                    throw new InvalidDataException("Embedded audio exceeded its declared or configured size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (total != entry.Length) throw new InvalidDataException("Embedded audio length did not match its archive declaration.");
            await output.FlushAsync(cancellationToken);
            return Result<PreparedMediaPreview>.Ok(new(new Uri(tempPath), tempPath));
        }
        catch (OperationCanceledException)
        {
            DeleteTemporary(tempPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            DeleteTemporary(tempPath);
            return Result<PreparedMediaPreview>.Fail("MEDIA_PREVIEW_FAILED", ex.Message);
        }
    }

    private static async Task<Result<PreparedMediaPreview>> CopyExternalFileAsync(string sourcePath,
        string extension, long expectedLength, CancellationToken cancellationToken)
    {
        string? tempPath = null;
        try
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "CubaseProjectExplorer", "SamplePreview");
            Directory.CreateDirectory(tempDirectory);
            tempPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + extension);
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length != expectedLength || input.Length is <= 0 or > MaximumPreviewBytes)
                throw new InvalidDataException("The external audio file changed or exceeded its size limit.");
            await using var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > MaximumPreviewBytes || total > expectedLength)
                    throw new InvalidDataException("External audio exceeded its declared or configured size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (total != expectedLength || input.Length != expectedLength)
                throw new InvalidDataException("External audio length changed while being copied.");
            await output.FlushAsync(cancellationToken);
            return Result<PreparedMediaPreview>.Ok(new(new Uri(tempPath), tempPath));
        }
        catch (OperationCanceledException)
        {
            DeleteTemporary(tempPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException or ArgumentException)
        {
            DeleteTemporary(tempPath);
            return Result<PreparedMediaPreview>.Fail("MEDIA_PREVIEW_FAILED", ex.Message);
        }
    }

    private static void DeleteTemporary(string? path)
    {
        if (path is null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
