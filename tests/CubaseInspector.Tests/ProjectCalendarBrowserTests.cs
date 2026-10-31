using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectCalendarBrowserTests
{
    private static ProjectLibraryEntry Entry(string name, DateTimeOffset? modified) => new(
        new ProjectSnapshot(name, name, "DAWproject", null, null, EvidenceLevel.Confirmed,
            [], [], [], []), null, modified);

    [Fact]
    public void GroupsProjectsByLocalFileModifiedDateAndOmitsUnknownDates()
    {
        var firstDate = new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            Entry("B", firstDate),
            Entry("A", firstDate.AddHours(4)),
            Entry("Unknown", null)
        };
        var localDate = DateOnly.FromDateTime(firstDate.ToLocalTime().DateTime);

        var projects = ProjectCalendarBrowser.OnLocalFileModifiedDate(entries, localDate);
        var counts = ProjectCalendarBrowser.CountsByLocalFileModifiedDate(entries);

        Assert.Equal(new[] { "A", "B" }, projects.Select(entry => entry.Project.Name));
        Assert.Equal(2, counts[localDate]);
        Assert.DoesNotContain(entries.Single(entry => entry.Project.Name == "Unknown"),
            projects);
    }

    [Fact]
    public void KeepsProjectsWithoutReadableModificationDatesInStableUnknownDateList()
    {
        var entries = new[]
        {
            Entry("Zebra", null),
            Entry("Dated", DateTimeOffset.UtcNow),
            Entry("Alpha", null)
        };

        var unknown = ProjectCalendarBrowser.WithoutKnownFileModifiedDate(entries);

        Assert.Equal(new[] { "Alpha", "Zebra" }, unknown.Select(entry => entry.Project.Name));
        Assert.Empty(ProjectCalendarBrowser.CountsByLocalFileModifiedDate(unknown));
    }
}
