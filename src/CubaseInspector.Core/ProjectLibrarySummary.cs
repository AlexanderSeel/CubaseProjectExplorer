namespace CubaseInspector.Core;

/// <summary>Compact library triage counts. Candidate evidence and parser warnings remain separate.</summary>
public static class ProjectLibrarySummary
{
    public static string Describe(IEnumerable<ProjectLibraryEntry> entries, int visibleCount, int scanIssueCount)
    {
        var projects = entries.ToArray();
        var candidates = projects.Count(entry => entry.Project.Evidence == EvidenceLevel.Candidate);
        var warnings = projects.Count(entry => (entry.ParserWarningCount ?? entry.Project.Warnings.Count) > 0);
        return $"{visibleCount:N0} shown of {projects.Length:N0} readable · " +
               $"{candidates:N0} candidate evidence · {warnings:N0} with parser warnings · " +
               $"last scan: {scanIssueCount:N0} failed inputs";
    }
}
