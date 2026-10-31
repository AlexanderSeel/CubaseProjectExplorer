namespace CubaseInspector.Core;

public sealed record LocalClassProjectReference(
    string ProjectName, string FilePath, int Instances)
{
    public string Display => ProjectName + " · " + Instances +
        (Instances == 1 ? " confirmed plugin reference" : " confirmed plugin references");
}

/// <summary>
/// Finds actual confirmed exported VST3 class ID references, never CPR candidate
/// strings, filename similarities, or last-used timestamps.
/// </summary>
public static class LocalClassProjectUsage
{
    public static IReadOnlyList<LocalClassProjectReference> ForClass(
        IEnumerable<ProjectSnapshot> projects, string? classId)
    {
        var canonical = Vst3ClassIdentity.Canonical(classId);
        if (canonical is null) return [];
        return projects
            .Where(project => project.Evidence == EvidenceLevel.Confirmed)
            .Select(project => new LocalClassProjectReference(
                project.Name, project.FilePath, project.Plugins.Count(reference =>
                    reference.Evidence == EvidenceLevel.Confirmed &&
                    reference.Format.Contains("vst3", StringComparison.OrdinalIgnoreCase) &&
                    Vst3ClassIdentity.Canonical(reference.Id) == canonical)))
            .Where(row => row.Instances > 0)
            .GroupBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LocalClassProjectReference(group.First().ProjectName,
                group.Key, group.Max(x => x.Instances)))
            .OrderByDescending(row => row.Instances)
            .ThenBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
