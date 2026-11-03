using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprLocalUidAdvisoryTests
{
    private const string Cid = "56535473796C3173796C656E74683100";

    private static PluginReference Candidate(string uid = Cid) =>
        new(uid, "Sylenth1", null, "CPR UID (format unverified)", null,
            EvidenceLevel.Candidate);

    private static InstalledPlugin Module(string path) =>
        new("Bundled Synth", path, "VST3",
            [new InstalledPluginClass(Cid, "Some declared module", "Example Vendor",
                "1.0", "Audio Module Class")]);

    [Fact]
    public void ReportsUniqueCandidateStringEqualityWithoutUpgradingIdentity()
    {
        var evidence = CprLocalUidAdvisory.Analyze(Candidate(),
            [Module(@"C:\VST3\first.vst3")], true);
        Assert.Equal(CprLocalUidEvidenceKind.UniqueUidCoincidence, evidence.Kind);
        Assert.Equal(@"C:\VST3\first.vst3", evidence.BundlePath);
        Assert.Equal(Cid, evidence.MatchedClass?.Cid);
        Assert.Contains("Advisory", evidence.Description);
        Assert.Contains("UNKNOWN", evidence.Description);
    }

    [Fact]
    public void DoesNotClaimMissingWhenNoLocalClassDeclarationExists()
    {
        var evidence = CprLocalUidAdvisory.Analyze(Candidate(), [], true);
        Assert.Equal(CprLocalUidEvidenceKind.NoDeclaration, evidence.Kind);
        Assert.Null(evidence.MatchedClass);
        Assert.Contains("does NOT prove", evidence.Description);
    }

    [Fact]
    public void MultipleSameUidDeclarationsCannotSelectAnyOne()
    {
        var evidence = CprLocalUidAdvisory.Analyze(Candidate(),
            [Module("first.vst3"), Module("second.vst3")], true);
        Assert.Equal(CprLocalUidEvidenceKind.Ambiguous, evidence.Kind);
        Assert.Null(evidence.MatchedClass);
    }

    [Fact]
    public void DoesNotTreatConfirmedOrUnknownFormatAsNativeCandidate()
    {
        Assert.Equal(CprLocalUidEvidenceKind.NotApplicable,
            CprLocalUidAdvisory.Analyze(Candidate() with {
                Evidence = EvidenceLevel.Confirmed }, [Module("first.vst3")], true).Kind);
        Assert.Equal(CprLocalUidEvidenceKind.NotApplicable,
            CprLocalUidAdvisory.Analyze(Candidate() with {
                Format = "VST3" }, [Module("first.vst3")], true).Kind);
        Assert.Equal(CprLocalUidEvidenceKind.NotApplicable,
            CprLocalUidAdvisory.Analyze(Candidate("offset-ABC"), [Module("first.vst3")], true).Kind);
    }

    [Fact]
    public void ExplicitNotScannedStatusDoesNotAssumeNoMatch()
    {
        var evidence = CprLocalUidAdvisory.Analyze(Candidate(),
            [Module("first.vst3")], false);
        Assert.Equal(CprLocalUidEvidenceKind.NotScanned, evidence.Kind);
        Assert.Null(evidence.BundlePath);
    }
}
