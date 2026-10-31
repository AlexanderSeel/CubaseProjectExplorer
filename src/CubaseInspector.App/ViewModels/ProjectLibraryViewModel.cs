using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubaseInspector.App.Services;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;

namespace CubaseInspector.App.ViewModels;

public sealed class LibraryProjectRow(ProjectLibraryEntry entry)
{
    public ProjectLibraryEntry Entry => entry;
    public ProjectSnapshot Project => entry.Project;
    public string Name => entry.Project.Name;
    public string FilePath => entry.Project.FilePath;
    public string FileType => Path.GetExtension(entry.Project.FilePath).TrimStart('.').ToUpperInvariant();
    public string Version => entry.Project.CreatorVersion ?? "Unknown";
    public string Evidence => EvidencePresentation.Label(entry.Project.Evidence);
    public string Tracks => entry.Project.Tracks.Count == 0
        ? "Unknown" : entry.Project.Tracks.Count.ToString("N0") + " records";
    public int? TrackCount => entry.Project.Tracks.Count == 0 ? null : entry.Project.Tracks.Count;
    public int PluginReferenceCount => entry.Project.Plugins.Count;
    public string Plugins => entry.Project.Evidence == EvidenceLevel.Candidate
        ? "Unverified CPR markers"
        : entry.Project.Plugins.Count(p => p.Evidence == EvidenceLevel.Confirmed) + " confirmed · " +
          entry.Project.Plugins.Count(p => p.Evidence == EvidenceLevel.Candidate) + " candidate · " +
          entry.Project.Plugins.Count(p => p.Evidence == EvidenceLevel.Unknown) + " unknown";
    public string FileSize => entry.SizeBytes is long size ? FormatSize(size) : "Unknown";
    public string Modified => entry.LastModifiedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown";

    private static string FormatSize(long bytes) => bytes >= 1_073_741_824
        ? (bytes / 1_073_741_824d).ToString("0.0") + " GB"
        : bytes >= 1_048_576 ? (bytes / 1_048_576d).ToString("0.0") + " MB"
        : (bytes / 1024d).ToString("0") + " KB";
}

public sealed partial class ProjectLibraryViewModel(
    IProjectInspector inspector, IFolderScanner scanner, ProjectSessionState session,
    LibraryDocumentStore documentStore, IProjectPathPicker picker) : ObservableObject
{
    private readonly List<ProjectLibraryEntry> _allEntries = [];
    private readonly Dictionary<string, int> _entryIndexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _checksumIndexes = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _filterCancellation;
    private bool _loadedSavedProjects;

    [ObservableProperty] private bool _isFiltering;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleSummary))]
    private string _search = string.Empty;

    [ObservableProperty]
    private string _status = "Open a project or scan a folder to start building your library.";

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _lastScannedFolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleSummary))]
    private int _scanIssueCount;

    public ResettableObservableCollection<LibraryProjectRow> Projects { get; } = [];
    public ResettableObservableCollection<LibraryProjectRowGroup> ProjectGroups { get; } = [];
    public ResettableObservableCollection<object> DisplayProjects { get; } = [];
    public ObservableCollection<ScanIssueRow> ScanIssues { get; } = [];
    public IReadOnlyList<ProjectLibraryEntry> AllEntries => _allEntries;
    public string VisibleSummary => ProjectLibrarySummary.Describe(_allEntries, Projects.Count, ScanIssueCount);
    public IReadOnlyList<string> FileTypes { get; } = ["All", "CPR", "BAK", "DAWPROJECT"];
    [ObservableProperty] private string _fileType = "All";
    [ObservableProperty] private string _evidence = "All";
    [ObservableProperty] private string _versionContains = string.Empty;
    [ObservableProperty] private string _folderContains = string.Empty;
    [ObservableProperty] private bool _warningsOnly;
    [ObservableProperty] private string _sortBy = "Name";
    [ObservableProperty] private string _groupMode = "None";
    partial void OnGroupModeChanged(string value) => ScheduleProjectRefresh();

    partial void OnSearchChanged(string value) => ScheduleProjectRefresh();
    partial void OnFileTypeChanged(string value) => ScheduleProjectRefresh();
    partial void OnEvidenceChanged(string value) => ScheduleProjectRefresh();
    partial void OnVersionContainsChanged(string value) => ScheduleProjectRefresh();
    partial void OnFolderContainsChanged(string value) => ScheduleProjectRefresh();
    partial void OnWarningsOnlyChanged(bool value) => ScheduleProjectRefresh();
    partial void OnSortByChanged(string value) => ScheduleProjectRefresh();

    public async Task LoadSavedProjectsAsync()
    {
        if (_loadedSavedProjects) return;
        _loadedSavedProjects = true;
        var loadedSuccessfully = false;
        IsLoading = true;
        IsBusy = true;
        Progress = 0;
        ProgressText = "Loading indexed projects…";
        Status = ProgressText;
        try
        {
            var records = await documentStore.GetProjectsAsync();
            var entries = await Task.Run(() => records.Where(record => record.Snapshot is not null)
                .Select(record => ProjectLibraryBrowser.FromFile(record.Snapshot!,
                    record.SizeBytes, record.LastModifiedUtc, record.SourceSha256)).ToArray());
            _allEntries.Clear();
            _allEntries.AddRange(ProjectLibraryBrowser.MergeScanResults(entries, []));
            RebuildEntryIndexes();
            Progress = 70;
            ProgressText = $"Loaded {_allEntries.Count:N0} projects · loading scan folders…";
            var folders = await documentStore.GetScanFoldersAsync();
            if (folders.Count > 0) LastScannedFolder = folders[0].Path;
            await RefreshProjectsAsync();
            loadedSuccessfully = true;
            if (_allEntries.Count > 0) Status = $"Loaded {_allEntries.Count:N0} locally indexed projects.";
        }
        catch (Exception ex)
        {
            Status = "Saved library could not be loaded: " + ex.Message;
        }
        finally
        {
            if (!loadedSuccessfully) _loadedSavedProjects = false;
            IsLoading = false;
            IsBusy = false;
            Progress = 0;
            ProgressText = string.Empty;
        }
    }

    public async Task OpenProjectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        ProgressText = "Reading project file…";
        Progress = 0;
        Status = "Reading project file…";
        try
        {
            var result = await inspector.InspectAsync(path, cancellationToken);
            if (!result.IsSuccess || result.Value is null)
            {
                Status = result.Error is null ? "The project could not be read." :
                    $"{result.Error.Code}: {result.Error.Message}";
                return;
            }

            var project = result.Value;
            if (ProjectLibraryBrowser.NormalizeSha256(project.SourceSha256) is null)
            {
                ProgressText = "Calculating file checksum…";
                try { project = project with { SourceSha256 = await ProjectContentChecksum.ComputeAsync(path, cancellationToken) }; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or ArgumentException or NotSupportedException or System.Security.SecurityException)
                {
                    var warning = "Source SHA-256 could not be calculated; duplicate detection falls back to file path. " + exception.Message;
                    project = project with { Warnings = project.Warnings.Append(warning).ToArray() };
                }
            }
            AddOrReplace(project);
            Progress = 100;
            session.Select(project);
            try
            {
                ProgressText = "Saving the fresh project read to the library…";
                await documentStore.SaveProjectInspectionAsync(project, cancellationToken);
                Status = $"Opened and refreshed {project.Name}. The source project remains unchanged.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { Status = $"Opened {project.Name}, but the library refresh failed: {ex.Message}"; }
        }
        finally { IsBusy = false; ProgressText = string.Empty; Progress = 0; }
    }

    public async Task ScanFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        if (IsScanning) return;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsBusy = true;
        IsScanning = true;
        Progress = 0;
        LastScannedFolder = path;
        ScanIssues.Clear();
        Status = "Scanning project folder…";
        ProgressText = "Preparing folder scan…";
        var lastRefreshCount = 0;
        var progress = new Progress<ScanProgress>(update =>
        {
            ProgressText = update.Stage switch
            {
                ScanStage.Discovering => "Finding project files · " + update.CurrentPath,
                ScanStage.Hashing => $"Hashing {update.Completed:N0} of {update.Total:N0} · {Path.GetFileName(update.CurrentPath)}",
                _ => $"Inspecting {update.Completed:N0} of {update.Total:N0} · {Path.GetFileName(update.CurrentPath)}"
            };
            if (update.Total > 0) Progress = Math.Clamp(update.Completed * 100d / update.Total, 0, 100);
            if (update.ParsedProject is not null)
            {
                AddOrReplace(update.ParsedProject, refresh: false);
                if (_allEntries.Count - lastRefreshCount >= 20)
                {
                    lastRefreshCount = _allEntries.Count;
                    ScheduleProjectRefresh();
                }
            }
            if (update.Failure is not null)
            {
                ScanIssues.Add(new(update.CurrentPath, update.Failure.Code, update.Failure.Message));
                ScanIssueCount = ScanIssues.Count;
            }
        });

        try
        {
            var result = await scanner.ScanAsync(path, progress, _scanCancellation.Token);
            if (!result.IsSuccess || result.Value is null)
            {
                Status = result.Error?.Code == "CANCELLED" ? "Scan cancelled. Completed results are retained."
                    : result.Error?.Code + ": " + result.Error?.Message;
                return;
            }

            var mergedEntries = ProjectLibraryBrowser.MergeScanResults(_allEntries,
                result.Value.Projects.Select(project => ProjectLibraryBrowser.FromFile(project)));
            _allEntries.Clear();
            _allEntries.AddRange(mergedEntries);
            RebuildEntryIndexes();
            ScanIssues.Clear();
            foreach (var failure in result.Value.Failures)
                ScanIssues.Add(new(failure.Path, failure.Error.Split(':', 2)[0], failure.Error));
            ScanIssueCount = ScanIssues.Count;
            await RefreshProjectsAsync();
            var persistenceNote = string.Empty;
            // A user cancellation stops discovery/inspection, but completed read-only
            // results are still useful and should survive restarting the library.
            try
            {
                ProgressText = $"Saving {_allEntries.Count:N0} scan results to the library…";
                await documentStore.SaveScanAsync(path, result.Value, CancellationToken.None);
            }
            catch (Exception ex) { persistenceNote = " · local database save failed: " + ex.Message; }
            var wasCancelled = _scanCancellation!.IsCancellationRequested;
            var scanOutcome = wasCancelled ? "Scan cancelled · completed results retained"
                : result.Value.Partial ? "Scan reached a limit · partial results retained" : "Scan complete";
            Status = $"{scanOutcome} · {_allEntries.Count:N0} readable · {ScanIssues.Count:N0} issues" + persistenceNote;
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
            ProgressText = string.Empty;
            Progress = 0;
        }
    }

    [RelayCommand]
    private void CancelScan() => _scanCancellation?.Cancel();

    [RelayCommand]
    private Task ExportCsvAsync() => ExportLibraryAsync(".csv", "CSV project library", "Project-library CSV",
        rows => ProjectLibraryExportService.Csv(rows, LastScannedFolder));

    [RelayCommand]
    private Task ExportMarkdownAsync() => ExportLibraryAsync(".md", "Markdown project library", "Project-library Markdown",
        rows => ProjectLibraryExportService.Markdown(rows, LastScannedFolder));

    [RelayCommand]
    private Task ExportJsonAsync() => ExportLibraryAsync(".json", "JSON project library", "Project-library JSON",
        rows => ProjectLibraryExportService.Json(rows, LastScannedFolder));

    private async Task ExportLibraryAsync(string extension, string description, string format,
        Func<IReadOnlyList<ProjectLibraryEntry>, string> serialize)
    {
        var rows = ProjectLibraryBrowser.Filter(_allEntries, CurrentQuery()).ToArray();
        if (rows.Length == 0) { Status = "There are no visible projects to export."; return; }
        IsBusy = true;
        Status = "Choose where to save the visible-project export…";
        try
        {
            var path = await picker.PickSaveFileAsync("project-library", extension, description);
            if (path is null) return;
            Status = $"Preparing {rows.Length:N0} visible projects for export…";
            var contents = await Task.Run(() => serialize(rows));
            ProgressText = $"Writing {format}…";
            await File.WriteAllTextAsync(path, contents);
            Status = $"Exported {rows.Length:N0} visible projects as {format}. File paths are relative to the scan root.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Status = $"Could not export {format}: {ex.Message}"; }
        finally { IsBusy = false; ProgressText = string.Empty; }
    }

    public async Task SelectProjectAsync(LibraryProjectRow? row, CancellationToken cancellationToken = default)
    {
        if (row is null) return;
        await OpenProjectAsync(row.Project.FilePath, cancellationToken);
    }

    public void SelectProject(ProjectSnapshot? project)
    {
        if (project is not null) session.Select(project);
    }

    private void AddOrReplace(ProjectSnapshot project, bool refresh = true)
    {
        if (!_entryIndexes.ContainsKey(project.FilePath))
        {
            // Entries can be restored by callers that replace the backing list; keep the
            // lookup coherent before the first incremental update.
            RebuildEntryIndexes();
        }
        var entry = ProjectLibraryBrowser.FromFile(project);
        var checksum = ProjectLibraryBrowser.NormalizeSha256(entry.SourceSha256);
        if (_entryIndexes.TryGetValue(project.FilePath, out var index))
        {
            var oldEntry = _allEntries[index];
            var oldChecksum = ProjectLibraryBrowser.NormalizeSha256(oldEntry.SourceSha256);
            var duplicateIndex = checksum is not null && _checksumIndexes.TryGetValue(checksum, out var found) && found != index
                ? found : -1;
            if (duplicateIndex >= 0 && File.Exists(_allEntries[duplicateIndex].Project.FilePath))
            {
                _allEntries.RemoveAt(index);
                RebuildEntryIndexes();
            }
            else if (duplicateIndex >= 0)
            {
                _allEntries[duplicateIndex] = entry;
                _allEntries.RemoveAt(index);
                RebuildEntryIndexes();
            }
            else
            {
                _allEntries[index] = entry;
                UpdateEntryIndexes(entry, index, oldChecksum, checksum);
            }
        }
        else if (checksum is not null && _checksumIndexes.TryGetValue(checksum, out var duplicateIndex))
        {
            if (File.Exists(_allEntries[duplicateIndex].Project.FilePath)) return;
            var oldEntry = _allEntries[duplicateIndex];
            var oldChecksum = ProjectLibraryBrowser.NormalizeSha256(oldEntry.SourceSha256);
            _allEntries[duplicateIndex] = entry;
            _entryIndexes.Remove(oldEntry.Project.FilePath);
            UpdateEntryIndexes(entry, duplicateIndex, oldChecksum, checksum);
        }
        else
        {
            var newIndex = _allEntries.Count;
            _allEntries.Add(entry);
            UpdateEntryIndexes(entry, newIndex, null, checksum);
        }
        if (refresh) ScheduleProjectRefresh();
    }

    private void UpdateEntryIndexes(ProjectLibraryEntry entry, int index, string? oldChecksum, string? newChecksum)
    {
        _entryIndexes[entry.Project.FilePath] = index;
        if (oldChecksum is not null && oldChecksum != newChecksum &&
            _checksumIndexes.TryGetValue(oldChecksum, out var oldIndex) && oldIndex == index)
            _checksumIndexes.Remove(oldChecksum);
        if (newChecksum is not null) _checksumIndexes[newChecksum] = index;
    }

    private void RebuildEntryIndexes()
    {
        _entryIndexes.Clear();
        _checksumIndexes.Clear();
        for (var index = 0; index < _allEntries.Count; index++)
        {
            _entryIndexes[_allEntries[index].Project.FilePath] = index;
            if (ProjectLibraryBrowser.NormalizeSha256(_allEntries[index].SourceSha256) is { } checksum)
                _checksumIndexes.TryAdd(checksum, index);
        }
    }

    private void ScheduleProjectRefresh() => _ = RefreshProjectsAsync();

    private async Task RefreshProjectsAsync()
    {
        var next = new CancellationTokenSource();
        var previous = _filterCancellation;
        _filterCancellation = next;
        IsFiltering = true;
        previous?.Cancel();
        try
        {
            await Task.Delay(120, next.Token);
            var query = CurrentQuery();
            var entries = _allEntries.ToArray();
            var grouping = GroupMode;
            var root = LastScannedFolder;
            var projection = await Task.Run(() =>
            {
                var visible = ProjectLibraryBrowser.Filter(entries, query).ToArray();
                var rows = visible.Select(entry => new LibraryProjectRow(entry)).ToArray();
                var groups = grouping == "None" ? [] : visible.Select((entry, index) =>
                        (Key: ProjectLibraryGrouping.Key(entry, grouping, root), Row: rows[index]))
                    .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new LibraryProjectRowGroup(group.Key.Length == 0 ? "(scan root)" : group.Key,
                        group.Select(item => item.Row))).ToArray();
                return (Rows: rows, Groups: groups);
            }, next.Token);
            if (next.IsCancellationRequested) return;
            ProjectGroups.ReplaceAll(projection.Groups);
            Projects.ReplaceAll(projection.Rows);
            DisplayProjects.ReplaceAll(grouping == "None" ? projection.Rows.Cast<object>() :
                projection.Groups.SelectMany(group => new object[] { new LibraryGroupHeading(group.Display) }.Concat(group.Cast<object>())));
        OnPropertyChanged(nameof(VisibleSummary));
        OnPropertyChanged(nameof(AllEntries));
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_filterCancellation, next))
            {
                _filterCancellation = null;
                IsFiltering = false;
            }
            next.Dispose();
        }
    }

    private ProjectLibraryQuery CurrentQuery() => new(Search, FileType, Evidence, SortBy: SortBy,
        VersionContains: VersionContains, FolderContains: FolderContains, WarningsOnly: WarningsOnly);
}

public sealed record ScanIssueRow(string Path, string Code, string Message)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

public sealed class LibraryProjectRowGroup(string label, IEnumerable<LibraryProjectRow> rows) : List<LibraryProjectRow>(rows)
{
    public string Display => $"{label} · {Count:N0} projects";
}

public sealed record LibraryGroupHeading(string Label);
