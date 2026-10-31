using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using System.Security.Cryptography;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class FolderScanCancellationTests
{
    [Fact]
    public async Task ChecksumSkipsOversizedFilesBeforeReadingTheirContents()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-checksum-limit-");
        try
        {
            var path = Path.Combine(directory.FullName, "large.dawproject");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);

            var error = await Assert.ThrowsAsync<ProjectChecksumSkippedException>(() =>
                ProjectContentChecksum.ComputeAsync(path, maximumFileBytes: 2));

            Assert.Contains("exact SHA-256 is skipped", error.Message);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task CompactScanRetainsLibraryAndAssetSummariesWithoutFullRoutingSnapshot()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-compact-scan-");
        try
        {
            var path = Path.Combine(directory.FullName, "one.cpr");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            var result = await new FolderScanner(new SummarySnapshotInspector())
                .ScanAsync(directory.FullName, retainFullSnapshots: false);

            Assert.True(result.IsSuccess, result.Error?.Message);
            var report = result.Value!;
            var project = Assert.Single(report.Projects);
            Assert.Empty(project.Tracks);
            Assert.Empty(project.Routing);
            Assert.Single(project.Plugins);
            Assert.Equal(1, Assert.Single(report.ProjectSummaries!).TrackRecordCount);
            Assert.Equal(1, Assert.Single(report.ProjectSummaries!).RoutingCount);
            Assert.Single(report.ProjectAssetIndexes!);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class SummarySnapshotInspector : IProjectInspector
    {
        public Task<Result<ProjectSnapshot>> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ProjectSnapshot>.Ok(new ProjectSnapshot(path, "One", "CPR candidate", null,
                null, EvidenceLevel.Candidate,
                [new TrackInfo("track", "Track", "Audio", null, null, null, null, null)],
                [new PluginReference("plugin", "Synth", null, "VST3", null, EvidenceLevel.Candidate)],
                [new RoutingEdge("a", "b", "Output")], ["candidate warning"])));
    }

    private sealed class FirstFastThenWaitInspector : IProjectInspector
    {
        private int _calls;

        public async Task<Result<ProjectSnapshot>> InspectAsync(
            string path, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) > 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return Result<ProjectSnapshot>.Ok(new ProjectSnapshot(
                path, Path.GetFileNameWithoutExtension(path), "CPR candidate",
                null, null, EvidenceLevel.Candidate, [], [], [], []));
        }
    }

    private sealed class InlineProgress(Action<ScanProgress> action) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => action(value);
    }

    private sealed class DeletesBeforeHashInspector : IProjectInspector
    {
        public Task<Result<ProjectSnapshot>> InspectAsync(string path, CancellationToken cancellationToken = default)
        {
            File.Delete(path);
            return Task.FromResult(Result<ProjectSnapshot>.Ok(new ProjectSnapshot(
                path, Path.GetFileNameWithoutExtension(path), "CPR candidate",
                null, null, EvidenceLevel.Candidate, [], [], [], [])));
        }
    }

    private sealed class ConcurrencyTrackingInspector : IProjectInspector
    {
        private int _active;
        private int _maximumActive;
        public int MaximumActive => Volatile.Read(ref _maximumActive);

        public async Task<Result<ProjectSnapshot>> InspectAsync(string path, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            try
            {
                await Task.Delay(50, cancellationToken);
                return Result<ProjectSnapshot>.Ok(new ProjectSnapshot(path,
                    Path.GetFileNameWithoutExtension(path), "DAWproject test", null,
                    null, EvidenceLevel.Confirmed, [], [], [], []));
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        private void UpdateMaximum(int active)
        {
            while (true)
            {
                var previous = Volatile.Read(ref _maximumActive);
                if (active <= previous || Interlocked.CompareExchange(ref _maximumActive, active, previous) == previous)
                    return;
            }
        }
    }

    [Fact]
    public async Task CancelledScanPreservesCompletedProjectsAndReportsPartialStatus()
    {
        var directory = Directory.CreateTempSubdirectory("cpr-scan-cancel-");
        try
        {
            for (var i = 0; i < 10; i++)
                await File.WriteAllBytesAsync(
                    Path.Combine(directory.FullName, $"project-{i}.cpr"), [1, 2, 3]);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            // Discovery now emits progress before any file is parsed.
            // This test targets cancellation after the first completed INSPECTION.
            var progress = new InlineProgress(p =>
            {
                if (p.Stage == ScanStage.Inspecting && p.Completed > 0)
                    cancellation.Cancel();
            });
            var result = await new FolderScanner(new FirstFastThenWaitInspector())
                .ScanAsync(directory.FullName, progress, cancellation.Token);

            Assert.True(result.IsSuccess, result.Error?.Message);
            var report = result.Value!;
            Assert.True(report.Partial);
            Assert.Equal(10, report.Discovered);
            Assert.InRange(report.Completed, 1, 10);
            Assert.NotEmpty(report.Projects);
            Assert.All(report.Projects, p => Assert.Equal(EvidenceLevel.Candidate, p.Evidence));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task ProgressProvidesSuccessfullyParsedProjectAndNoFalseFailure()
    {
        var directory = Directory.CreateTempSubdirectory("cpr-scan-progress-");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory.FullName, "one.cpr"), [1,2,3]);
            var updates = new List<ScanProgress>();
            var progress = new InlineProgress(p => updates.Add(p));
            var result = await new FolderScanner(new FirstFastThenWaitInspector())
                .ScanAsync(directory.FullName, progress);
            Assert.True(result.IsSuccess);
            Assert.Contains(updates, p => p.Stage == ScanStage.Discovering);
            Assert.Contains(updates, p => p.Stage == ScanStage.Inspecting &&
                p.Completed == 0 && p.Total == 1);
            var update = Assert.Single(updates, p => p.ParsedProject is not null);
            Assert.NotNull(update.ParsedProject);
            Assert.Null(update.Failure);
            Assert.Equal(1, update.Completed);
            Assert.Equal(1, update.Total);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task FolderScanHashesProjectBytesAndReportsHashingStage()
    {
        var directory = Directory.CreateTempSubdirectory("cpr-scan-hash-");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory.FullName, "one.cpr"), bytes);
            var updates = new List<ScanProgress>();
            var result = await new FolderScanner(new FirstFastThenWaitInspector())
                .ScanAsync(directory.FullName, new InlineProgress(update => updates.Add(update)));

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), Assert.Single(result.Value!.Projects).SourceSha256);
            Assert.Contains(updates, update => update.Stage == ScanStage.Hashing);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task LargeDawprojectInspectionIsSerializedWithinFolderScan()
    {
        var directory = Directory.CreateTempSubdirectory("dawproject-scan-concurrency-");
        try
        {
            for (var index = 0; index < 4; index++)
                await File.WriteAllBytesAsync(Path.Combine(directory.FullName, $"project-{index}.dawproject"), [1, 2, 3]);
            var inspector = new ConcurrencyTrackingInspector();

            var result = await new FolderScanner(inspector).ScanAsync(directory.FullName);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(4, result.Value!.Projects.Count);
            Assert.Equal(1, inspector.MaximumActive);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task ScanSettingsLimitDiscoveryToSelectedProjectTypes()
    {
        var directory = Directory.CreateTempSubdirectory("project-scan-types-");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory.FullName, "song.cpr"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(directory.FullName, "song.bak"),
                [(byte)'R', (byte)'I', (byte)'F', (byte)'2', 0, 0, 0, 0, 0, 0, 0, 0, (byte)'N', (byte)'U', (byte)'N', (byte)'D']);
            await File.WriteAllBytesAsync(Path.Combine(directory.FullName, "song.dawproject"), [4, 5, 6]);

            var result = await new FolderScanner(new ConcurrencyTrackingInspector()).ScanAsync(directory.FullName,
                projectTypes: new ProjectScanTypes(Cpr: false, Bak: true, Dawproject: false));

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("song.bak", Path.GetFileName(Assert.Single(result.Value!.Projects).FilePath));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task ScanRequiresAtLeastOneEnabledProjectType()
    {
        var directory = Directory.CreateTempSubdirectory("project-scan-no-types-");
        try
        {
            var result = await new FolderScanner(new ConcurrencyTrackingInspector()).ScanAsync(directory.FullName,
                projectTypes: new ProjectScanTypes(Cpr: false, Bak: false, Dawproject: false));

            Assert.False(result.IsSuccess);
            Assert.Equal("NO_PROJECT_TYPES", result.Error?.Code);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task ChecksumReadFailureRetainsParsedProjectAndReportsPathFallback()
    {
        var directory = Directory.CreateTempSubdirectory("cpr-scan-hash-failure-");
        try
        {
            var path = Path.Combine(directory.FullName, "one.cpr");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            var result = await new FolderScanner(new DeletesBeforeHashInspector()).ScanAsync(directory.FullName);

            Assert.True(result.IsSuccess, result.Error?.Message);
            var project = Assert.Single(result.Value!.Projects);
            Assert.Null(project.SourceSha256);
            Assert.Contains(project.Warnings, warning => warning.Contains("duplicate detection falls back to file path", StringComparison.Ordinal));
            Assert.Contains(result.Value.Failures, failure => failure.Error.StartsWith("CHECKSUM_FAILED:", StringComparison.Ordinal));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task EmptyFolderReportsCompleteNotPartial()
    {
        var directory = Directory.CreateTempSubdirectory("cpr-scan-empty-");
        try
        {
            var result = await new FolderScanner(new FirstFastThenWaitInspector())
                .ScanAsync(directory.FullName);
            Assert.True(result.IsSuccess);
            Assert.False(result.Value!.Partial);
            Assert.Equal(0, result.Value.Discovered);
            Assert.Equal(0, result.Value.Completed);
        }
        finally { directory.Delete(true); }
    }
}
