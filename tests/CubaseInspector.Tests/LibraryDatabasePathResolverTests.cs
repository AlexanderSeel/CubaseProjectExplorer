using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LibraryDatabasePathResolverTests
{
    [Fact]
    public void UsesLocalLibraryWhenConfiguredDriveIsUnavailable()
    {
        var missingDrive = Enumerable.Range('A', 26)
            .Select(letter => $"{(char)letter}:\\")
            .FirstOrDefault(path => !Directory.Exists(path));
        Assert.NotNull(missingDrive);
        var configured = Path.Combine(missingDrive!, "Cubase", "library.db");
        var localData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = LibraryDatabasePathResolver.Resolve(configured, localData);

        Assert.Equal(Path.GetFullPath(Path.Combine(localData, "CubaseProjectExplorer", "library.db")), result.DatabasePath);
        Assert.Contains(configured, result.Warning);
        Assert.Contains("saved location is retained", result.Warning);
    }

    [Fact]
    public void KeepsConfiguredPathWhenItsDriveIsAvailable()
    {
        var configured = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "library.db");
        var result = LibraryDatabasePathResolver.Resolve(configured, Path.GetTempPath());

        Assert.Equal(Path.GetFullPath(configured), result.DatabasePath);
        Assert.Null(result.Warning);
    }
}
