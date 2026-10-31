using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class NavigationHistoryTests
{
    [Fact]
    public void NavigateBackForwardAndBreadcrumbMaintainExpectedTrail()
    {
        var history = new NavigationHistory("Projects");
        history.Navigate("Overview");
        history.Navigate("Plugins");

        Assert.Equal(new[] { "Projects", "Overview", "Plugins" }, history.Trail);
        Assert.True(history.Back());
        Assert.Equal("Overview", history.Current);
        Assert.True(history.Forward());
        Assert.Equal("Plugins", history.Current);
        Assert.True(history.JumpTo(0));
        Assert.Equal("Projects", history.Current);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void NewNavigationAfterBackDropsForwardPagesAndDuplicateIsIgnored()
    {
        var history = new NavigationHistory("Projects");
        history.Navigate("Overview");
        history.Navigate("Plugins");
        history.Back();

        Assert.False(history.Navigate("Overview"));
        Assert.True(history.Navigate("Catalog"));
        Assert.Equal(new[] { "Projects", "Overview", "Catalog" }, history.Trail);
        Assert.False(history.CanGoForward);
    }
}
