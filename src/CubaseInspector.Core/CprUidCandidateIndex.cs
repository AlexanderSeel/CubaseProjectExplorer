using System.Text;

namespace CubaseInspector.Core;

/// <summary>
/// Indexes observed native CPR UID/name property tokens across readable project files.
/// These are NOT verified plugin instances, channels, installations, or last-used events.
/// Kept completely separate from confirmed DAWproject plugin usage.
/// </summary>
public sealed record CprUidCandidate(
    string Uid, IReadOnlyList<string> StoredNames, int TokenOccurrences,
    IReadOnlyList<string> ProjectPaths)
{
    public string Name => StoredNames.Count == 0 ? "Name not stored/decoded" :
        StoredNames.Count == 1 ? StoredNames[0] : StoredNames[0] + " (multiple recorded names)";
    public int ProjectCount => ProjectPaths.Count;
    public bool ConflictingNames => StoredNames.Count > 1;
    public string NamesLabel => StoredNames.Count == 0 ? "Unknown" : string.Join(" / ", StoredNames);
}

public sealed record CprUidCandidateIndex(
    IReadOnlyList<CprUidCandidate> Candidates, int CprProjects, int NamedTokens,
    int UidOnlyTokens, int OffsetOnlyMarkers)
{
    public int UniqueUids => Candidates.Count;
    public int AllTokens => NamedTokens + UidOnlyTokens + OffsetOnlyMarkers;
}

public static class CprUidCandidateIndexService
{
    public static CprUidCandidateIndex Build(IEnumerable<ProjectSnapshot> projects)
    {
        var input = projects.Where(p => p.Evidence == EvidenceLevel.Candidate &&
            (Path.GetExtension(p.FilePath).Equals(".cpr", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(p.FilePath).Equals(".bak", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(p => p.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).ToArray();
        var observations = input.SelectMany(project => project.Plugins
            .Where(p => p.Evidence == EvidenceLevel.Candidate)
            .Select(p => (Project: project.FilePath, Plugin: p))).ToArray();

        static bool Named(PluginReference p) =>
            !p.Name.StartsWith("Unresolved", StringComparison.OrdinalIgnoreCase) &&
            !p.Name.StartsWith("Unknown plugin", StringComparison.OrdinalIgnoreCase) &&
            !p.Name.StartsWith("Name not ", StringComparison.OrdinalIgnoreCase);

        var withUid = observations.Select(x =>
                (x.Project, x.Plugin, Uid: Vst3ClassIdentity.Canonical(x.Plugin.Id)))
            .Where(x => x.Uid is not null).ToArray();
        var entries = withUid.GroupBy(x => x.Uid!, StringComparer.Ordinal)
            .Select(g =>
            {
                var names = g.Where(x => Named(x.Plugin))
                    .Select(x => x.Plugin.Name.Trim())
                    .Where(x => x.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                var paths = g.Select(x => x.Project)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                return new CprUidCandidate(g.Key, names, g.Count(), paths);
            }).OrderByDescending(x => x.ProjectCount)
            .ThenByDescending(x => x.TokenOccurrences)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var named = withUid.Count(x => Named(x.Plugin));
        return new CprUidCandidateIndex(entries, input.Length, named,
            withUid.Length - named, observations.Length - withUid.Length);
    }

    /// <summary>
    /// Candidate name suggestion from other observed CPR property tokens.
    /// Deliberately never mutates the source record or upgrades its Evidence.
    /// Ambiguous recorded names produce no automatic suggestion.
    /// </summary>
    public static string? UniqueNameHint(CprUidCandidateIndex index, PluginReference record)
    {
        if (record.Evidence != EvidenceLevel.Candidate ||
            record.Name != "Name not decoded" ||
            Vst3ClassIdentity.Canonical(record.Id) is not { } uid)
            return null;
        var found = index.Candidates.FirstOrDefault(x => x.Uid == uid);
        return found?.StoredNames.Count == 1 ? found.StoredNames[0] : null;
    }

    public static IReadOnlyList<CprUidCandidate> Search(
        CprUidCandidateIndex index, string? search, string sort = "Most projects",
        bool onlyConflicts = false, bool onlyUnknownNames = false)
    {
        var term = (search ?? "").Trim();
        var found = index.Candidates.Where(x =>
            (!onlyConflicts || x.ConflictingNames) &&
            (!onlyUnknownNames || x.StoredNames.Count == 0) &&
            (term.Length == 0 ||
             x.Uid.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             x.StoredNames.Any(n => n.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
             x.ProjectPaths.Any(path => path.Contains(term, StringComparison.OrdinalIgnoreCase))));
        return (sort switch
        {
            "Most tokens" => found.OrderByDescending(x => x.TokenOccurrences)
                .ThenByDescending(x => x.ProjectCount),
            "Name" => found.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(x => x.ProjectCount),
            "UID" => found.OrderBy(x => x.Uid, StringComparer.Ordinal),
            "Fewest projects" => found.OrderBy(x => x.ProjectCount)
                .ThenBy(x => x.TokenOccurrences),
            _ => found.OrderByDescending(x => x.ProjectCount)
                .ThenByDescending(x => x.TokenOccurrences)
        }).ThenBy(x => x.Uid, StringComparer.Ordinal).ToArray();
    }

    public static string ExportCsv(IReadOnlyList<CprUidCandidate> rows)
    {
        static string Cell(string? value)
        {
            var s = value ?? "";
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder("StoredUid,RecordedPluginNames,ProjectFiles,UidTokenOccurrences,ConflictingNames,ReferencingProjectPaths,Evidence")
            .AppendLine();
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", new[]
            {
                row.Uid, row.NamesLabel, row.ProjectCount.ToString(),
                row.TokenOccurrences.ToString(), row.ConflictingNames.ToString(),
                string.Join("; ", row.ProjectPaths),
                "Candidate CPR binary metadata; not a confirmed insert or installation"
            }.Select(Cell)));
        return sb.ToString();
    }
}
