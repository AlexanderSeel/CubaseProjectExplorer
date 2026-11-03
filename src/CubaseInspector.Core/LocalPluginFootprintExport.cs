using System.Globalization;
using System.Text;

namespace CubaseInspector.Core;

/// <summary>Evidence-qualified reports of a completed local file-size measurement.</summary>
public static class LocalPluginFootprintExport
{
    public static string Csv(LocalPluginFootprintReport report)
    {
        var sb = new StringBuilder("BundlePath,LogicalBytes,FileCount,Complete,Warning,ConfirmedReferences").AppendLine();
        foreach (var row in report.Bundles)
            sb.AppendLine(string.Join(",", new[]
            {
                Cell(row.Path), row.Complete ? row.LogicalBytes.ToString(CultureInfo.InvariantCulture) : "",
                row.Files.ToString(CultureInfo.InvariantCulture),
                row.Complete ? "true" : "false", Cell(row.Warning),
                row.ConfirmedReferences.ToString(CultureInfo.InvariantCulture)
            }));
        return sb.ToString();
    }

    public static string Markdown(LocalPluginFootprintReport report)
    {
        var sb = new StringBuilder("# Local VST3 bundle footprint").AppendLine().AppendLine()
            .AppendLine(report.Notice).AppendLine()
            .AppendLine($"Complete bundle logical bytes: {report.CompleteLogicalBytes.ToString(CultureInfo.InvariantCulture)}; " +
                        $"exact references: {report.ExactReferences}; excluded references: {report.ExcludedReferences}; " +
                        $"all bundles complete: {report.Complete}.").AppendLine()
            .AppendLine("| Bundle path | Logical bytes (only if complete) | Files seen | Complete | Warning | Exact references |")
            .AppendLine("|---|---:|---:|---|---|---:|");
        foreach (var row in report.Bundles)
            sb.AppendLine($"| {Md(row.Path)} | {(row.Complete ? row.LogicalBytes.ToString(CultureInfo.InvariantCulture) : "Unknown (incomplete)")} | " +
                          $"{row.Files} | {(row.Complete ? "Yes" : "No")} | {Md(row.Warning)} | {row.ConfirmedReferences} |");
        return sb.ToString();
    }

    private static string Cell(string? raw)
    {
        var value = raw ?? "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string Md(string? value) => (value ?? "").Replace("|", "\\|")
        .Replace("\r", " ").Replace("\n", " ");
}
