using System.Globalization;
using System.Text;

namespace CubaseInspector.Core;

/// <summary>Exports the visible grouped projection; source evidence is not upgraded.</summary>
public static class ProjectPluginGroupReport
{
    public static string Csv(IEnumerable<ProjectPluginGroup> groups)
    {
        var sb = new StringBuilder("GroupKey,Plugin,Vendor,Format,Evidence,ObservationCount,SourceName,SourceTrackId,SourceSlot,SourceOffsetBytes,Location").AppendLine();
        foreach (var group in groups)
            foreach (var use in group.Uses)
                sb.AppendLine(string.Join(",", new[] {
                    Cell(group.Identity), Cell(group.Name), Cell(group.Vendor), Cell(group.Format),
                    Cell(group.Evidence.ToString()), group.Observations.ToString(CultureInfo.InvariantCulture),
                    Cell(use.Reference.Name), Cell(use.Reference.TrackId),
                    use.Reference.Slot?.ToString(CultureInfo.InvariantCulture) ?? "",
                    use.Reference.SourceByteOffset?.ToString(CultureInfo.InvariantCulture) ?? "",
                    Cell(use.Location)
                }));
        return sb.ToString();
    }

    public static string Markdown(IEnumerable<ProjectPluginGroup> groups, string? projectName = null)
    {
        var rows = groups.ToArray();
        var sb = new StringBuilder("# Grouped project plugin report").AppendLine().AppendLine();
        if (!string.IsNullOrWhiteSpace(projectName)) sb.AppendLine("Project: " + Md(projectName)).AppendLine();
        sb.AppendLine($"Visible groups: {rows.Length}; source observations: {rows.Sum(x => x.Observations)}.").AppendLine()
            .AppendLine("This report reflects currently filtered project plugin records. " +
                        "Confirmed exported references are not verified installations; native CPR " +
                        "candidate tokens are NOT authenticated insert instances.").AppendLine();
        foreach (var group in rows)
        {
            sb.AppendLine("## " + Md(group.Name)).AppendLine()
                .AppendLine("Vendor: " + Md(group.Vendor ?? "Unknown") +
                            "; format: " + Md(group.Format) + "; evidence: " + group.Evidence +
                            "; " + group.CountLabel).AppendLine();
            foreach (var use in group.Uses)
                sb.AppendLine("- " + Md(use.Location) + " — " + Md(use.Reference.Name) +
                    (use.Reference.SourceByteOffset is long offset
                        ? " (candidate offset " + offset.ToString(CultureInfo.InvariantCulture) + ")" : ""));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Cell(string? raw)
    {
        var value = raw ?? "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static string Md(string value) => value.Replace("\r", " ").Replace("\n", " ")
        .Replace("|", "\\|").Replace("<", "&lt;").Replace(">", "&gt;");
}
