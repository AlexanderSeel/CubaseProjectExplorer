using System.Text;
namespace CubaseInspector.Core;

/// <summary>Exports diffs without changing original project files.</summary>
public sealed class ProjectChangeReportService
{
    public string Markdown(IReadOnlyList<ProjectChange> changes, string before, string after)
    {
        var b = new StringBuilder("# Project comparison").AppendLine().AppendLine();
        b.AppendLine("- Before: " + Clean(before))
            .AppendLine("- After: " + Clean(after))
            .AppendLine("- Changes: " + changes.Count)
            .AppendLine("Match quality is reported; heuristic matches are not definitive.")
            .AppendLine().AppendLine("| Category | Entity | Property | Before | After | Match |")
            .AppendLine("|---|---|---|---|---|---|");
        foreach (var change in changes)
            b.Append("| ").Append(Clean(change.Category)).Append(" | ")
                .Append(Clean(change.Entity)).Append(" | ")
                .Append(Clean(change.Property)).Append(" | ")
                .Append(Clean(change.Before)).Append(" | ")
                .Append(Clean(change.After)).Append(" | ")
                .Append(Clean(change.MatchQuality)).AppendLine(" |");
        return b.ToString();
    }
    public string Csv(IReadOnlyList<ProjectChange> changes)
    {
        var b = new StringBuilder("Category,Entity,Property,Before,After,MatchQuality").AppendLine();
        foreach (var change in changes)
            b.AppendLine(string.Join(",", new[] { change.Category, change.Entity, change.Property,
                change.Before, change.After, change.MatchQuality }.Select(CsvCell)));
        return b.ToString();
    }
    private static string Clean(string? text) =>
        (text ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string CsvCell(string? text)
    {
        var value = text ?? "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
