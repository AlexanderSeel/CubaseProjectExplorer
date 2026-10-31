using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class AssetIndexLoadingTests
{
    private sealed class Reports(Action<LibraryAssetIndexProgress> report) : IProgress<LibraryAssetIndexProgress>
    { public void Report(LibraryAssetIndexProgress value) => report(value); }

    private static ProjectSnapshot Project(int index) => new($"C:/Sessions/assets-{index:D3}.dawproject", $"assets-{index:D3}",
        "DAWproject", "15", 120, EvidenceLevel.Confirmed, [], [], [], []);

    [Fact]
    public async Task StreamingIndexReadReportsProgressAndHonorsTheSelectedProject()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-asset-load-");
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory.FullName, "library.db"));
            var projects = Enumerable.Range(0, 37).Select(Project).ToArray();
            await store.SaveScanAsync("C:/Sessions", new(projects, [], false, projects.Length, projects.Length));
            var seen = new List<string>();
            var reports = new List<LibraryAssetIndexProgress>();
            var count = await store.VisitProjectAssetIndexesAsync(document => seen.Add(document.ProjectPath), new Reports(reports.Add));

            Assert.Equal(37, count);
            Assert.Equal(37, seen.Distinct().Count());
            Assert.Equal(37, reports.Last().Processed);
            Assert.Equal(37, reports.Last().Total);
            Assert.Contains(reports, report => report.CurrentProject == projects[20].FilePath);
            seen.Clear(); reports.Clear();
            count = await store.VisitProjectAssetIndexesAsync(document => seen.Add(document.ProjectPath), new Reports(reports.Add), projects[20].FilePath);
            Assert.Equal(1, count);
            Assert.Equal(projects[20].FilePath, Assert.Single(seen));
            Assert.Equal(1, reports.Last().Total);
            Assert.Equal(1, await store.RebuildProjectAssetIndexesAsync(progress: new Reports(reports.Add), projectPath: projects[20].FilePath));
            Assert.Equal(37, (await store.GetProjectAssetIndexesAsync()).Count);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task CancelledReadKeepsAlreadyDeliveredRecordsAndDoesNotChangeSavedIndexes()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-asset-cancel-");
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory.FullName, "library.db"));
            var projects = Enumerable.Range(0, 20).Select(Project).ToArray();
            await store.SaveScanAsync("C:/Sessions", new(projects, [], false, projects.Length, projects.Length));
            using var cancellation = new CancellationTokenSource();
            var delivered = new List<string>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.VisitProjectAssetIndexesAsync(document =>
            {
                delivered.Add(document.ProjectPath);
                if (delivered.Count == 3) cancellation.Cancel();
            }, ct: cancellation.Token));
            Assert.Equal(3, delivered.Count);
            Assert.Equal(20, (await store.GetProjectAssetIndexesAsync()).Count);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task RebuildReportsMissingSourceDetailsAndRetainsItsPreviousIndex()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-asset-rebuild-feedback-");
        try
        {
            using var store = new LibraryDocumentStore(Path.Combine(directory.FullName, "library.db"));
            var project = Project(0) with { FilePath = Path.Combine(directory.FullName, "missing.dawproject") };
            await store.SaveScanAsync(directory.FullName, new([project], [], false, 1, 1));
            var document = Assert.Single(await store.GetProjectsAsync());
            document.IsCompactSnapshot = true;
            await store.SaveProjectAsync(document);
            var reports = new List<LibraryAssetIndexProgress>();
            Assert.Equal(0, await store.RebuildProjectAssetIndexesAsync(progress: new Reports(reports.Add)));
            Assert.Contains(reports, report => report.Skipped == 1 && report.CurrentProject == project.FilePath &&
                report.Detail?.Contains("FILE_NOT_FOUND") == true);
            Assert.Single(await store.GetProjectAssetIndexesAsync());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task EmptyLibraryReturnsAnEmptyRead()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-asset-empty-");
        try
        {
            var path = Path.Combine(directory.FullName, "library.db");
            using var store = new LibraryDocumentStore(path);
            Assert.Equal(0, await store.VisitProjectAssetIndexesAsync(_ => throw new InvalidOperationException()));
        }
        finally { directory.Delete(true); }
    }
}
