using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectMediaReadinessTests
{
    [Fact]
    public void ChecksOnlyExplicitExternalRelativePathsAndDoesNotReadFileContents()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-media-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "audio"));
        var projectPath = Path.Combine(root, "session.dawproject");
        var mediaPath = Path.Combine(root, "audio", "vocal.wav");
        var originalBytes = new byte[] { 0, 1, 2, 255 };
        File.WriteAllBytes(mediaPath, originalBytes);
        try
        {
            var project = Project(projectPath,
                new ProjectMediaReference("audio/vocal.wav", true, null, null, null),
                new ProjectMediaReference("audio/missing.wav", true, null, null, null),
                new ProjectMediaReference("audio", true, null, null, null),
                new ProjectMediaReference("audio/vocal.wav", false, null, null, null),
                new ProjectMediaReference("audio/unknown.wav", null, null, null, null),
                new ProjectMediaReference(mediaPath, true, null, null, null));

            var report = ProjectMediaReadiness.Analyze(project);

            Assert.Equal(6, report.Items.Count);
            Assert.Equal(ProjectMediaReadinessState.Present, report.Items[0].State);
            Assert.Equal(ProjectMediaReadinessState.Missing, report.Items[1].State);
            Assert.Equal(ProjectMediaReadinessState.Unsupported, report.Items[2].State);
            Assert.Equal(ProjectMediaReadinessState.EmbeddedNotChecked, report.Items[3].State);
            Assert.Equal(ProjectMediaReadinessState.Unsupported, report.Items[4].State);
            Assert.Equal(ProjectMediaReadinessState.Present, report.Items[5].State);
            Assert.Equal(3, report.UniqueExternalPathsChecked);
            Assert.Equal(originalBytes, File.ReadAllBytes(mediaPath));
            var markdown = ProjectMediaReadiness.Markdown(report);
            Assert.Contains("audio/vocal.wav", markdown);
            Assert.DoesNotContain(root, markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("do not read audio", report.Limitation, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CandidateNativeProjectDoesNotTreatReferencesAsExternalMediaEvidence()
    {
        var report = ProjectMediaReadiness.Analyze(Project(Path.Combine(Path.GetTempPath(), "candidate.cpr"),
            new ProjectMediaReference("audio.wav", true, null, null, null), evidence: EvidenceLevel.Candidate));

        Assert.Empty(report.Items);
        Assert.Contains("Native CPR/BAK", report.Limitation);
    }

    [Fact]
    public void DuplicateDeclarationsReuseOneFilesystemCheckButRemainVisibleAsSeparateReferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpe-media-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "loop.wav"), [1]);
        try
        {
            var reference = new ProjectMediaReference("loop.wav", true, null, null, null);
            var report = ProjectMediaReadiness.Analyze(Project(Path.Combine(root, "session.dawproject"), reference, reference));

            Assert.Equal(2, report.Items.Count);
            Assert.All(report.Items, item => Assert.Equal(ProjectMediaReadinessState.Present, item.State));
            Assert.Equal(1, report.UniqueExternalPathsChecked);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ProjectSnapshot Project(string path, ProjectMediaReference first,
        ProjectMediaReference? second = null, ProjectMediaReference? third = null,
        ProjectMediaReference? fourth = null, ProjectMediaReference? fifth = null,
        ProjectMediaReference? sixth = null,
        EvidenceLevel evidence = EvidenceLevel.Confirmed)
    {
        var references = new[] { first, second, third, fourth, fifth, sixth }.OfType<ProjectMediaReference>().ToArray();
        return new ProjectSnapshot(path, Path.GetFileNameWithoutExtension(path), "DAWproject", null, null,
            evidence, [], [], [], [], MediaReferences: references);
    }
}
