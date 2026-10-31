using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectLibrarySortPresentationTests
{
    [Theory]
    [InlineData("Name", false, "Project ↑")]
    [InlineData("Name", true, "Project ↓")]
    [InlineData("Largest", false, "Size ↓")]
    [InlineData("Largest", true, "Size ↑")]
    [InlineData("Most recent", false, "File modified ↓")]
    [InlineData("Most recent", true, "File modified ↑")]
    public void ActiveColumnsShowTheirActualDirection(string key, bool reverse, string expected)
    {
        string title = key == "Name" ? "Project" : key == "Largest" ? "Size" : "File modified";
        Assert.Equal(expected, ProjectLibrarySortPresentation.Header(key, key, reverse, title));
        Assert.Equal("Other", ProjectLibrarySortPresentation.Header("Version", key, reverse, "Other"));
    }
}
