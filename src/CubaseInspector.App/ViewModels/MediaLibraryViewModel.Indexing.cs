using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;

namespace CubaseInspector.App.ViewModels;

public sealed partial class MediaLibraryViewModel
{
    private const int BrowserRowLimit = 2000;
    private CancellationTokenSource? _indexCancellation;
    private CancellationTokenSource? _filterCancellation;
    private readonly Stopwatch _indexClock = new();
    private bool _hasLoadedIndex;
    private int _browserRequest;
    private int _indexRequest;
    private ProjectSnapshot? _openProjectSnapshot;
    [ObservableProperty] private bool _isIndexLoading;
    [ObservableProperty] private bool _isIndexFiltering;
    [ObservableProperty] private string _indexStage = "Saved media and MIDI index";
    [ObservableProperty] private string _indexCurrentProject = "No source files are read when loading the saved index.";
    [ObservableProperty] private string _indexOutcome = "Choose the whole library or the selected project. Rebuilding is a separate operation that can read project files.";
    [ObservableProperty] private string _indexProjectPath = "";
    [ObservableProperty] private int _indexNoteLimit = 100000;
    [ObservableProperty] private int _indexProcessed;
    [ObservableProperty] private int _indexTotal;
    [ObservableProperty] private int _indexSkipped;
    public ObservableCollection<string> IndexMessages { get; } = [];
    public string IndexElapsed => _indexClock.Elapsed.ToString(@"hh\:mm\:ss");
    public string IndexScope => string.IsNullOrWhiteSpace(IndexProjectPath) ? "Whole library" : Path.GetFileName(IndexProjectPath);
    public double IndexProgress => IndexTotal > 0 ? Math.Clamp(IndexProcessed * 100d / IndexTotal, 0, 100) : 0;
    public bool IsIndexCancelling => _indexCancellation?.IsCancellationRequested == true;

    [RelayCommand]
    public Task LoadAsync() => _hasLoadedIndex || IsBusy ? Task.CompletedTask : RunIndexOperationAsync(false);

    [RelayCommand]
    private Task ReloadIndexAsync() => IsBusy ? Task.CompletedTask : RunIndexOperationAsync(false);

    [RelayCommand]
    private Task RebuildIndexAsync() => IsBusy ? Task.CompletedTask : RunIndexOperationAsync(true);

    [RelayCommand]
    private void CancelIndexLoad()
    {
        _indexCancellation?.Cancel();
        IndexStage = "Stopping after the current saved record…";
        IndexOutcome = "Records read so far will remain available. You can reload or choose a smaller scope afterward.";
        Status = IndexStage + " " + IndexOutcome;
        OnPropertyChanged(nameof(IsIndexCancelling));
    }

    public async Task LoadIndexScopeAsync(string? projectPath)
    {
        if (IsBusy) return;
        IndexProjectPath = projectPath ?? "";
        SampleProjectPath = IndexProjectPath;
        OnPropertyChanged(nameof(IndexScope));
        await RunIndexOperationAsync(false);
    }

    public async Task RebuildIndexScopeAsync(string? projectPath)
    {
        if (IsBusy) return;
        IndexProjectPath = projectPath ?? "";
        SampleProjectPath = IndexProjectPath;
        OnPropertyChanged(nameof(IndexScope));
        await RunIndexOperationAsync(true);
    }

    public async Task LoadOpenProjectAsync(ProjectSnapshot project)
    {
        if (IsBusy) return;
        _openProjectSnapshot = project;
        IndexProjectPath = project.FilePath;
        SampleProjectPath = IndexProjectPath;
        OnPropertyChanged(nameof(IndexScope));
        await RunIndexOperationAsync(false, project);
    }

    private async Task RunIndexOperationAsync(bool rebuild, ProjectSnapshot? openProject = null)
    {
        var request = ++_indexRequest;
        IsBusy = IsIndexLoading = true;
        _indexCancellation = new CancellationTokenSource();
        var ct = _indexCancellation.Token;
        _indexClock.Restart();
        IndexProcessed = IndexTotal = IndexSkipped = 0;
        IndexMessages.Clear();
        IndexStage = rebuild ? "Preparing rebuild" : "Opening saved asset indexes";
        IndexCurrentProject = openProject?.FilePath ?? store.DatabasePath;
        IndexOutcome = rebuild ? "Rebuild reads saved projects and reopens source files for compact records. Cancel keeps indexes already saved."
            : "Loading saved database records only. Cancel to browse the records read so far.";
        RecordIndexMessage($"Starting {(rebuild ? "source rebuild" : openProject is not null ? "open-project inspection" : "saved-index load")} · {IndexScope} · native-note limit {IndexNoteLimit:N0}");
        var accumulator = new IndexAccumulator(Math.Clamp(IndexNoteLimit, 1000, 250000));
        IProgress<LibraryAssetIndexProgress> progress = new Progress<LibraryAssetIndexProgress>(update =>
        {
            if (ct.IsCancellationRequested || !IsIndexLoading || request != _indexRequest) return;
            IndexStage = update.Stage;
            IndexProcessed = update.Processed;
            IndexTotal = update.Total;
            IndexSkipped = Math.Max(IndexSkipped, update.Skipped);
            IndexCurrentProject = update.CurrentProject ?? store.DatabasePath;
            if (update.Detail is { Length: > 0 } detail)
                RecordIndexMessage(update.Stage + " · " + IndexCurrentProject + " · " + detail);
            Status = $"{IndexStage} · {IndexProcessed:N0}/{IndexTotal:N0} projects · {IndexElapsed}";
            IndexOutcome = $"{accumulator.MediaCount:N0} audio references · {accumulator.MidiCount:N0} MIDI tracks · {accumulator.NotesSeen:N0} notes read · {accumulator.NotesLoaded:N0} notes retained · {IndexSkipped:N0} skipped";
            OnPropertyChanged(nameof(IndexProgress));
        });
        var cancelled = false;
        string? failure = null;
        try
        {
            if (openProject is not null)
            {
                progress.Report(new("Preparing the open project's current audio/MIDI data", 0, 1, openProject.FilePath));
                await Task.Run(() => accumulator.Add(new LibraryProjectAssetIndexDocument
                {
                    Id = "open-project:" + openProject.FilePath, ProjectPath = openProject.FilePath,
                    Index = ProjectAssetIndexer.Build(openProject)
                }, ct), ct);
                progress.Report(new("Open project prepared", 1, 1, openProject.FilePath));
            }
            else
            {
                if (rebuild) await store.RebuildProjectAssetIndexesAsync(ct, progress, IndexProjectPath);
                IndexStage = "Reading saved asset indexes";
                await store.VisitProjectAssetIndexesAsync(document => accumulator.Add(document, ct), progress, IndexProjectPath, ct);
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.Data.Common.DbException or JsonException)
        { failure = ex.Message; }
        try
        {
            IndexStage = "Preparing browser results";
            var contents = await Task.Run(accumulator.Build);
            if (accumulator.Projects > 0 || failure is null && !cancelled)
            {
                _allSamples = contents.Samples;
                _allNativeSampleCandidates = contents.NativeSamples;
                _allMidiTracks = contents.MidiTracks;
                _allMidiCandidates = contents.Notes;
                SampleTypes = contents.Types;
                SampleProjects = contents.Projects;
                OnPropertyChanged(nameof(SampleTypes));
                OnPropertyChanged(nameof(SampleProjects));
                _hasLoadedIndex = true;
                await RefreshBrowsersAsync(CancellationToken.None);
            }
            cancelled |= ct.IsCancellationRequested;
            IndexStage = failure is not null ? "Load interrupted" : cancelled ? "Cancelled · partial results available" : "Index ready";
            IndexOutcome = $"{accumulator.Projects:N0} projects read · {accumulator.MediaCount:N0} audio references · {accumulator.MidiCount:N0} MIDI tracks · {accumulator.NotesLoaded:N0}/{accumulator.NotesSeen:N0} notes retained · {IndexElapsed}. " +
                (accumulator.IsLimited ? "The loading limit was reached. Choose one project or increase Notes to load, then reload. " : "") +
                (failure is not null ? failure + " Previous or partial results are available; reload to retry." :
                    accumulator.Projects == 0 && !cancelled ? "No saved indexes match this scope. Scan projects or explicitly rebuild their indexes." : "");
            Status = IndexStage + " · " + IndexOutcome;
            RecordIndexMessage(Status);
            // A cancelled load is an explicit user choice; navigation must not silently restart it.
            if (cancelled) _hasLoadedIndex = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
        {
            IndexStage = "Could not prepare browser results";
            IndexOutcome = ex.Message + " Choose a project scope or retry the load. Previous results remain available.";
            Status = IndexStage + " · " + IndexOutcome;
            RecordIndexMessage(Status);
        }
        finally
        {
            _indexClock.Stop();
            IsBusy = IsIndexLoading = false;
            _indexCancellation?.Dispose();
            _indexCancellation = null;
            OnPropertyChanged(nameof(IsIndexCancelling));
        }
    }

    private Task<LibraryProjectDocument?> GetIndexedProjectAsync(string projectId) =>
        _openProjectSnapshot is { } project && projectId == "open-project:" + project.FilePath
            ? Task.FromResult<LibraryProjectDocument?>(new() { Id = projectId, FilePath = project.FilePath, Snapshot = project })
            : store.GetProjectAsync(projectId);

    private void RecordIndexMessage(string message)
    {
        while (IndexMessages.Count >= 100) IndexMessages.RemoveAt(0);
        IndexMessages.Add(DateTime.Now.ToString("HH:mm:ss") + " · " + message);
    }

    private void QueueBrowserRefresh()
    {
        _filterCancellation?.Cancel();
        _filterCancellation?.Dispose();
        _filterCancellation = new CancellationTokenSource();
        _ = RefreshAfterTypingAsync(_filterCancellation.Token);
    }

    private async Task RefreshAfterTypingAsync(CancellationToken ct)
    {
        try { await Task.Delay(180, ct); await RefreshBrowsersAsync(ct); }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshBrowsersAsync(CancellationToken ct)
    {
        var request = ++_browserRequest;
        var samples = _allSamples; var nativeSamples = _allNativeSampleCandidates;
        var tracks = _allMidiTracks; var notes = _allMidiCandidates;
        var query = Search.Trim(); var midiQuery = MidiSearch.Trim(); var type = SampleType; var scope = SampleProjectPath;
        IsIndexFiltering = true;
        try
        {
            var result = await Task.Run(() =>
            {
                bool Match(string value, string search) => search.Length == 0 || value.Contains(search, StringComparison.OrdinalIgnoreCase);
                bool Scoped(string path) => scope.Length == 0 || path.Equals(scope, StringComparison.OrdinalIgnoreCase);
                var audioRows = samples.Where(row => Scoped(row.ProjectPath) && (type == "All" || row.FileType.Equals(type, StringComparison.OrdinalIgnoreCase)) &&
                    Match(row.DeclaredPath + " " + row.Folder + " " + row.ProjectName + " " + row.TrackName + " " + row.Source, query))
                    .OrderBy(row => row.DeclaredPath, StringComparer.OrdinalIgnoreCase).Take(BrowserRowLimit).ToArray();
                ct.ThrowIfCancellationRequested();
                var nativeRows = nativeSamples.Where(row => Scoped(row.ProjectPath) && (type == "All" || row.FileType.Equals(type, StringComparison.OrdinalIgnoreCase)) &&
                    Match(row.DeclaredPath + " " + row.ProjectName + " " + row.TrackName, query)).Take(BrowserRowLimit).ToArray();
                var midiRows = tracks.Where(row => Match(row.Name + " " + row.ProjectName + " " + row.ProjectPath + " " + row.Kind, midiQuery))
                    .OrderBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Take(BrowserRowLimit).ToArray();
                var noteRows = notes.Where(row => Match(row.ProjectName + " " + row.ProjectPath + " " + row.CandidateTrackName + " " +
                    row.CandidatePartName + " " + row.SourceLayout + " " + row.Pitch, midiQuery)).ToArray();
                ct.ThrowIfCancellationRequested();
                var groups = NativeMidiBrowser.Group(noteRows).Take(BrowserRowLimit).ToArray();
                return (audioRows, nativeRows, midiRows, noteRows, groups);
            }, ct);
            ct.ThrowIfCancellationRequested();
            if (request != _browserRequest) return;
            Samples.ReplaceAll(result.audioRows);
            NativeSampleCandidates.ReplaceAll(result.nativeRows);
            MidiTracks.ReplaceAll(result.midiRows);
            NativeMidiCandidates.ReplaceAll(result.noteRows.Take(BrowserRowLimit));
            NativeMidiGroups.ReplaceAll(result.groups);
            OnPropertyChanged(nameof(SampleSummary));
            OnPropertyChanged(nameof(MidiSummary));
        }
        finally { if (request == _browserRequest) IsIndexFiltering = false; }
    }

    private sealed record IndexContents(SampleUseRow[] Samples, NativeSampleCandidateRow[] NativeSamples,
        MidiTrackLibraryRow[] MidiTracks, IndexedMidiNoteCandidate[] Notes, string[] Types, SampleProjectScopeOption[] Projects);

    private sealed class IndexAccumulator(int noteLimit)
    {
        private const int RowLimit = 50000;
        private readonly List<SampleUseRow> _samples = [];
        private readonly List<NativeSampleCandidateRow> _nativeSamples = [];
        private readonly List<MidiTrackLibraryRow> _tracks = [];
        private readonly List<IndexedMidiNoteCandidate> _notes = [];
        public int Projects { get; private set; }
        public long MediaCount { get; private set; }
        public long MidiCount { get; private set; }
        public long NotesSeen { get; private set; }
        public int NotesLoaded => _notes.Count;
        public bool IsLimited => _notes.Count >= noteLimit && NotesSeen > _notes.Count ||
            (_samples.Count >= RowLimit || _nativeSamples.Count >= RowLimit) && MediaCount > _samples.Count + _nativeSamples.Count ||
            _tracks.Count >= RowLimit && MidiCount > _tracks.Count;

        public void Add(LibraryProjectAssetIndexDocument document, CancellationToken ct)
        {
            Projects++;
            if (document.Index is not { } index) return;
            var projectPath = document.ProjectPath ?? index.ProjectPath ?? "";
            var projectName = index.ProjectName ?? Path.GetFileNameWithoutExtension(projectPath);
            if (index.Evidence == EvidenceLevel.Confirmed)
            {
                foreach (var media in index.MediaUses ?? [])
                {
                    ct.ThrowIfCancellationRequested(); MediaCount++;
                    if (media is null || string.IsNullOrWhiteSpace(media.DeclaredPath)) continue;
                    if (_samples.Count < RowLimit) _samples.Add(new(document.Id, projectName, projectPath, media.ReferenceIndex,
                        media.DeclaredPath, ResolveFolder(projectPath, media), media.FileType ?? "Unknown",
                        media.External switch { true => "External reference", false => "Embedded archive entry", _ => "Location unknown" },
                        media.TrackName ?? "Track unknown", media.Channels, media.SampleRate, media.Duration));
                }
                foreach (var track in index.MidiTracks ?? [])
                {
                    ct.ThrowIfCancellationRequested(); MidiCount++;
                    if (track is null) continue;
                    if (_tracks.Count < RowLimit) _tracks.Add(new(document.Id, projectName, projectPath, track.TrackId ?? "", track.Name ?? "Unnamed track", track.Kind ?? "MIDI",
                        track.NoteCount, track.LowestKey is int low && track.HighestKey is int high ? $"{low}–{high}" : "Unknown",
                        track.ChannelCount, track.TimeUnit ?? "Unknown", track.CanExportStandardMidi, track.ExportLimitation));
                }
            }
            foreach (var note in index.NativeMidiNoteCandidates ?? [])
            {
                ct.ThrowIfCancellationRequested(); NotesSeen++;
                if (note?.Evidence == EvidenceLevel.Candidate && _notes.Count < noteLimit) _notes.Add(note with
                    { ProjectPath = note.ProjectPath ?? projectPath, ProjectName = note.ProjectName ?? projectName });
            }
            foreach (var media in index.NativeMediaReferenceCandidates ?? [])
            {
                ct.ThrowIfCancellationRequested(); MediaCount++;
                if (media is null || string.IsNullOrWhiteSpace(media.DeclaredPath)) continue;
                if (_nativeSamples.Count < RowLimit) _nativeSamples.Add(new(document.Id, media.ProjectName ?? projectName, media.ProjectPath ?? projectPath, media.Observation,
                    media.DeclaredPath, media.CandidateTrackName, media.CandidateTrackOffset, media.FileOffset, media.Evidence, media.EvidenceSource, media.Warning));
            }
        }

        public IndexContents Build()
        {
            var useCounts = _samples.GroupBy(row => row.ProjectPath + "\0" + row.DeclaredPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var samples = _samples.Select(row => row with { UseCount = useCounts[row.ProjectPath + "\0" + row.DeclaredPath] }).ToArray();
            var types = new[] { "All" }.Concat(samples.Select(row => row.FileType).Concat(_nativeSamples.Select(row => row.FileType))
                .Where(type => type.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(type => type, StringComparer.OrdinalIgnoreCase)).ToArray();
            var projects = samples.Select(row => (row.ProjectPath, row.ProjectName)).Concat(_nativeSamples.Select(row => (row.ProjectPath, row.ProjectName)))
                .Concat(_tracks.Select(row => (row.ProjectPath, row.ProjectName))).Concat(_notes.Select(row => (row.ProjectPath, row.ProjectName)))
                .GroupBy(row => row.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => new SampleProjectScopeOption(group.Key, group.First().ProjectName + " · " + Path.GetFileName(group.Key)))
                .OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
            return new(samples, _nativeSamples.ToArray(), _tracks.ToArray(), _notes.ToArray(), types, projects);
        }
    }
}
