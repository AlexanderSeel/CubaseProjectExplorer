using System.Text;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Exportable diagnostic summary for the read-only CPR token scanner.
/// Does not include file contents, plug-in state, installed software,
/// track ownership or source directory paths.
/// </summary>
public static class CprPluginDiagnosticService
{
    public static CprPluginDiagnosticReport Build(CprPluginStreamResult scan, long fileBytes,
        string fileName, bool unresolvedOnly = false)
    {
        static string Status(PluginReference p) =>
            p.Id.StartsWith("offset-", StringComparison.Ordinal) ? "Unresolved marker" :
            p.Name.Equals("Name not decoded", StringComparison.Ordinal) ? "Stored UID, name unavailable" :
            "Stored UID and candidate name";

        var all = scan.Plugins;
        var rows = all.Where(p => !unresolvedOnly ||
                Status(p) != "Stored UID and candidate name")
            .Select(p => new CprPluginDiagnosticRow(
                p.SourceByteOffset, p.Id, p.Name, Status(p), p.Role ?? "No provenance",
                "CPR candidate property token; not a confirmed Cubase plugin instance"))
            .ToArray();

        return new CprPluginDiagnosticReport(fileName, fileBytes, scan.BytesRead,
            scan.Partial, scan.TokenCapReached,
            all.Count(p => Status(p) == "Stored UID and candidate name"),
            all.Count(p => Status(p) == "Stored UID, name unavailable"),
            all.Count(p => Status(p) == "Unresolved marker"),
            unresolvedOnly, rows);
    }

    public static string Markdown(CprPluginDiagnosticReport report)
    {
        var b = new StringBuilder("# CPR plugin UID metadata diagnostics\n\n")
            .AppendLine("- File: " + Md(report.FileName))
            .AppendLine("- File bytes: " + report.FileBytes)
            .AppendLine("- Scanned bytes: " + report.ScannedBytes)
            .AppendLine("- Complete scan: " + (!report.Partial))
            .AppendLine("- Hit global 10,000 token cap: " + report.TokenCapReached)
            .AppendLine("- Named UID tokens: " + report.NamedUidTokens)
            .AppendLine("- UID-only tokens: " + report.UidOnlyTokens)
            .AppendLine("- Unresolved markers: " + report.UnresolvedMarkers)
            .AppendLine("- Filter: " + (report.UnresolvedOnly ? "UID-only / unresolved" : "All"))
            .AppendLine()
            .AppendLine("Observations are candidate binary metadata, not validated plug-in instances, installed VST3 classes, track assignments, or evidence of missing software.")
            .AppendLine()
            .AppendLine("| File offset | Name | Stored UID / marker | Status | Provenance |")
            .AppendLine("|---:|---|---|---|---|");
        foreach (var row in report.Records)
            b.Append("| ").Append(row.ByteOffset?.ToString() ?? "unknown").Append(" | ")
                .Append(Md(row.Name)).Append(" | ").Append(Md(row.UidOrMarker))
                .Append(" | ").Append(Md(row.Status)).Append(" | ")
                .Append(Md(row.Provenance)).AppendLine(" |");
        return b.ToString();
    }

    public static string Csv(CprPluginDiagnosticReport report)
    {
        var b = new StringBuilder("FileOffset,StoredUidOrMarker,Name,Status,Provenance,Evidence").AppendLine();
        foreach (var row in report.Records)
            b.AppendLine(string.Join(",", new[]
            {
                row.ByteOffset?.ToString(), row.UidOrMarker, row.Name,
                row.Status, row.Provenance, row.Evidence
            }.Select(Cell)));
        return b.ToString();
    }

    private static string Md(string? value) =>
        (value ?? "unknown").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string Cell(string? value)
    {
        var s = value ?? "";
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}

public sealed record CprPluginDiagnosticRow(long? ByteOffset, string UidOrMarker,
    string Name, string Status, string Provenance, string Evidence);

public sealed record CprPluginDiagnosticReport(string FileName, long FileBytes,
    long ScannedBytes, bool Partial, bool TokenCapReached,
    int NamedUidTokens, int UidOnlyTokens, int UnresolvedMarkers,
    bool UnresolvedOnly, IReadOnlyList<CprPluginDiagnosticRow> Records);
