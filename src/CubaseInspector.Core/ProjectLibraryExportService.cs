using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CubaseInspector.Core;

/// <summary>Explicitly exported, currently displayed read-only project metadata or failed scan inputs.</summary>
public static class ProjectLibraryExportService
{
    public static string Csv(IEnumerable<ProjectLibraryEntry> entries, string? folder = null)
    {
        var result = new StringBuilder(
            "Name,FilePath,FileType,Format,Evidence,CreatorVersion,TempoBpm,TrackRecords,PluginReferencesOrMarkers,ParserWarnings,SizeBytes,FileModifiedUtc,FieldAvailability")
            .AppendLine();
        foreach (var entry in entries)
        {
            var p = entry.Project;
            result.AppendLine(string.Join(",", new[]
            {
                p.Name, ExportPath(p.FilePath, folder), Path.GetExtension(p.FilePath).TrimStart('.').ToUpperInvariant(),
                p.Format, p.Evidence.ToString(), p.CreatorVersion,
                p.Bpm?.ToString("R", CultureInfo.InvariantCulture),
                (entry.TrackRecordCount ?? p.Tracks.Count).ToString(CultureInfo.InvariantCulture),
                (entry.PluginReferenceCount ?? p.Plugins.Count).ToString(CultureInfo.InvariantCulture),
                (entry.ParserWarningCount ?? p.Warnings.Count).ToString(CultureInfo.InvariantCulture),
                entry.SizeBytes?.ToString(CultureInfo.InvariantCulture),
                entry.LastModifiedUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                entry.IsSummary ? "Open project for field-level availability" :
                    string.Join("; ", ProjectFieldCoverage.Evaluate(p).Select(field =>
                        field.Key + "=" + field.Value))
            }.Select(CsvCell)));
        }
        return result.ToString();
    }

    public static string Markdown(IEnumerable<ProjectLibraryEntry> entries, string? folder)
    {
        var rows = entries.ToArray();
        var b = new StringBuilder("# Filtered project library")
            .AppendLine().AppendLine()
            .AppendLine("Scope: currently displayed readable projects, with the current filters, group and ordering.")
            .AppendLine("Scan root: omitted; file paths are relative where possible.").AppendLine()
            .AppendLine($"Displayed project files: {rows.Length}.")
            .AppendLine("CPR candidate records are not verified Cubase track/plugin identities. ")
            .AppendLine("File modification time is filesystem metadata, not plugin last use.")
            .AppendLine()
            .AppendLine("| Project | Type | Evidence | Version | BPM | Track records | Plugin refs/markers | Size bytes | File modified UTC | File path |")
            .AppendLine("|---|---|---|---|---:|---:|---:|---:|---|---|");
        foreach (var entry in rows)
        {
            var p = entry.Project;
            b.AppendLine("| " + string.Join(" | ", new[]
            {
                Clean(p.Name), Clean(Path.GetExtension(p.FilePath).TrimStart('.').ToUpperInvariant()),
                Clean(p.Evidence.ToString()), Clean(p.CreatorVersion),
                p.Bpm?.ToString("R", CultureInfo.InvariantCulture) ?? "Unknown",
                (entry.TrackRecordCount ?? p.Tracks.Count).ToString(CultureInfo.InvariantCulture),
                (entry.PluginReferenceCount ?? p.Plugins.Count).ToString(CultureInfo.InvariantCulture),
                entry.SizeBytes?.ToString(CultureInfo.InvariantCulture) ?? "Unknown",
                Clean(entry.LastModifiedUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)),
                Clean(ExportPath(p.FilePath, folder))
            }) + " |");
        }
        return b.ToString();
    }

    public static string Json(IEnumerable<ProjectLibraryEntry> entries, string? folder)
    {
        var rows = entries.ToArray();
        var document = new
        {
            schemaVersion = 1,
            scope = "Currently displayed readable projects, with active filters and ordering.",
            pathPolicy = "Project paths are relative to the scan root; absolute scan-root paths are omitted.",
            projects = rows.Select(entry =>
            {
                var project = entry.Project;
                var candidate = project.Evidence == EvidenceLevel.Candidate;
                var coverage = ProjectFieldCoverage.Evaluate(project);
                return new
                {
                    name = project.Name,
                    path = ExportPath(project.FilePath, folder),
                    fileType = Path.GetExtension(project.FilePath).TrimStart('.').ToUpperInvariant(),
                    format = project.Format,
                    evidence = project.Evidence.ToString(),
                    creatorVersion = project.CreatorVersion,
                    tempoBpm = project.Bpm,
                    tempoState = coverage["Tempo"].ToString(),
                    fieldAvailability = entry.IsSummary ? null : coverage.ToDictionary(field => field.Key,
                        field => field.Value.ToString(), StringComparer.Ordinal),
                    fieldAvailabilityState = entry.IsSummary ? "Open project for field-level availability" : "Evaluated",
                    trackRecords = entry.TrackRecordCount ?? project.Tracks.Count,
                    trackRecordEvidence = candidate ? "Candidate" : "ConfirmedExportRecords",
                    pluginReferencesOrMarkers = entry.PluginReferenceCount ?? project.Plugins.Count,
                    pluginReferenceEvidence = candidate ? "CandidateMarkers" : "ExportReferences",
                    routeCount = candidate ? (int?)null : entry.RoutingCount ?? project.Routing.Count,
                    routeState = candidate ? "Unknown" : entry.IsSummary ? "Stored summary; inspect project for detail" : project.Routing.Count > 0 ? "Present" : "NotExported",
                    parserWarnings = entry.IsSummary ? null : project.Warnings,
                    parserWarningCount = entry.ParserWarningCount ?? project.Warnings.Count,
                    sizeBytes = entry.SizeBytes,
                    fileModifiedUtc = entry.LastModifiedUtc?.ToUniversalTime()
                };
            }).ToArray()
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string IssuesCsv(IEnumerable<(string Path, string Message)> issues)
    {
        var b = new StringBuilder("FilePath,ErrorCode,Message").AppendLine();
        foreach (var issue in issues)
            b.AppendLine(string.Join(",", new[]
            {
                issue.Path, ScanIssueBrowser.Category(issue.Message), issue.Message
            }.Select(CsvCell)));
        return b.ToString();
    }

    public static string IssuesMarkdown(IEnumerable<(string Path, string Message)> issues)
    {
        var rows = issues.ToArray();
        var b = new StringBuilder("# Filtered scan failures")
            .AppendLine().AppendLine()
            .AppendLine($"Displayed scan errors: {rows.Length}. Only displayed failures are exported.")
            .AppendLine().AppendLine()
            .AppendLine("| Failed input | Error code | Reported error |")
            .AppendLine("|---|---|---|");
        foreach (var issue in rows)
            b.AppendLine("| " + Clean(issue.Path) + " | " +
                Clean(ScanIssueBrowser.Category(issue.Message)) + " | " +
                Clean(issue.Message) + " |");
        return b.ToString();
    }

    private static string Clean(string? source) =>
        (source ?? "Unknown").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string CsvCell(string? source)
    {
        var value = source ?? "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string ExportPath(string path, string? folder)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(folder)) return fileName;
        try
        {
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(root, full);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)) return fileName;
            return relative;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { return fileName; }
    }
}
