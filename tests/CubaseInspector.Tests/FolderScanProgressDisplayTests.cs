using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public class FolderScanProgressDisplayTests
{
    [Fact]
    public void DiscoveryIsIndeterminateEvenAsFoldersAndCandidatesAccumulate()
    {
        var view = FolderScanProgressDisplay.Create(
            new ScanProgress(32, 14, @"C:\Music", Stage: ScanStage.Discovering),
            0, 2, TimeSpan.FromSeconds(1.25));
        Assert.True(view.IsIndeterminate);
        Assert.Equal(0, view.Percent);
        Assert.Contains("32 folders explored", view.Summary);
        Assert.Contains("14 candidate project files", view.Summary);
        Assert.Equal(@"C:\Music", view.CurrentPath);
    }

    [Fact]
    public void InspectingTracksRealPercentageAndErrors()
    {
        var view = FolderScanProgressDisplay.Create(
            new ScanProgress(3, 12, "Song.cpr"), 2, 1,
            TimeSpan.FromSeconds(4));
        Assert.False(view.IsIndeterminate);
        Assert.Equal(25, view.Percent);
        Assert.Contains("3 / 12 inspected (25%)", view.Summary);
        Assert.Contains("2 readable", view.Summary);
        Assert.Contains("1 issues", view.Summary);
    }

    [Fact]
    public void HashingReportsItsOwnStageAndKeepsDeterminateProgress()
    {
        var view = FolderScanProgressDisplay.Create(
            new ScanProgress(2, 8, "Song.cpr", Stage: ScanStage.Hashing),
            1, 0, TimeSpan.FromSeconds(2));
        Assert.Equal("Hashing project files", view.Stage);
        Assert.Equal(25, view.Percent);
        Assert.Contains("2 / 8 hashed", view.Summary);
    }

    [Fact]
    public void EmptyFolderAndOutOfBoundsEventsRemainValid()
    {
        var empty = FolderScanProgressDisplay.Create(
            new ScanProgress(0, 0, ""), 0, 0, TimeSpan.Zero);
        Assert.False(empty.IsIndeterminate);
        Assert.Equal(100, empty.Percent);
        var clamped = FolderScanProgressDisplay.Create(
            new ScanProgress(20, 10, ""), 0, 0, TimeSpan.Zero);
        Assert.Equal(100, clamped.Percent);
    }
}
