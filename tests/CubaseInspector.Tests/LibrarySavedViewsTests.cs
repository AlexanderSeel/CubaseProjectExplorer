using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LibrarySavedViewsTests
{
    [Fact]
    public void CompleteFiltersRoundtripWithoutScannedFiles()
    {
        var filters = new LibraryFilterState(Search: "strings", FileType: "CPR",
            Evidence: "Candidate", Sort: "Most recent", ReverseSort: true,
            Version: "15", Folder: "mixes", TrackType: "Audio", MinSizeMiB: "2",
            MaxSizeMiB: "20", MinBpm: "120", MaxBpm: "140",
            IncludeUnknownBpm: false, WarningsOnly: true,
            ModifiedFrom: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)),
            ModifiedThrough: new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(2)),
            GroupMode: "Folder", GroupKey: "Sessions");
        var saved = LibrarySavedViews.Save([], "  My mixes ", filters);
        var json = LibrarySavedViews.ToJson(saved);
        Assert.Equal(new LibrarySavedView("My mixes", filters), Assert.Single(LibrarySavedViews.ParseOrEmpty(json)));
        Assert.DoesNotContain("ProjectSnapshot", json);
        Assert.DoesNotContain("ScanIssues", json);
    }

    [Fact]
    public void SameNameReplacesCaseInsensitivelyWithoutAddingDuplicate()
    {
        var first = LibrarySavedViews.Save([], "Mix", new LibraryFilterState(FileType: "CPR"));
        var second = LibrarySavedViews.Save(first, "mix", new LibraryFilterState(FileType: "BAK"));
        Assert.Equal("BAK", Assert.Single(second).Filters.FileType);
    }

    [Fact]
    public void MultiSelectFacetsNormalizeAndPersistInStableOrder()
    {
        var normalized = new LibraryFilterState(FileType: "BAK, CPR, bak",
            Evidence: "Candidate, Confirmed").Normalized();
        Assert.Equal("CPR, BAK", normalized.FileType);
        Assert.Equal("Confirmed, Candidate", normalized.Evidence);

        var roundTrip = LibrarySavedViews.ParseOrEmpty(LibrarySavedViews.ToJson([
            new LibrarySavedView("Mixed evidence", normalized)]));
        Assert.Equal(normalized, Assert.Single(roundTrip).Filters);
    }

    [Fact]
    public void CorruptedAndOversizedJsonDoesNotLoad()
    {
        Assert.Empty(LibrarySavedViews.ParseOrEmpty("{ bad json"));
        Assert.Empty(LibrarySavedViews.ParseOrEmpty(new string('x', 128001)));
    }

    [Fact]
    public void RejectsUntrustedUnknownModesAndBoundsTheCatalog()
    {
        var filters = new LibraryFilterState(FileType: "EXE", Evidence: "Imaginary", Sort: "Nonsense",
            GroupMode: "Other");
        var saved = LibrarySavedViews.Save([], "safe", filters);
        var normalized = Assert.Single(saved).Filters;
        Assert.Equal("All", normalized.FileType);
        Assert.Equal("All", normalized.Evidence);
        Assert.Equal("Name", normalized.Sort);
        Assert.Equal("None", normalized.GroupMode);
        Assert.Null(normalized.GroupKey);
        var views = Enumerable.Range(0, LibrarySavedViews.MaximumViews)
            .Select(i => new LibrarySavedView("View " + i, new LibraryFilterState())).ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            LibrarySavedViews.Save(views, "Another", new LibraryFilterState()));
        Assert.Throws<ArgumentException>(() =>
            LibrarySavedViews.Save(views, " ", new LibraryFilterState()));
    }
    [Fact]
    public void SavingViewsReplacesFileWithoutLeavingTemporaryFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cpe-preset-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "views.json");
            LibrarySavedViews.SaveFile(path, LibrarySavedViews.Save([], "First", new LibraryFilterState()));
            LibrarySavedViews.SaveFile(path, LibrarySavedViews.Save([], "Second", new LibraryFilterState()));
            var views = LibrarySavedViews.ParseOrEmpty(File.ReadAllText(path));
            Assert.Equal("Second", Assert.Single(views).Name);
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("{ bad json")]
    [InlineData("[{\"Name\":\"Current\",\"Filters\":{\"NewFilter\":\"value\"}}]")]
    [InlineData("[] trailing")]
    public void UnknownOrCorruptSavedViewFilesAreNotWritable(string contents)
    {
        var directory = Directory.CreateTempSubdirectory("library-views-protection-");
        try
        {
            var path = Path.Combine(directory.FullName, "views.json");
            File.WriteAllText(path, contents);
            var loaded = LibrarySavedViews.ReadFile(path);
            Assert.False(loaded.CanWrite);
            Assert.NotNull(loaded.Warning);
            Assert.Throws<InvalidOperationException>(() =>
                LibrarySavedViews.SaveFile(path, [], loaded.CanWrite));
            Assert.Equal(contents, File.ReadAllText(path));
        }
        finally { directory.Delete(true); }
    }

}
