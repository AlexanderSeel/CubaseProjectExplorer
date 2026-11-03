using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LibraryUiPreferencesTests
{
    [Fact]
    public void LayoutPreferencesRoundTripWithoutProjectMetadata()
    {
        var expected = new LibraryUiPreferences(2, 420, 2);
        var json = expected.ToJson();
        Assert.Equal(expected, LibraryUiPreferences.ParseOrDefault(json));
        Assert.DoesNotContain("ProjectPath", json);
        Assert.DoesNotContain("Recent", json);
    }

    [Fact]
    public void DensityLoadsLegacySettingsAndClampsInvalidValues()
    {
        Assert.Equal(1, LibraryUiPreferences.ParseOrDefault("{\"ViewMode\":2}").Density);
        Assert.Equal(0, LibraryUiPreferences.ParseOrDefault("{\"Density\":-4}").Density);
        Assert.Equal(2, LibraryUiPreferences.ParseOrDefault("{\"Density\":99}").Density);
    }

    [Theory]
    [InlineData("{ invalid", 1, 312)]
    [InlineData("{\"ViewMode\":99,\"DetailsWidth\":999}", 2, 530)]
    [InlineData("{\"ViewMode\":-3,\"DetailsWidth\":25}", 0, 230)]
    [InlineData("", 1, 312)]
    public void CorruptAndOutOfRangeUserLayoutSettingsAreSafe(
        string content, int expectedMode, double expectedWidth)
    {
        var pref = LibraryUiPreferences.ParseOrDefault(content);
        Assert.Equal(expectedMode, pref.ViewMode);
        Assert.Equal(expectedWidth, pref.DetailsWidth);
    }

    [Fact]
    public void LegacyExplicitViewChoiceIsPreserved()
    {
        var directory = Directory.CreateTempSubdirectory("library-ui-preferences-");
        try
        {
            var path = Path.Combine(directory.FullName, "layout.json");
            File.WriteAllText(path, "{\"ViewMode\":0}");
            var loaded = LibraryUiPreferences.ReadFile(path);
            Assert.True(loaded.CanWrite);
            Assert.Equal(0, loaded.Settings.ViewMode);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData("{ invalid")]
    [InlineData("{\"FutureField\":1}")]
    [InlineData("{\"Density\":1,\"Density\":2}")]
    public void UnreadableOrUnknownSettingsAreProtectedFromOverwrite(string contents)
    {
        var directory = Directory.CreateTempSubdirectory("library-ui-preferences-");
        try
        {
            var path = Path.Combine(directory.FullName, "layout.json");
            File.WriteAllText(path, contents);
            var loaded = LibraryUiPreferences.ReadFile(path);
            Assert.False(loaded.CanWrite);
            Assert.NotNull(loaded.Warning);
            Assert.Throws<InvalidOperationException>(() =>
                LibraryUiPreferences.SaveFile(path, new LibraryUiPreferences(), loaded.CanWrite));
            Assert.Equal(contents, File.ReadAllText(path));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void AtomicLayoutSaveRoundtripsAndLeavesNoTemporaryFile()
    {
        var directory = Directory.CreateTempSubdirectory("library-ui-preferences-");
        try
        {
            var path = Path.Combine(directory.FullName, "layout.json");
            var settings = new LibraryUiPreferences(2, 430, 0, [1, 2, 3], "Project focus");
            LibraryUiPreferences.SaveFile(path, settings);
            var loaded = LibraryUiPreferences.ReadFile(path);
            Assert.True(loaded.CanWrite);
            Assert.Equal(settings.Normalized() with { ColumnWeights = null },
                loaded.Settings with { ColumnWeights = null });
            Assert.Equal(LibraryColumnLayout.Normalize(settings.ColumnWeights!),
                loaded.Settings.ColumnWeights);
            Assert.Single(Directory.GetFiles(directory.FullName));
        }
        finally { directory.Delete(true); }
    }
}
