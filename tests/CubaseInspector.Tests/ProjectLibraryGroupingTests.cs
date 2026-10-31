using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectLibraryGroupingTests
{
    private static ProjectLibraryEntry Entry(string path, string? version = null,
        EvidenceLevel evidence = EvidenceLevel.Candidate)
    {
        var p = new ProjectSnapshot(path, Path.GetFileNameWithoutExtension(path), "CPR",
            version, null, evidence, [], [], [], []);
        return new ProjectLibraryEntry(p, null, null);
    }

    [Fact]
    public void GroupsByFolderOnlyInsideScanRootAndKeepsRootDistinct()
    {
        var root = Path.Combine(Path.GetTempPath(), "Sessions");
        var entries = new[] {
            Entry(Path.Combine(root, "A", "mix.cpr")),
            Entry(Path.Combine(root, "A", "two.bak")),
            Entry(Path.Combine(root, "B", "three.cpr")),
            Entry(Path.Combine(root, "root.cpr")),
            Entry(Path.Combine(Path.GetTempPath(), "elsewhere", "outside.cpr"))
        };
        var grouped = ProjectLibraryGrouping.Build(entries, "Folder", root);
        Assert.Equal(5, grouped.Groups.Count); // All + A + B + root + outside
        Assert.Equal(2, Assert.Single(grouped.Groups.Where(g => g.Key == "A")).Count);
        Assert.Equal(1, Assert.Single(grouped.Groups.Where(g => g.Key == "")).Count);
        Assert.Equal(1, entries.Count(e => ProjectLibraryGrouping.Includes(e, "Folder", root, "B")));
        Assert.Equal(5, entries.Count(e => ProjectLibraryGrouping.Includes(e, "Folder", root, null)));
        Assert.Contains(grouped.Groups, g => g.Key == "Outside scan root");
    }

    [Fact]
    public void DateGroupingPreservesUnknownDatesAndFolderGroupingWorksWithoutScanRoot()
    {
        var entry = Entry(Path.Combine("Sessions", "Song", "mix.cpr")) with
        { LastModifiedUtc = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero) };
        Assert.Equal("2025", ProjectLibraryGrouping.Key(entry, "Modified year", ""));
        Assert.Equal("2025-06", ProjectLibraryGrouping.Key(entry, "Modified month", ""));
        Assert.Equal("Unknown modification date", ProjectLibraryGrouping.Key(entry with { LastModifiedUtc = null }, "Modified year", ""));
        Assert.Equal(Path.Combine("Sessions", "Song"), ProjectLibraryGrouping.Key(entry, "Folder", ""));
    }

    [Fact]
    public void VersionEvidenceAndTypeGroupingPreserveUnknowns()
    {
        var entries = new[] { Entry("first.cpr", version: "Cubase 12"),
            Entry("second.dawproject", evidence: EvidenceLevel.Confirmed),
            Entry("third.cpr", version: "Cubase 12") };
        Assert.Equal(2, ProjectLibraryGrouping.Build(entries, "Version", ".").Groups.Count(g => g.Key is not null));
        Assert.Equal(2, entries.Count(e => ProjectLibraryGrouping.Includes(e, "Type", ".", "CPR")));
        Assert.Single(entries.Where(e => ProjectLibraryGrouping.Includes(e, "Evidence", ".", "Confirmed")));
        Assert.Empty(ProjectLibraryGrouping.Build(entries, "None", ".").Groups);
    }

    [Fact]
    public void LargeDirectorySetLimitsNavigatorWithoutDroppingProjects()
    {
        var entries = Enumerable.Range(0, 10000)
            .Select(i => Entry(Path.Combine("root", "folder-" + i, "mix.cpr")))
            .ToArray();
        var result = ProjectLibraryGrouping.Build(entries, "Folder", "root");
        Assert.True(result.Limited);
        Assert.Equal(251, result.Groups.Count);
        Assert.Equal(10000, result.Groups[0].Count);
        Assert.True(entries.All(e => ProjectLibraryGrouping.Includes(e, "Folder", "root", null)));
    }

    [Fact]
    public void RescanningOneFolderUpdatesItsProjectsAndRetainsOtherScanRoots()
    {
        var oldA = Entry(Path.Combine("library-a", "one.cpr"), version: "Cubase 12");
        var otherRoot = Entry(Path.Combine("library-b", "two.cpr"));
        var updatedA = oldA with { Project = oldA.Project with { CreatorVersion = "Cubase 14" } };

        var merged = ProjectLibraryBrowser.MergeScanResults([oldA, otherRoot], [updatedA]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("Cubase 14", Assert.Single(merged, entry => entry.Project.FilePath == oldA.Project.FilePath).Project.CreatorVersion);
        Assert.Contains(merged, entry => entry.Project.FilePath == otherRoot.Project.FilePath);
    }
}
