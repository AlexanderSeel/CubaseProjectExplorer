using System.IO.Compression;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectMediaPreviewServiceTests
{
    [Fact]
    public async Task PreparesEmbeddedAudioInBoundedTemporaryCopyAndCanReleaseIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "session.dawproject");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        try
        {
            await using (var file = File.Create(projectPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                await using var stream = archive.CreateEntry("audio/kick.wav").Open();
                await stream.WriteAsync(bytes);
            }
            var originalArchive = await File.ReadAllBytesAsync(projectPath);
            var service = new ProjectMediaPreviewService();
            var result = await service.PrepareAsync(Project(projectPath),
                new ProjectMediaReference("audio/kick.wav", false, 1, 48000, 1, "track"));

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.NotNull(result.Value!.TemporaryFilePath);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(result.Value.TemporaryFilePath!));
            service.Release(result.Value);
            Assert.False(File.Exists(result.Value.TemporaryFilePath));
            Assert.Equal(originalArchive, await File.ReadAllBytesAsync(projectPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PreviewsExplicitAbsoluteExternalAudioButRejectsUnapprovedTypesAndLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var samplePath = Path.Combine(root, "vocal.wav");
        await File.WriteAllBytesAsync(samplePath, [1, 2, 3]);
        try
        {
            var service = new ProjectMediaPreviewService();
            var result = await service.PrepareAsync(Project(Path.Combine(root, "session.dawproject")),
                new ProjectMediaReference(samplePath, true, 1, 44100, 1, "track"));
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.NotNull(result.Value!.TemporaryFilePath);
            Assert.NotEqual(Path.GetFullPath(samplePath), result.Value.SourceUri.LocalPath);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(result.Value.TemporaryFilePath!));
            service.Release(result.Value);
            Assert.False(File.Exists(result.Value.TemporaryFilePath));

            var unsupported = await service.PrepareAsync(Project(Path.Combine(root, "session.dawproject")),
                new ProjectMediaReference(Path.Combine(root, "file.mid"), true, null, null, null));
            Assert.Equal("MEDIA_FORMAT_UNSUPPORTED", unsupported.Error?.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PreviewsExistingExternalAudioFromCandidateCprReference()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-native-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "session.cpr");
        var samplePath = Path.Combine(root, "audio", "kick.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(samplePath)!);
        await File.WriteAllBytesAsync(projectPath, "RIFF\0\0\0\0NUND"u8.ToArray());
        await File.WriteAllBytesAsync(samplePath, [1, 2, 3, 4]);
        try
        {
            var candidateProject = Project(projectPath) with { Evidence = EvidenceLevel.Candidate, Format = "CPR" };
            var service = new ProjectMediaPreviewService();
            var result = await service.PrepareAsync(candidateProject,
                new ProjectMediaReference("audio/kick.wav", true, null, null, null));

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(result.Value!.SourceUri.LocalPath));
            service.Release(result.Value);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task EmbeddedPreviewRejectsAmbiguousEntriesAndTraversalNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "session.dawproject");
        try
        {
            await using (var file = File.Create(projectPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                await using (var first = archive.CreateEntry("audio/kick.wav").Open()) await first.WriteAsync(new byte[] { 1 });
                await using (var second = archive.CreateEntry("audio/kick.wav").Open()) await second.WriteAsync(new byte[] { 2 });
            }
            var service = new ProjectMediaPreviewService();
            var ambiguous = await service.PrepareAsync(Project(projectPath),
                new ProjectMediaReference("audio/kick.wav", false, null, null, null));
            Assert.Equal("MEDIA_ENTRY_AMBIGUOUS", ambiguous.Error?.Code);
            var traversal = await service.PrepareAsync(Project(projectPath),
                new ProjectMediaReference("../audio/kick.wav", false, null, null, null));
            Assert.Equal("MEDIA_ARCHIVE_PATH_UNSUPPORTED", traversal.Error?.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ProjectSnapshot Project(string path) => new(path, "Session", "DAWproject", "15", 120,
        EvidenceLevel.Confirmed, [], [], [], []);
}
