using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprUidCandidateIndexTests
{
    private const string UidA = "56535473796C3173796C656E74683100";
    private const string UidB = "297BA567D83144E1AE921DEF07B41156";

    private static ProjectSnapshot Cpr(string path, params PluginReference[] plugins) =>
        new(path, Path.GetFileNameWithoutExtension(path), "CPR structural evidence",
            "Cubase 14", null, EvidenceLevel.Candidate,
            [], plugins, [], []);

    private static PluginReference Candidate(string id, string name) =>
        new(id, name, null, "CPR UID (format unverified)", null, EvidenceLevel.Candidate);

    [Fact]
    public void IndexesUniqueUidAcrossCprFilesWithoutCallingTokensInstances()
    {
        var index = CprUidCandidateIndexService.Build([
            Cpr(@"C:\Projects\one.cpr", Candidate(UidA, "Sylenth1"),
                Candidate(UidA, "Sylenth1"), Candidate("offset-1", "Unresolved plugin UID")),
            Cpr(@"C:\Projects\two.cpr", Candidate(UidA, "Sylenth 1"),
                Candidate(UidB, "StudioEQ")),
            new(@"C:\Projects\export.dawproject", "Export", "DAWproject", null, null,
                EvidenceLevel.Confirmed, [], [new(UidA, "Sylenth1", null, "VST3", null,
                    EvidenceLevel.Confirmed)], [], [])
        ]);
        Assert.Equal(2, index.CprProjects);
        Assert.Equal(2, index.UniqueUids);
        Assert.Equal(4, index.NamedTokens);
        Assert.Equal(1, index.OffsetOnlyMarkers);
        var first = Assert.Single(index.Candidates.Where(x => x.Uid == UidA));
        Assert.Equal(2, first.ProjectCount);
        Assert.Equal(3, first.TokenOccurrences);
        Assert.True(first.ConflictingNames);
        Assert.Equal(2, first.StoredNames.Count);
        Assert.Single(CprUidCandidateIndexService.Search(index, "sylenth", onlyConflicts: true));
    }

    [Fact]
    public void UidOnlyTokensAppearAsUnknownNamesButRemainCandidate()
    {
        var index = CprUidCandidateIndexService.Build([
            Cpr("/projects/uid-only.bak", Candidate(UidA, "Name not decoded"),
                Candidate("offset-42", "Unresolved plugin UID"))]);
        Assert.Equal(1, index.UidOnlyTokens);
        Assert.Equal(1, index.OffsetOnlyMarkers);
        var candidate = Assert.Single(CprUidCandidateIndexService.Search(index, null,
            onlyUnknownNames: true));
        Assert.Equal(UidA, candidate.Uid);
        Assert.Empty(candidate.StoredNames);
        Assert.Equal("Name not stored/decoded", candidate.Name);
        Assert.Equal("1", candidate.ProjectCount.ToString());
    }

    [Fact]
    public void OnlyUniqueStoredNameCanBeSuggestedForUndecodedUid()
    {
        var index = CprUidCandidateIndexService.Build([
            Cpr("/projects/a.cpr", Candidate(UidA, "Sylenth1"),
                Candidate(UidB, "StudioEQ")),
            Cpr("/projects/b.cpr", Candidate(UidA, "Name not decoded"))
        ]);
        Assert.Equal("Sylenth1", CprUidCandidateIndexService.UniqueNameHint(index,
            Candidate(UidA, "Name not decoded")));
        Assert.Null(CprUidCandidateIndexService.UniqueNameHint(index,
            Candidate(UidA, "Sylenth1")));
        Assert.Null(CprUidCandidateIndexService.UniqueNameHint(index,
            new PluginReference(UidA, "Name not decoded", null, "VST3", null,
                EvidenceLevel.Confirmed)));
        var conflicting = CprUidCandidateIndexService.Build([
            Cpr("/projects/a.cpr", Candidate(UidA, "Sylenth1")),
            Cpr("/projects/b.cpr", Candidate(UidA, "Different name"))
        ]);
        Assert.Null(CprUidCandidateIndexService.UniqueNameHint(conflicting,
            Candidate(UidA, "Name not decoded")));
    }

    [Fact]
    public void CsvExportEscapesFormulaNamesAndSeparatesProvenance()
    {
        var row = new CprUidCandidate(UidA, ["=HYPERLINK(\"x\")"], 1, ["/home/a.cpr"]);
        var csv = CprUidCandidateIndexService.ExportCsv([row]);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
        Assert.Contains("Candidate CPR binary metadata", csv);
    }
}
