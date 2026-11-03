using System.Security;
using System.Text;

namespace CubaseInspector.Core;

public enum ProjectMediaReadinessState
{
    EmbeddedNotChecked,
    Present,
    Missing,
    Inaccessible,
    Unsupported,
    NotChecked
}

public sealed record ProjectMediaReadinessItem(
    string DeclaredPath,
    ProjectMediaReadinessState State,
    string Detail);

public sealed record ProjectMediaReadinessReport(
    string ProjectName,
    IReadOnlyList<ProjectMediaReadinessItem> Items,
    int UniqueExternalPathsChecked,
    string Limitation)
{
    public string Summary =>
        $"{Items.Count(item => item.State == ProjectMediaReadinessState.Present):N0} present · " +
        $"{Items.Count(item => item.State == ProjectMediaReadinessState.Missing):N0} missing · " +
        $"{Items.Count(item => item.State == ProjectMediaReadinessState.Inaccessible):N0} inaccessible · " +
        $"{Items.Count(item => item.State == ProjectMediaReadinessState.Unsupported):N0} unsupported · " +
        $"{Items.Count(item => item.State == ProjectMediaReadinessState.EmbeddedNotChecked):N0} embedded (not checked) · " +
        $"{Items.Count(item => item.State == ProjectMediaReadinessState.NotChecked):N0} not checked · " +
        $"{UniqueExternalPathsChecked:N0} unique external paths checked";
}

/// <summary>Checks only explicit DAWproject external references using metadata calls; never opens media files.</summary>
public static class ProjectMediaReadiness
{
    public const int MaxUniqueExternalPaths = 5000;

    public static ProjectMediaReadinessReport Analyze(ProjectSnapshot project,
        CancellationToken cancellationToken = default)
    {
        var references = project.MediaReferences ?? [];
        var declaredDawproject = project.Evidence == EvidenceLevel.Confirmed &&
            Path.GetExtension(project.FilePath).Equals(".dawproject", StringComparison.OrdinalIgnoreCase);
        if (!declaredDawproject)
            return new(project.Name, [], 0,
                "External media checking is supported only for confirmed DAWproject exports. Native CPR/BAK media links remain unknown.");

        string? projectDirectory;
        try { projectDirectory = Path.GetDirectoryName(Path.GetFullPath(project.FilePath)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or SecurityException)
        {
            projectDirectory = null;
        }

        var statuses = new Dictionary<string, (ProjectMediaReadinessState State, string Detail)>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ProjectMediaReadinessItem>(references.Count);
        var checkedPaths = 0;
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.External == false)
            {
                items.Add(new(reference.Path, ProjectMediaReadinessState.EmbeddedNotChecked,
                    "Declared inside the DAWproject; archive entry contents were not checked."));
                continue;
            }
            if (reference.External is null)
            {
                items.Add(new(reference.Path, ProjectMediaReadinessState.Unsupported,
                    "The export does not declare whether this path is external."));
                continue;
            }
            if (projectDirectory is null || !TryResolve(projectDirectory, reference.Path, out var resolvedPath))
            {
                items.Add(new(reference.Path, ProjectMediaReadinessState.Unsupported,
                    "The declared path is not a supported relative file path."));
                continue;
            }
            if (statuses.TryGetValue(resolvedPath, out var existing))
            {
                items.Add(new(reference.Path, existing.State, existing.Detail));
                continue;
            }
            if (checkedPaths >= MaxUniqueExternalPaths)
            {
                items.Add(new(reference.Path, ProjectMediaReadinessState.NotChecked,
                    $"The safety limit of {MaxUniqueExternalPaths:N0} unique paths was reached."));
                continue;
            }

            var assessment = CheckPath(resolvedPath);
            statuses.Add(resolvedPath, assessment);
            checkedPaths++;
            items.Add(new(reference.Path, assessment.State, assessment.Detail));
        }

        return new(project.Name, items, checkedPaths,
            "File presence checks read filesystem attributes only. They do not read audio, validate media contents, or prove that a DAW can load a file. Embedded entries and native CPR/BAK references are not checked.");
    }

    public static string Markdown(ProjectMediaReadinessReport report)
    {
        static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        var builder = new StringBuilder("# DAWproject external media handoff check")
            .AppendLine().AppendLine()
            .AppendLine("Project: " + Cell(report.ProjectName))
            .AppendLine("Summary: " + report.Summary)
            .AppendLine("Scope: only explicitly external DAWproject references; declared relative paths are listed without exporting resolved absolute paths.")
            .AppendLine().AppendLine("| Declared path | Status | Details |")
            .AppendLine("|---|---|---|");
        foreach (var item in report.Items)
            builder.AppendLine($"| {Cell(ExportDeclaredPath(item.DeclaredPath))} | {item.State} | {Cell(item.Detail)} |");
        builder.AppendLine().AppendLine("## Limitations").AppendLine(Cell(report.Limitation));
        return builder.ToString();
    }

    private static string ExportDeclaredPath(string path) =>
        Path.IsPathRooted(path) || Uri.TryCreate(path, UriKind.Absolute, out _)
            ? "[absolute or URI path omitted for privacy]" : path;

    private static bool TryResolve(string projectDirectory, string? declaredPath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(declaredPath) || declaredPath.Length > 4096 || declaredPath.Contains('\0') ||
            (!Path.IsPathRooted(declaredPath) && Uri.TryCreate(declaredPath, UriKind.Absolute, out _))) return false;
        try
        {
            fullPath = Path.GetFullPath(Path.IsPathRooted(declaredPath)
                ? declaredPath : Path.Combine(projectDirectory, declaredPath));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or SecurityException)
        { return false; }
    }

    private static (ProjectMediaReadinessState State, string Detail) CheckPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
                return (ProjectMediaReadinessState.Unsupported, "The resolved path has no supported filesystem root.");
            var segments = path[root.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            for (var index = 0; index < segments.Length; index++)
            {
                current = Path.Combine(current, segments[index]);
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return (ProjectMediaReadinessState.Unsupported, "A path component is a link/reparse point; it was not traversed.");
                var last = index == segments.Length - 1;
                if (!last && (attributes & FileAttributes.Directory) == 0)
                    return (ProjectMediaReadinessState.Missing, "A folder in the declared path does not exist.");
                if (last && (attributes & FileAttributes.Directory) != 0)
                    return (ProjectMediaReadinessState.Unsupported, "The declared path resolves to a directory, not a media file.");
            }
            return segments.Length == 0
                ? (ProjectMediaReadinessState.Unsupported, "The declared path resolves to a filesystem root, not a media file.")
                : (ProjectMediaReadinessState.Present, "A file exists at the declared path; contents and loadability were not checked.");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return (ProjectMediaReadinessState.Missing, "No file exists at the declared path."); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        { return (ProjectMediaReadinessState.Inaccessible, "Access to the declared path was denied."); }
        catch (IOException)
        { return (ProjectMediaReadinessState.Inaccessible, "The file system could not confirm the declared path."); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        { return (ProjectMediaReadinessState.Unsupported, "The declared path is not supported by this system."); }
    }
}
