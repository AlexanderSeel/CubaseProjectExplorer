using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprCandidateComparisonTests
{
    private const string A = "56535473796C3173796C656E74683100";
    private const string B = "297BA567D83144E1AE921DEF07B41156";
    private const string C = "77BBA7CA90F14C9BB298BA9010D6DD78";

    private static ProjectSnapshot Cpr(string path, params PluginReference[] plugins) =>
        new(path, Path.GetFileNameWithoutExtension(path), "CPR structure",
            "Cubase 14", null, EvidenceLevel.Candidate, [], plugins, [], []);
    private static PluginReference Token(string id, string name) =>
        new(id, name, null, "CPR UID (format unverified)", null, EvidenceLevel.Candidate);

    [Fact]
    public void ShowsNewAndUnobservedUidsWithoutClaimingPluginRemoval()
    {
        var result = CprCandidateComparisonService.Compare(
            Cpr("mix-A.cpr", Token(A, "Sylenth1"), Token(B, "EQ")),
            Cpr("mix-B.cpr", Token(A, "Sylenth1"), Token(C, "StereoEnhancer")));
        Assert.Equal(1, result.NewUids);
        Assert.Equal(1, result.UnobservedUids);
        Assert.Equal(2, result.Changes.Count);
        Assert.Contains(result.Changes, x => x.Entity.Contains(B) &&
            x.Property == "Removed" && x.After!.Contains("Not observed"));
        Assert.All(result.Changes, c => Assert.Contains("candidate", c.MatchQuality, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SeparatesRecordedNameAndTokenFrequencyChanges()
    {
        var result = CprCandidateComparisonService.Compare(
            Cpr("old.cpr", Token(A, "My Synth"), Token(A, "My Synth")),
            Cpr("new.bak", Token(A, "Sylenth1")));
        Assert.Equal(1, result.NameChanges);
        Assert.Equal(1, result.TokenFrequencyChanges);
        Assert.Equal(0, result.NewUids);
        Assert.Equal(2, result.Changes.Count);
        Assert.Contains(result.Changes, x => x.Property == "Recorded names");
        Assert.Contains(result.Changes, x => x.Property == "UID token occurrences" &&
            x.Before == "2" && x.After == "1");
    }

    [Fact]
    public void UidOnlyVsNamedIsReportedAsMetadataAvailabilityNotNewPlugin()
    {
        var result = CprCandidateComparisonService.Compare(
            Cpr("first.cpr", Token(A, "Name not decoded")),
            Cpr("second.cpr", Token(A, "Sylenth1")));
        Assert.Equal(0, result.NewUids);
        Assert.Equal(0, result.UnobservedUids);
        Assert.Equal(1, result.NameChanges);
        Assert.Contains(result.Changes, x => x.Property == "Recorded names" &&
            x.Before!.Contains("No typed"));
    }

    [Fact]
    public void UnresolvedOffsetMarkersRemainCoverageOnly()
    {
        var result = CprCandidateComparisonService.Compare(
            Cpr("first.cpr", Token("offset-AAA", "Unresolved plugin UID")),
            Cpr("second.cpr", Token("offset-BBB", "Unresolved plugin UID"),
                Token("offset-CCC", "Unresolved plugin UID")));
        Assert.Equal(0, result.BeforeUids);
        Assert.Equal(0, result.AfterUids);
        var change = Assert.Single(result.Changes);
        Assert.Equal(CprCandidateComparisonService.CoverageCategory, change.Category);
        Assert.Equal("Offset-only marker count", change.Property);
    }

    [Fact]
    public void MixedOrConfirmedInputsAreNotComparedAsCpr()
    {
        var native = Cpr("a.cpr", Token(A, "Sylenth1"));
        var confirmed = native with { FilePath = "a.dawproject", Evidence = EvidenceLevel.Confirmed };
        Assert.True(CprCandidateComparisonService.Supports(native));
        Assert.False(CprCandidateComparisonService.Supports(confirmed));
        Assert.Throws<ArgumentException>(() =>
            CprCandidateComparisonService.Compare(native, confirmed));
    }

    [Fact]
    public void ExportsExplainCandidateProvenanceAndEscapeCsvFormulas()
    {
        var result = CprCandidateComparisonService.Compare(
            Cpr("a.cpr", Token(A, "=SUSPICIOUS")),
            Cpr("b.cpr", Token(A, "SafeName")));
        var markdown = CprCandidateComparisonService.Markdown(result, "a.cpr", "b.cpr");
        Assert.Contains("unverified binary property-token", markdown);
        Assert.Contains("not", markdown, StringComparison.OrdinalIgnoreCase);
        var csv = CprCandidateComparisonService.Csv(result);
        Assert.Contains("'=SUSPICIOUS", csv);
        Assert.Contains("candidate", csv, StringComparison.OrdinalIgnoreCase);
    }
}
