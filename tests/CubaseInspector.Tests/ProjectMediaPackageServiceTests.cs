using System.IO.Compression;
using System.Text.Json;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectMediaPackageServiceTests
{
    [Fact]
    public async Task CollectsExplicitExternalAudioOnceAndWritesPrivacySafeManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "session.dawproject");
        var mediaPath = Path.Combine(root, "audio", "kick.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        var originalProject = new byte[] { 9, 8, 7, 6 };
        var originalMedia = new byte[] { 1, 2, 3, 4, 5 };
        var destination = Path.Combine(root, "handoff.zip");
        try
        {
            await File.WriteAllBytesAsync(projectPath, originalProject);
            await File.WriteAllBytesAsync(mediaPath, originalMedia);
            var external = Reference("audio/kick.wav", true);
            var project = Project(projectPath,
            [
                external with { TrackId = "track-a" },
                external with { TrackId = "track-b" },
                Reference("audio/embedded.wav", false),
                Reference(Path.Combine(root, "audio", "private-vocal.wav"), true),
                Reference("audio/unknown.wav", null)
            ]);

            var service = new ProjectMediaPackageService(new ProjectMediaPreviewService());
            var result = await service.CreateAsync(project, destination);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(1, result.Value!.IncludedFileCount);
            Assert.Equal(originalMedia.Length, result.Value.IncludedBytes);
            Assert.Equal("Included", result.Value.References[0].Status);
            Assert.Equal("Included duplicate reference", result.Value.References[1].Status);
            Assert.Equal("Embedded", result.Value.References[2].Status);
            Assert.Equal("Unavailable", result.Value.References[3].Status);
            Assert.Equal("Unknown", result.Value.References[4].Status);

            using var archive = ZipFile.OpenRead(destination);
            var mediaEntry = Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("media/", StringComparison.Ordinal));
            await using var input = mediaEntry.Open();
            using var copied = new MemoryStream();
            await input.CopyToAsync(copied);
            Assert.Equal(originalMedia, copied.ToArray());
            var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
            using var manifestStream = manifestEntry.Open();
            using var manifest = await JsonDocument.ParseAsync(manifestStream);
            var serialized = manifest.RootElement.GetRawText();
            Assert.Contains("Included duplicate reference", serialized);
            Assert.DoesNotContain(root, serialized, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(originalProject, await File.ReadAllBytesAsync(projectPath));
            Assert.Equal(originalMedia, await File.ReadAllBytesAsync(mediaPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsDestinationThatWouldReplaceSourceProjectOrReferencedMedia()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "session.dawproject");
        var mediaPath = Path.Combine(root, "vocal.wav");
        var projectBytes = new byte[] { 7, 7, 7 };
        var mediaBytes = new byte[] { 1, 4, 1, 4 };
        try
        {
            await File.WriteAllBytesAsync(projectPath, projectBytes);
            await File.WriteAllBytesAsync(mediaPath, mediaBytes);
            var project = Project(projectPath, [Reference(mediaPath, true)]);
            var service = new ProjectMediaPackageService(new ProjectMediaPreviewService());

            var source = await service.CreateAsync(project, projectPath);
            var media = await service.CreateAsync(project, mediaPath);

            Assert.Equal("MEDIA_PACKAGE_SOURCE_PROTECTED", source.Error?.Code);
            Assert.Equal("MEDIA_PACKAGE_SOURCE_PROTECTED", media.Error?.Code);
            Assert.Equal(projectBytes, await File.ReadAllBytesAsync(projectPath));
            Assert.Equal(mediaBytes, await File.ReadAllBytesAsync(mediaPath));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RefusesCandidateOnlyProjectAndDoesNotCreatePackage()
    {
        var path = Path.Combine(Path.GetTempPath(), "candidate-" + Guid.NewGuid().ToString("N") + ".dawproject");
        var destination = path + ".zip";
        try
        {
            var project = Project(path, [Reference("media/a.wav", true)]) with { Evidence = EvidenceLevel.Candidate };
            var result = await new ProjectMediaPackageService(new ProjectMediaPreviewService()).CreateAsync(project, destination);
            Assert.Equal("MEDIA_PACKAGE_UNSUPPORTED", result.Error?.Code);
            Assert.False(File.Exists(destination));
        }
        finally { if (File.Exists(destination)) File.Delete(destination); }
    }

    [Fact]
    public async Task ProtectsExternalMediaEvenWhenReferenceIsBeyondPackageManifestLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-package-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "session.dawproject");
        var mediaPath = Path.Combine(root, "last.wav");
        var bytes = new byte[] { 3, 1, 4, 1, 5 };
        try
        {
            await File.WriteAllBytesAsync(projectPath, [8, 8]);
            await File.WriteAllBytesAsync(mediaPath, bytes);
            var refs = Enumerable.Repeat(Reference("embedded/unused.wav", false), ProjectMediaPackageService.MaximumReferences)
                .Append(Reference(mediaPath, true)).ToArray();
            var result = await new ProjectMediaPackageService(new ProjectMediaPreviewService())
                .CreateAsync(Project(projectPath, refs), mediaPath);
            Assert.Equal("MEDIA_PACKAGE_SOURCE_PROTECTED", result.Error?.Code);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(mediaPath));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ProjectSnapshot Project(string path, IReadOnlyList<ProjectMediaReference> media) =>
        new(path, "session", "DAWproject", "15", 120, EvidenceLevel.Confirmed, [], [], [], [], MediaReferences: media);

    private static ProjectMediaReference Reference(string path, bool? external) => new(path, external, 2, 48_000, 1.0);
}
