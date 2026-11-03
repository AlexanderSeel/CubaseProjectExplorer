using System.Text.Json;

namespace CubaseInspector.Core;

public enum BackupLinkEvidence
{
    None,
    UniqueSameFolderAndNameCandidate,
    UserConfirmed
}

public sealed record BackupProjectLink(string BackupPath, string ProjectPath,
    BackupLinkEvidence Evidence);

public sealed record BackupAssociationSettingsLoad(
    IReadOnlyDictionary<string, string> Associations,
    bool CanWrite,
    string? Warning = null);

/// <summary>Resolves conservative same-folder/name suggestions and explicit user links.</summary>
public static class BackupProjectAssociationService
{
    private const int SchemaVersion = 1;
    private const int MaximumAssociations = 10_000;
    private const int MaximumJsonLength = 2_000_000;

    private sealed record SettingsFile(int Version, AssociationFileEntry?[]? Associations);
    private sealed record AssociationFileEntry(string BackupPath, string ProjectPath);

    public static BackupProjectLink? Resolve(ProjectLibraryEntry backup,
        IEnumerable<ProjectLibraryEntry> entries, IReadOnlyDictionary<string, string>? manual = null)
    {
        ArgumentNullException.ThrowIfNull(backup);
        var backupPath = Canonical(backup.Project.FilePath);
        if (!IsBackup(backupPath)) return null;

        if (manual is not null && TryFindManual(manual, backupPath, out var target))
        {
            var exact = entries.FirstOrDefault(e => PathEquals(e.Project.FilePath, target));
            if (exact is not null && IsProject(Canonical(exact.Project.FilePath)))
                return new BackupProjectLink(backupPath, Canonical(exact.Project.FilePath),
                    BackupLinkEvidence.UserConfirmed);
            return null;
        }

        var backupDirectory = Path.GetDirectoryName(backupPath);
        var backupName = Path.GetFileNameWithoutExtension(backupPath);
        var candidates = entries.Where(e => IsProject(e.Project.FilePath) &&
            PathEquals(Path.GetDirectoryName(Canonical(e.Project.FilePath)) ?? "", backupDirectory ?? "") &&
            string.Equals(Path.GetFileNameWithoutExtension(e.Project.FilePath), backupName,
                StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return candidates.Length == 1
            ? new BackupProjectLink(backupPath, Canonical(candidates[0].Project.FilePath),
                BackupLinkEvidence.UniqueSameFolderAndNameCandidate)
            : null;
    }

    public static IReadOnlyList<BackupProjectLink> ResolveAll(
        IEnumerable<ProjectLibraryEntry> entries, IReadOnlyDictionary<string, string>? manual = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var all = entries.ToArray();
        var projectsByPath = new Dictionary<string, ProjectLibraryEntry>(StringComparer.OrdinalIgnoreCase);
        var projectsByFolderAndName = new Dictionary<string, List<ProjectLibraryEntry>>(StringComparer.OrdinalIgnoreCase);
        var manualByBackup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in all)
        {
            var path = Canonical(entry.Project.FilePath);
            if (IsProject(path)) projectsByPath.TryAdd(path, entry);
            if (!IsProject(path)) continue;
            var key = FolderAndNameKey(path);
            if (!projectsByFolderAndName.TryGetValue(key, out var matches))
                projectsByFolderAndName[key] = matches = [];
            matches.Add(entry);
        }
        if (manual is not null)
            foreach (var pair in manual)
                manualByBackup[Canonical(pair.Key)] = Canonical(pair.Value);

        var result = new List<BackupProjectLink>();
        foreach (var entry in all.Where(item => IsBackup(item.Project.FilePath)))
        {
            var backup = Canonical(entry.Project.FilePath);
            if (manualByBackup.TryGetValue(backup, out var target))
            {
                if (projectsByPath.TryGetValue(target, out var confirmed))
                    result.Add(new BackupProjectLink(backup, Canonical(confirmed.Project.FilePath),
                        BackupLinkEvidence.UserConfirmed));
                continue;
            }
            if (projectsByFolderAndName.TryGetValue(FolderAndNameKey(backup), out var candidates) &&
                candidates.Count == 1)
                result.Add(new BackupProjectLink(backup, Canonical(candidates[0].Project.FilePath),
                    BackupLinkEvidence.UniqueSameFolderAndNameCandidate));
        }
        return result;
    }

    public static BackupAssociationSettingsLoad LoadOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(new Dictionary<string, string>(), true);
        if (json.Length > MaximumJsonLength)
            return new(new Dictionary<string, string>(), false, "Association settings exceed the size limit; edits are disabled.");
        try
        {
            var file = JsonSerializer.Deserialize<SettingsFile>(json);
            if (file is null || file.Version != SchemaVersion || file.Associations is null)
                return new(new Dictionary<string, string>(), false, "Association settings have an unsupported format; edits are disabled.");
            if (file.Associations.Length > MaximumAssociations)
                return new(new Dictionary<string, string>(), false, "Association settings exceed the entry limit; edits are disabled.");
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in file.Associations)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.BackupPath) || string.IsNullOrWhiteSpace(item.ProjectPath) ||
                    item.BackupPath.Length > 4096 || item.ProjectPath.Length > 4096 ||
                    !IsBackup(item.BackupPath) || !IsProject(item.ProjectPath))
                    return new(new Dictionary<string, string>(), false, "Association settings contain invalid paths; edits are disabled.");
                var backup = Canonical(item.BackupPath);
                var project = Canonical(item.ProjectPath);
                if (!result.TryAdd(backup, project) && !PathEquals(result[backup], project))
                    return new(new Dictionary<string, string>(), false, "Association settings contain conflicting links; edits are disabled.");
            }
            return new(result, true);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or IOException)
        {
            return new(new Dictionary<string, string>(), false,
                "Association settings could not be read safely; edits are disabled.");
        }
    }

    public static void SaveFile(string path, IReadOnlyDictionary<string, string> associations,
        bool existingSettingsWritable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(associations);
        if (!existingSettingsWritable)
            throw new InvalidOperationException("Existing association settings could not be read safely; refusing to overwrite them.");
        if (associations.Count > MaximumAssociations)
            throw new InvalidOperationException("Association count exceeds the safety limit.");
        var items = associations.Select(pair =>
        {
            var backup = Canonical(pair.Key);
            var project = Canonical(pair.Value);
            if (!IsBackup(backup) || !IsProject(project))
                throw new ArgumentException("Links must pair a .bak file with a .cpr project.", nameof(associations));
            return new AssociationFileEntry(backup, project);
        }).ToArray();
        var absolute = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(absolute)!;
        Directory.CreateDirectory(folder);
        var temp = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new SettingsFile(SchemaVersion, items),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, absolute, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static IReadOnlyDictionary<string, string> SetManual(
        IReadOnlyDictionary<string, string> existing, string backupPath, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var backup = Canonical(backupPath);
        var project = Canonical(projectPath);
        if (!IsBackup(backup) || !IsProject(project))
            throw new ArgumentException("Choose a .bak backup and a .cpr project.");
        var updated = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase)
        {
            [backup] = project
        };
        if (updated.Count > MaximumAssociations)
            throw new InvalidOperationException("Association count exceeds the safety limit.");
        return updated;
    }

    public static IReadOnlyDictionary<string, string> RemoveManual(
        IReadOnlyDictionary<string, string> existing, string backupPath)
    {
        var updated = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        updated.Remove(Canonical(backupPath));
        return updated;
    }

    private static bool TryFindManual(IReadOnlyDictionary<string, string> manual,
        string backupPath, out string projectPath)
    {
        foreach (var pair in manual)
            if (PathEquals(pair.Key, backupPath))
            {
                projectPath = pair.Value;
                return true;
            }
        projectPath = "";
        return false;
    }

    private static bool IsBackup(string path) => Path.GetExtension(path).Equals(".bak", StringComparison.OrdinalIgnoreCase);
    private static bool IsProject(string path) => Path.GetExtension(path).Equals(".cpr", StringComparison.OrdinalIgnoreCase);
    private static string FolderAndNameKey(string path) =>
        Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path));
    private static string Canonical(string path) => Path.GetFullPath(path);
    private static bool PathEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
