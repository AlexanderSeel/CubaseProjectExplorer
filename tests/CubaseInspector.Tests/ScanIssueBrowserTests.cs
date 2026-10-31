using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ScanIssueBrowserTests
{
    [Theory]
    [InlineData("READ_ERROR: Unsupported header", "READ_ERROR")]
    [InlineData("ARCHIVE_RATIO_LIMIT: Compression ratio too high", "ARCHIVE_RATIO_LIMIT")]
    [InlineData("Folder permissions denied", "Unclassified")]
    [InlineData("Invalid: something", "Unclassified")]
    [InlineData("", "Unclassified")]
    public void ErrorFacetIsBasedOnExplicitErrorCodesOnly(string message, string expected)
    {
        Assert.Equal(expected, ScanIssueBrowser.Category(message));
    }

    [Fact]
    public void CategoriesAreDistinctAndNeverInventErrorCode()
    {
        var categories = ScanIssueBrowser.Categories([
            "IO_ERROR: folder", "IO_ERROR: file", "PARSE_FAILED: malformed", "permission denied"]);
        Assert.Equal(new[] { "All", "IO_ERROR", "PARSE_FAILED", "Unclassified" }, categories);
    }

    [Fact]
    public void SearchMatchesPathAndMessageIgnoringCase()
    {
        Assert.True(ScanIssueBrowser.Matches("D:/mixes/broken.cpr", "READ_ERROR: truncated",
            "READ_ERROR", "BROKEN"));
        Assert.True(ScanIssueBrowser.Matches("D:/mixes/broken.cpr", "READ_ERROR: truncated",
            "All", "TRUNCATED"));
        Assert.False(ScanIssueBrowser.Matches("D:/mixes/broken.cpr", "READ_ERROR: truncated",
            "IO_ERROR", ""));
        Assert.False(ScanIssueBrowser.Matches("D:/mixes/broken.cpr", "READ_ERROR: truncated",
            "All", "another file"));
    }
}
