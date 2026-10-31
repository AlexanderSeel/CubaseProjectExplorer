using System.Text;

namespace CubaseInspector.Core;

/// <summary>
/// Compares bounded, read-only native Cubase CPR/BAK plugin property tokens.
/// This does NOT compare plug-in instances, track ownership, plugin state, VST
/// format, or modifications to project files.
/// </summary>
public static class CprCandidateComparisonService
{
    public const string Category = "CPR plugin UID";
    public const string CoverageCategory = "CPR metadata coverage";
    private const string Evidence = "CPR candidate UID property tokens only; NOT verified plugin instances";

    public static bool Supports(ProjectSnapshot? project) => project is not null &&
        project.Evidence == EvidenceLevel.Candidate &&
        (Path.GetExtension(project.FilePath).Equals(".cpr", StringComparison.OrdinalIgnoreCase) ||
         Path.GetExtension(project.FilePath).Equals(".bak", StringComparison.OrdinalIgnoreCase));

    public static CprCandidateComparison Compare(ProjectSnapshot before, ProjectSnapshot after)
    {
        if (!Supports(before) || !Supports(after))
            throw new ArgumentException("Both inputs must be candidate-only native CPR/BAK snapshots.");

        static Dictionary<string, Entry> Index(ProjectSnapshot project)
        {
            return project.Plugins.Where(x => x.Evidence == EvidenceLevel.Candidate)
                .Select(x => (Plugin: x, Uid: Vst3ClassIdentity.Canonical(x.Id)))
                .Where(x => x.Uid is not null)
                .GroupBy(x => x.Uid!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => new Entry(g.Count(),
                    g.Select(x => x.Plugin.Name.Trim())
                        .Where(IsRecordedName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToArray()), StringComparer.Ordinal);
        }

        var a = Index(before);
        var b = Index(after);
        var changes = new List<ProjectChange>();
        var newCount = 0;
        var removedCount = 0;
        var nameCount = 0;
        var tokenCount = 0;
        foreach (var uid in a.Keys.Union(b.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            var old = a.GetValueOrDefault(uid);
            var next = b.GetValueOrDefault(uid);
            var title = "UID " + uid;
            if (old is null)
            {
                changes.Add(new ProjectChange(Category, title, "Added",
                    "Not observed in A", "Observed in B: " + FormatNames(next!),
                    Evidence));
                newCount++;
            }
            else if (next is null)
            {
                changes.Add(new ProjectChange(Category, title, "Removed",
                    "Observed in A: " + FormatNames(old), "Not observed in B",
                    Evidence + "; missing token does not prove plugin removal"));
                removedCount++;
            }
            else
            {
                if (!old.Names.SequenceEqual(next.Names, StringComparer.OrdinalIgnoreCase))
                {
                    changes.Add(new ProjectChange(Category, title, "Recorded names",
                        FormatNames(old), FormatNames(next),
                        Evidence + "; names may be track labels, aliases or metadata differences"));
                    nameCount++;
                }
                if (old.Count != next.Count)
                {
                    changes.Add(new ProjectChange(Category, title, "UID token occurrences",
                        old.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        next.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Evidence + "; counts are NOT insert-instance counts"));
                    tokenCount++;
                }
            }
        }

        var unresolvedA = before.Plugins.Count(x => x.Evidence == EvidenceLevel.Candidate &&
            Vst3ClassIdentity.Canonical(x.Id) is null);
        var unresolvedB = after.Plugins.Count(x => x.Evidence == EvidenceLevel.Candidate &&
            Vst3ClassIdentity.Canonical(x.Id) is null);
        if (unresolvedA != unresolvedB)
            changes.Add(new ProjectChange(CoverageCategory, "Unresolved CPR token markers",
                "Offset-only marker count", unresolvedA.ToString(), unresolvedB.ToString(),
                "Unknown identities; no per-plugin matching attempted"));

        return new CprCandidateComparison(changes, a.Count, b.Count,
            newCount, removedCount, nameCount, tokenCount, unresolvedA, unresolvedB);
    }

    public static string Markdown(CprCandidateComparison result, string before, string after)
    {
        var sb = new StringBuilder("# Native CPR candidate UID comparison").AppendLine().AppendLine()
            .AppendLine("- Project A: " + Md(before))
            .AppendLine("- Project B: " + Md(after))
            .AppendLine("- Distinct stored UIDs in A: " + result.BeforeUids)
            .AppendLine("- Distinct stored UIDs in B: " + result.AfterUids)
            .AppendLine("- New UID observations in B: " + result.NewUids)
            .AppendLine("- UIDs no longer observed in B: " + result.UnobservedUids)
            .AppendLine("- Changes in recorded names: " + result.NameChanges)
            .AppendLine("- Changes in UID token frequencies: " + result.TokenFrequencyChanges)
            .AppendLine("- Offset-only unresolved markers: A " + result.UnresolvedA + " / B " + result.UnresolvedB)
            .AppendLine()
            .AppendLine("**Evidence limitation:** This report compares unverified binary property-token observations. " +
                "A UID may occur repeatedly without representing separate plug-in instances. " +
                "A missing UID token does not prove the plug-in was removed, and names do not establish VST2/VST3 format, ownership, or installation.")
            .AppendLine()
            .AppendLine("| Category | Stored UID / observation | Property | A | B | Evidence |")
            .AppendLine("|---|---|---|---|---|---|");
        foreach (var c in result.Changes)
            sb.AppendLine("| " + Md(c.Category) + " | " + Md(c.Entity) +
                " | " + Md(c.Property) + " | " + Md(c.Before) + " | " +
                Md(c.After) + " | " + Md(c.MatchQuality) + " |");
        return sb.ToString();
    }

    public static string Csv(CprCandidateComparison result)
    {
        var sb = new StringBuilder("Category,StoredUidOrObservation,Property,ProjectA,ProjectB,Evidence").AppendLine();
        foreach (var c in result.Changes)
            sb.AppendLine(string.Join(",", new[]
            {
                c.Category, c.Entity, c.Property, c.Before, c.After, c.MatchQuality
            }.Select(CsvCell)));
        return sb.ToString();
    }

    private sealed record Entry(int Count, string[] Names);
    private static string FormatNames(Entry e) => e.Names.Length == 0
        ? "No typed plugin name decoded" : string.Join("; ", e.Names);
    private static bool IsRecordedName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !name.StartsWith("Unresolved", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("Name not decoded", StringComparison.OrdinalIgnoreCase);
    private static string Md(string? s) =>
        (s ?? "—").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string CsvCell(string? s)
    {
        var value = s ?? "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

public sealed record CprCandidateComparison(
    IReadOnlyList<ProjectChange> Changes,
    int BeforeUids, int AfterUids, int NewUids, int UnobservedUids,
    int NameChanges, int TokenFrequencyChanges, int UnresolvedA, int UnresolvedB);
