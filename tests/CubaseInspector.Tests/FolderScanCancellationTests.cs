using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using System.Security.Cryptography;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class FolderScanCancellationTests
{
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
