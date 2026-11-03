using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectLibrarySummaryTests
{
    [Fact]
    public void DescribesVisibleReadableCandidateWarningAndLatestScanFailureCountsSeparately()
    {
        var entries = new[]
        {
            Entry("confirmed.dawproject", EvidenceLevel.Confirmed),
            Entry("candidate.cpr", EvidenceLevel.Candidate),
            Entry("warning.dawproject", EvidenceLevel.Confirmed, "Unsupported optional field")
        };

        var summary = ProjectLibrarySummary.Describe(entries, visibleCount: 2, scanIssueCount: 4);

        Assert.Equal("2 shown of 3 readable · 1 candidate evidence · 1 with parser warnings · last scan: 4 failed inputs", summary);
    }

    [Fact]
    public void EmptyLibraryKeepsUnknownEvidenceCountsAtZero()
    {
        Assert.Equal("0 shown of 0 readable · 0 candidate evidence · 0 with parser warnings · last scan: 0 failed inputs",
            ProjectLibrarySummary.Describe([], visibleCount: 0, scanIssueCount: 0));
    }

    private static ProjectLibraryEntry Entry(string path, EvidenceLevel evidence, params string[] warnings) =>
        new(new ProjectSnapshot(path, Path.GetFileNameWithoutExtension(path), "DAWproject", null, null,
            evidence, [], [], [], warnings), null, null);
}
