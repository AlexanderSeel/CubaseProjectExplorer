using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class BackupProjectAssociationTests
{
    private static ProjectLibraryEntry Entry(string path) => new(
        new ProjectSnapshot(path, Path.GetFileNameWithoutExtension(path), "CPR",
            null, null, EvidenceLevel.Candidate, [], [], [], []), null, null);

    [Fact]
    public void SuggestsOnlyAUniqueSameFolderSameNameProject()
    {
        var backup = Entry(Path.Combine(Path.GetTempPath(), "session.bak"));
        var project = Entry(Path.Combine(Path.GetTempPath(), "session.cpr"));
        var match = BackupProjectAssociationService.Resolve(backup, [backup, project]);

        Assert.Equal(project.Project.FilePath, match?.ProjectPath);
        Assert.Equal(BackupLinkEvidence.UniqueSameFolderAndNameCandidate, match?.Evidence);
        Assert.Equal(match, Assert.Single(BackupProjectAssociationService.ResolveAll([backup, project])));
    }

    [Fact]
    public void DoesNotSuggestAcrossFoldersOrWhenBasenameIsAmbiguous()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var backup = Entry(Path.Combine(root, "session.bak"));
        var otherFolder = Entry(Path.Combine(root, "elsewhere", "session.cpr"));
        Assert.Null(BackupProjectAssociationService.Resolve(backup, [backup, otherFolder]));

        var second = Entry(Path.Combine(root, "SESSION.cpr"));
        Assert.Null(BackupProjectAssociationService.Resolve(backup, [backup, otherFolder with
        {
            Project = otherFolder.Project with { FilePath = Path.Combine(root, "session.cpr") }
        }, second]));
    }

    [Fact]
    public void ManualAssociationOverridesCandidateAndMustReferToScannedCpr()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var backup = Entry(Path.Combine(root, "autosave.bak"));
        var suggested = Entry(Path.Combine(root, "autosave.cpr"));
        var chosen = Entry(Path.Combine(root, "Session Final.cpr"));
        var manual = BackupProjectAssociationService.SetManual(
            new Dictionary<string, string>(), backup.Project.FilePath, chosen.Project.FilePath);

        var resolved = BackupProjectAssociationService.Resolve(backup, [backup, suggested, chosen], manual);
        Assert.Equal(chosen.Project.FilePath, resolved?.ProjectPath);
        Assert.Equal(BackupLinkEvidence.UserConfirmed, resolved?.Evidence);
        Assert.Equal(resolved, Assert.Single(BackupProjectAssociationService.ResolveAll(
            [backup, suggested, chosen], manual)));
        Assert.Throws<ArgumentException>(() => BackupProjectAssociationService.SetManual(
            manual, backup.Project.FilePath, Path.Combine(root, "audio.wav")));
    }

    [Fact]
    public void RemovesManualAssociationWithoutChangingOtherLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var a = Path.Combine(root, "a.bak");
        var b = Path.Combine(root, "b.bak");
        var links = BackupProjectAssociationService.SetManual(new Dictionary<string, string>(),
            a, Path.Combine(root, "a.cpr"));
        links = BackupProjectAssociationService.SetManual(links, b, Path.Combine(root, "b.cpr"));

        var remaining = BackupProjectAssociationService.RemoveManual(links, a);
        Assert.Single(remaining);
        Assert.Contains(b, remaining.Keys);
    }

    [Fact]
    public void SettingsRoundtripAndAtomicSavePreserveLinks()
    {
        var root = Directory.CreateTempSubdirectory("backup-links-");
        try
        {
            var backup = Path.Combine(root.FullName, "mix.bak");
            var project = Path.Combine(root.FullName, "mix.cpr");
            var links = BackupProjectAssociationService.SetManual(new Dictionary<string, string>(),
                backup, project);
            var path = Path.Combine(root.FullName, "associations.json");

            BackupProjectAssociationService.SaveFile(path, links);
            var loaded = BackupProjectAssociationService.LoadOrEmpty(File.ReadAllText(path));

            Assert.True(loaded.CanWrite);
            Assert.Equal(project, Assert.Single(loaded.Associations).Value);
            Assert.Single(Directory.GetFiles(root.FullName));
        }
        finally { root.Delete(true); }
    }

    [Theory]
    [InlineData("{ malformed")]
    [InlineData("{\"Version\":9,\"Associations\":[]}")]
    [InlineData("{\"Version\":1,\"Associations\":[null]}")]
    [InlineData("{\"Version\":1,\"Associations\":[{\"BackupPath\":\"a.cpr\",\"ProjectPath\":\"b.bak\"}]}")]
    public void InvalidSettingsDisableWritesAndAreNeverOverwritten(string json)
    {
        var loaded = BackupProjectAssociationService.LoadOrEmpty(json);
        Assert.False(loaded.CanWrite);
        Assert.NotNull(loaded.Warning);
        Assert.Throws<InvalidOperationException>(() => BackupProjectAssociationService.SaveFile(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "links.json"),
            loaded.Associations, loaded.CanWrite));
    }

    [Fact]
    public void StaleManualAssociationDoesNotFallBackToFilenameSuggestion()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var backup = Entry(Path.Combine(root, "session.bak"));
        var sameName = Entry(Path.Combine(root, "session.cpr"));
        var staleTarget = Path.Combine(root, "removed-project.cpr");
        var manual = new Dictionary<string, string>
        {
            [backup.Project.FilePath] = staleTarget
        };

        Assert.Null(BackupProjectAssociationService.Resolve(backup, [backup, sameName], manual));
    }
}
