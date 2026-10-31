using CubaseInspector.Core;
using System.Security.Cryptography;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectLibraryBrowserTests
{
    private static ProjectLibraryEntry Item(string path, EvidenceLevel evidence,
        double? bpm = null, string? version = null, int tracks = 0, long size = 100,
        int day = 1)
    {
        var snapshots = Enumerable.Range(0, tracks)
            .Select(i => new TrackInfo("id-" + i, "Track " + i, "Audio", null, null, null, null, null,
                evidence)).ToArray();
        var project = new ProjectSnapshot(path, Path.GetFileNameWithoutExtension(path),
            "CPR project", version, bpm, evidence, snapshots, [], [], []);
        return new ProjectLibraryEntry(project, size,
            new DateTimeOffset(2026, 10, day, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Unknown_Bpm_Is_Visible_By_Default_With_Range_Filter()
    {
        var list = new[] { Item("old.bak", EvidenceLevel.Candidate),
            Item("mix.dawproject", EvidenceLevel.Confirmed, 126),
            Item("slow.cpr", EvidenceLevel.Candidate, 85) };
        var query = new ProjectLibraryQuery(MinBpm: 120, MaxBpm: 140);
        Assert.Equal(2, ProjectLibraryBrowser.Filter(list, query).Count);
        Assert.Contains(ProjectLibraryBrowser.Filter(list, query), e => e.Project.FilePath == "old.bak");
        var withoutUnknown = ProjectLibraryBrowser.Filter(list, query with { IncludeUnknownBpm = false });
        Assert.Single(withoutUnknown);
        Assert.Equal("mix.dawproject", withoutUnknown[0].Project.FilePath);
    }

    [Fact]
    public void Combines_FileType_Provenance_And_Path_Search()
    {
        var list = new[] { Item(@"D:\Sessions\old.bak", EvidenceLevel.Candidate),
            Item(@"D:\Sessions\new.cpr", EvidenceLevel.Candidate),
            Item(@"D:\Export\new.dawproject", EvidenceLevel.Confirmed) };
        var found = ProjectLibraryBrowser.Filter(list,
            new ProjectLibraryQuery(Search: "Sessions", FileType: "BAK", Evidence: "Candidate"));
        Assert.Single(found);
        Assert.Equal(@"D:\Sessions\old.bak", found[0].Project.FilePath);
        Assert.Empty(ProjectLibraryBrowser.Filter(list,
            new ProjectLibraryQuery(FileType: "BAK", Evidence: "Confirmed")));
    }

    [Fact]
    public void MultiSelectFileTypeAndEvidenceFacetsAreAppliedIndependently()
    {
        var list = new[]
        {
            Item("one.cpr", EvidenceLevel.Candidate),
            Item("two.bak", EvidenceLevel.Candidate),
            Item("three.dawproject", EvidenceLevel.Confirmed),
            Item("four.cpr", EvidenceLevel.Confirmed)
        };

        var result = ProjectLibraryBrowser.Filter(list, new ProjectLibraryQuery(
            FileType: "CPR, BAK", Evidence: "Candidate, Confirmed"));

        Assert.Equal(3, result.Count);
        Assert.DoesNotContain(result, item => item.Project.FilePath == "three.dawproject");
    }

    [Fact]
    public void Sorts_By_TrackCount_Size_And_Recent_Modification()
    {
        var list = new[] { Item("alpha.cpr", EvidenceLevel.Candidate, tracks: 2, size: 150, day: 1),
            Item("beta.cpr", EvidenceLevel.Candidate, tracks: 9, size: 50, day: 3),
            Item("gamma.cpr", EvidenceLevel.Candidate, tracks: 3, size: 800, day: 2) };
        Assert.Equal("beta.cpr",
            ProjectLibraryBrowser.Filter(list, new ProjectLibraryQuery(SortBy: "Track records"))[0].Project.FilePath);
        Assert.Equal("gamma.cpr",
            ProjectLibraryBrowser.Filter(list, new ProjectLibraryQuery(SortBy: "Largest"))[0].Project.FilePath);
        Assert.Equal("beta.cpr",
            ProjectLibraryBrowser.Filter(list, new ProjectLibraryQuery(SortBy: "Most recent"))[0].Project.FilePath);
    }

    [Fact]
    public void CompoundVersionFolderTrackSizeDateAndWarningsFilters()
    {
        var a = Item(@"D:\Music\Session12\mix.cpr", EvidenceLevel.Candidate,
            version: "Cubase 12.0.70", tracks: 2, size: 2048, day: 3);
        var b = Item(@"D:\Music\Session15\mix.cpr", EvidenceLevel.Candidate,
            version: "Cubase 15", tracks: 1, size: 4800, day: 10);
        a = a with { Project = a.Project with
        {
            Tracks = [new TrackInfo("i", "Lead", "Instrument", null, null, null, null, null,
                EvidenceLevel.Candidate)],
            Warnings = ["Unverified native track"]
        }};
        var list = new[] { a, b };
        var query = new ProjectLibraryQuery(
            VersionContains: "12", FolderContains: "Session12", TrackType: "Instrument",
            MinSizeBytes: 1024, MaxSizeBytes: 4096,
            ModifiedSinceUtc: new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
            ModifiedBeforeUtc: new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
            WarningsOnly: true);
        Assert.Single(ProjectLibraryBrowser.Filter(list, query));
        Assert.Empty(ProjectLibraryBrowser.Filter(list, query with { TrackType = "MIDI" }));
        Assert.Empty(ProjectLibraryBrowser.Filter(list, query with { MaxSizeBytes = 1000 }));
        Assert.Empty(ProjectLibraryBrowser.Filter(list, query with { VersionContains = "15" }));
        Assert.Empty(ProjectLibraryBrowser.Filter(list, query with { ModifiedBeforeUtc =
            new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void ReverseSortProducesOppositeStableOrdering()
    {
        var list = new[] { Item("alpha.cpr", EvidenceLevel.Candidate, size: 100),
            Item("bravo.cpr", EvidenceLevel.Candidate, size: 200),
            Item("charlie.cpr", EvidenceLevel.Candidate, size: 300) };
        var ascending = ProjectLibraryBrowser.Filter(list, new ProjectLibraryQuery(SortBy: "Name"))
            .Select(x => x.Project.Name).ToArray();
        var descending = ProjectLibraryBrowser.Filter(list,
            new ProjectLibraryQuery(SortBy: "Name", ReverseSort: true))
            .Select(x => x.Project.Name).ToArray();
        Assert.Equal(ascending.Reverse().ToArray(), descending);
        Assert.Equal("bravo.cpr", ProjectLibraryBrowser.Filter(list,
            new ProjectLibraryQuery(SortBy: "Largest", ReverseSort: true))[1].Project.FilePath);
    }

    [Fact]
    public void TenThousandProjectsRemainFilterableWithoutInventingUnknownData()
    {
        var all = Enumerable.Range(0, 10_000)
            .Select(i => Item($"folder-{i % 100}/project-{i:00000}.cpr",
                EvidenceLevel.Candidate, size: i * 100L))
            .ToArray();
        var matches = ProjectLibraryBrowser.Filter(all, new ProjectLibraryQuery(
            Search: "folder-42", FileType: "CPR", Evidence: "Candidate",
            MinSizeBytes: 0, SortBy: "Largest"));
        Assert.Equal(100, matches.Count);
        Assert.All(matches, x => Assert.Equal(EvidenceLevel.Candidate, x.Project.Evidence));
        Assert.True(matches[0].SizeBytes >= matches[^1].SizeBytes);
        Assert.Equal(all.Length, ProjectLibraryBrowser.Filter(all, new ProjectLibraryQuery()).Count);
    }

    [Fact]
    public void Empty_Filter_And_Unknown_Values_Are_Deterministic()
    {
        var list = new[] { Item("one.cpr", EvidenceLevel.Candidate) };
        Assert.Single(ProjectLibraryBrowser.Filter(list, new ProjectLibraryQuery()));
        Assert.Empty(ProjectLibraryBrowser.Filter(list, new ProjectLibraryQuery(Search: "does-not-exist")));
        Assert.Empty(ProjectLibraryBrowser.Filter([], new ProjectLibraryQuery()));
    }

    [Fact]
    public void FromFile_UsesCurrentFileAttributesThenFallsBackToStoredMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        var modified = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var storedModified = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            File.SetLastWriteTimeUtc(path, modified);
            var project = Item(path, EvidenceLevel.Candidate).Project;
            var fromAttributes = ProjectLibraryBrowser.FromFile(project, 99, storedModified);

            Assert.Equal(4, fromAttributes.SizeBytes);
            Assert.Equal(modified, fromAttributes.LastModifiedUtc?.UtcDateTime);

            File.Delete(path);
            var fromStoredMetadata = ProjectLibraryBrowser.FromFile(project, 99, storedModified);
            Assert.Equal(99, fromStoredMetadata.SizeBytes);
            Assert.Equal(storedModified, fromStoredMetadata.LastModifiedUtc);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MergeScanResults_DeduplicatesSameChecksumAndMovesWhenCanonicalPathIsGone()
    {
        var directory = Directory.CreateTempSubdirectory("cpe-project-hash-");
        var originalPath = Path.Combine(directory.FullName, "original.cpr");
        var movedPath = Path.Combine(directory.FullName, "moved", "copy.cpr");
        Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
        var bytes = "identical project bytes"u8.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(bytes));
        try
        {
            File.WriteAllBytes(originalPath, bytes);
            File.WriteAllBytes(movedPath, bytes);
            var original = Item(originalPath, EvidenceLevel.Candidate) with { SourceSha256 = checksum };
            var moved = Item(movedPath, EvidenceLevel.Candidate) with { SourceSha256 = checksum.ToLowerInvariant() };

            var whileOriginalExists = ProjectLibraryBrowser.MergeScanResults([original], [moved]);
            Assert.Single(whileOriginalExists);
            Assert.Equal(originalPath, whileOriginalExists[0].Project.FilePath);

            File.Delete(originalPath);
            var afterMove = ProjectLibraryBrowser.MergeScanResults([original], [moved]);
            Assert.Single(afterMove);
            Assert.Equal(movedPath, afterMove[0].Project.FilePath);
        }
        finally { directory.Delete(recursive: true); }
    }
}
