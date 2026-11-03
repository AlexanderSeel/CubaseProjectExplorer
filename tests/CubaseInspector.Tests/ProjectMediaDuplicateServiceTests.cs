using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectMediaDuplicateServiceTests
{
    [Fact]
    public async Task FindsByteIdenticalExternalFilesAndMergesRepeatedUsesOfSamePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-duplicate-media-" + Guid.NewGuid().ToString("N"));
        var firstProjectDirectory = Path.Combine(root, "one");
        var secondProjectDirectory = Path.Combine(root, "two");
        Directory.CreateDirectory(Path.Combine(firstProjectDirectory, "audio"));
        Directory.CreateDirectory(Path.Combine(secondProjectDirectory, "recordings"));
        var firstPath = Path.Combine(firstProjectDirectory, "audio", "kick.wav");
        var secondPath = Path.Combine(secondProjectDirectory, "recordings", "snare.wav");
        var uniquePath = Path.Combine(secondProjectDirectory, "recordings", "unique.wav");
        var sameBytes = new byte[] { 1, 2, 3, 4, 5, 6 };
        var uniqueBytes = new byte[] { 6, 5, 4, 3, 2, 1 };
        try
        {
            await File.WriteAllBytesAsync(firstPath, sameBytes);
            await File.WriteAllBytesAsync(secondPath, sameBytes);
            await File.WriteAllBytesAsync(uniquePath, uniqueBytes);
            var first = Project(Path.Combine(firstProjectDirectory, "one.dawproject"), "One", [
                new ProjectMediaReference("audio/kick.wav", true, 1, 48000, 1, "track-a"),
                new ProjectMediaReference("audio/kick.wav", true, 1, 48000, 1, "track-b")],
                [Track("track-a", "Kick"), Track("track-b", "Bass")]);
            var second = Project(Path.Combine(secondProjectDirectory, "two.dawproject"), "Two", [
                new ProjectMediaReference("recordings/snare.wav", true, 1, 48000, 1, "track-c"),
                new ProjectMediaReference("recordings/unique.wav", true, 1, 48000, 1, "track-d")],
                [Track("track-c", "Snare"), Track("track-d", "Hat")]);
            var selections = new[]
            {
                new ProjectMediaReferenceSelection(first, 0),
                new ProjectMediaReferenceSelection(first, 1),
                new ProjectMediaReferenceSelection(second, 0),
                new ProjectMediaReferenceSelection(second, 1)
            };

            var report = await new ProjectMediaDuplicateService(new ProjectMediaPreviewService()).ScanAsync(selections);

            Assert.Equal(3, report.AnalyzedFileCount);
            Assert.Equal(0, report.SkippedFileCount);
            Assert.Equal(sameBytes.Length * 2L + uniqueBytes.Length, report.BytesRead);
            var duplicate = Assert.Single(report.Groups);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sameBytes)), duplicate.Sha256);
            Assert.Equal(2, duplicate.Sources.Count);
            Assert.Equal(2, duplicate.Sources.Single(source => source.ResolvedPath == Path.GetFullPath(firstPath)).Uses.Count);
            Assert.Equal(new[] { "Bass", "Kick" }, duplicate.Sources.Single(source => source.ResolvedPath == Path.GetFullPath(firstPath))
                .Uses.Select(use => use.TrackName).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            Assert.Equal(sameBytes, await File.ReadAllBytesAsync(firstPath));
            Assert.Equal(sameBytes, await File.ReadAllBytesAsync(secondPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ProjectSnapshot Project(string path, string name,
        IReadOnlyList<ProjectMediaReference> references, IReadOnlyList<TrackInfo> tracks) =>
        new(path, name, "DAWproject", "15", 120, EvidenceLevel.Confirmed, tracks, [], [], [], MediaReferences: references);

    private static TrackInfo Track(string id, string name) => new(id, name, "Audio", id, null, null, null, null);
}
