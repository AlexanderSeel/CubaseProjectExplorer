using System.Text.Json;
using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ProjectLibraryExportServiceTests
{
    [Fact]
    public void LibraryExportsUseRootRelativePathsAndPreserveCandidateUncertainty()
    {
        var project = new ProjectSnapshot(
            @"E:\Cubase\Private Sessions\mix.cpr", "mix", "CPR candidate", "Cubase 12",
            null, EvidenceLevel.Candidate, [],
            [new PluginReference("uid-candidate", "Plugin marker", null, "unknown", null, EvidenceLevel.Candidate)],
            [], []);
        var entry = new ProjectLibraryEntry(project, 2048, DateTimeOffset.Parse("2026-10-30T12:00:00Z"));

        var csv = ProjectLibraryExportService.Csv([entry], @"E:\Cubase");
        var markdown = ProjectLibraryExportService.Markdown([entry], @"E:\Cubase");
        var json = ProjectLibraryExportService.Json([entry], @"E:\Cubase");

        Assert.Contains(@"Private Sessions\mix.cpr", csv);
        Assert.Contains(@"Private Sessions\mix.cpr", markdown);
        Assert.Contains("FieldAvailability", csv);
        Assert.Contains("Plugin references=Candidate", csv);
        Assert.DoesNotContain(@"E:\Cubase", csv);
        Assert.DoesNotContain(@"E:\Cubase", markdown);
        using var document = JsonDocument.Parse(json);
        var exportedProject = document.RootElement.GetProperty("projects")[0];
        Assert.Equal(@"Private Sessions\mix.cpr", exportedProject.GetProperty("path").GetString());
        Assert.Equal("Unknown", exportedProject.GetProperty("tempoState").GetString());
        Assert.Equal("CandidateMarkers", exportedProject.GetProperty("pluginReferenceEvidence").GetString());
        Assert.Equal("Unknown", exportedProject.GetProperty("routeState").GetString());
        Assert.True(exportedProject.GetProperty("routeCount").ValueKind == JsonValueKind.Null);
        Assert.Equal("Candidate", exportedProject.GetProperty("fieldAvailability")
            .GetProperty("Plugin references").GetString());
    }

    [Fact]
    public void JsonIncludesPerFieldCandidateStateForNativeTempoEvidence()
    {
        var project = new ProjectSnapshot("mix.cpr", "mix", "CPR candidate", null,
            null, EvidenceLevel.Candidate, [], [], [], [],
            NativeMetadataCandidates: [new CprMetadataCandidate("Tempo event BPM", "120", 512, "Event", "Candidate")]);

        using var document = JsonDocument.Parse(ProjectLibraryExportService.Json(
            [new ProjectLibraryEntry(project, null, null)], null));
        var exportedProject = document.RootElement.GetProperty("projects")[0];

        Assert.Equal("Candidate", exportedProject.GetProperty("tempoState").GetString());
        Assert.Equal(JsonValueKind.Null, exportedProject.GetProperty("tempoBpm").ValueKind);
        Assert.Equal("Candidate", exportedProject.GetProperty("fieldAvailability")
            .GetProperty("Tempo").GetString());
    }
}
