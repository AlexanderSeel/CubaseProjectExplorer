using CommunityToolkit.Mvvm.ComponentModel;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;

namespace CubaseInspector.App.ViewModels;

public sealed record LibrarySearchRow(LibrarySearchDocument Document)
{
    public string Category => Document.Category;
    public string Name => Document.Title;
    public string Details => Document.Detail;
    public string Project => Document.ProjectName;
    public string Location => Document.Category == "Audio" ? Document.Target ?? Document.ProjectPath : Document.ProjectPath;
    public string Evidence => Document.Evidence;
}

public sealed partial class LibrarySearchViewModel(LibraryDocumentStore store) : ObservableObject
{
    private LibrarySearchDocument[] _index = [];
    private CancellationTokenSource? _searchCancellation;
    private bool _loaded;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _scope = "All";
    [ObservableProperty] private string _status = "Search the saved project, audio, MIDI, and plug-in indexes.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private int _totalMatches;

    public ResettableObservableCollection<LibrarySearchRow> Results { get; } = [];
    public IReadOnlyList<string> Scopes { get; } = ["All", "Project", "Audio", "MIDI", "Plug-in"];

    partial void OnQueryChanged(string value) => ScheduleSearch();
    partial void OnScopeChanged(string value) => ScheduleSearch();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded || IsBusy) return;
        IsBusy = true;
        Status = "Loading saved search indexes…";
        await Task.Yield();
        try
        {
            var projectsTask = store.GetProjectLibraryEntriesAsync(cancellationToken);
            var assetsTask = store.GetProjectAssetIndexesAsync(cancellationToken);
            await Task.WhenAll(projectsTask, assetsTask);
            var projects = await projectsTask;
            var assets = await assetsTask;
            _index = await Task.Run(() => BuildIndex(projects, assets), cancellationToken);
            _loaded = true;
            Status = $"Ready · {_index.Length:N0} searchable records from {projects.Count:N0} saved projects.";
            ScheduleSearch();
        }
        catch (OperationCanceledException) { Status = "Search index loading canceled."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        { Status = "Could not load the saved search index: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private static LibrarySearchDocument[] BuildIndex(IReadOnlyList<ProjectLibraryEntry> projects,
        IReadOnlyList<LibraryProjectAssetIndexDocument> assets)
    {
        var documents = new List<LibrarySearchDocument>();
        foreach (var entry in projects)
        {
            var project = entry.Project;
            var evidence = EvidencePresentation.Label(project.Evidence);
            documents.Add(new LibrarySearchDocument("Project", project.Name,
                $"{project.Format} · {project.CreatorVersion ?? "Version unknown"}", project.Name,
                project.FilePath, evidence,
                $"{project.Format} {project.CreatorVersion} {project.Bpm} {string.Join(' ', entry.SearchableTrackNames ?? [])}", project));

            foreach (var plugin in entry.SearchablePlugins ?? [])
            {
                documents.Add(new LibrarySearchDocument("Plug-in", plugin.Name,
                    string.IsNullOrWhiteSpace(plugin.Detail)
                        ? "Plug-in reference · see project details"
                        : plugin.Detail + " · inspect project for full details", project.Name,
                    project.FilePath, EvidencePresentation.Label(plugin.Evidence),
                    plugin.Name + " " + plugin.SearchTerms, project));
            }
        }

        foreach (var saved in assets)
        {
            var index = saved.Index;
            if (index is null || index.Evidence != EvidenceLevel.Confirmed) continue;
            foreach (var media in index.MediaUses)
            {
                var name = Path.GetFileName(media.DeclaredPath);
                if (string.IsNullOrWhiteSpace(name)) name = media.DeclaredPath;
                documents.Add(new LibrarySearchDocument("Audio", name,
                    $"{media.FileType} · {media.TrackName ?? "Track unknown"} · {(media.External switch { true => "External", false => "Embedded", _ => "Location unknown" })}",
                    index.ProjectName, index.ProjectPath, "Confirmed export", media.DeclaredPath,
                    Target: media.DeclaredPath));
            }
            foreach (var track in index.MidiTracks)
            {
                var pitch = track.LowestKey is int low && track.HighestKey is int high ? $"keys {low}–{high}" : "pitch unknown";
                documents.Add(new LibrarySearchDocument("MIDI", track.Name,
                    $"{track.Kind} · {track.NoteCount:N0} notes · {pitch}", index.ProjectName,
                    index.ProjectPath, "Confirmed export", $"{track.TrackId} {track.TimeUnit} {track.ExportLimitation}",
                    Target: track.TrackId));
            }
            foreach (var note in index.NativeMidiNoteCandidates ?? [])
            {
                documents.Add(new LibrarySearchDocument("Native MIDI notes", $"Note {note.Observation}",
                    $"Pitch {note.Pitch} · position {note.PositionTicks:0.##} ticks · {note.Warning}",
                    note.ProjectName, note.ProjectPath, "Needs review", $"{note.FileOffset:X} {note.Velocity}",
                    Target: note.FileOffset.ToString("X")));
            }
        }
        return documents.ToArray();
    }

    private void ScheduleSearch()
    {
        if (!_loaded) return;
        var current = new CancellationTokenSource();
        var previous = _searchCancellation;
        _searchCancellation = current;
        previous?.Cancel();
        _ = SearchAsync(current);
    }

    private async Task SearchAsync(CancellationTokenSource current)
    {
        try
        {
            await Task.Delay(120, current.Token);
            if (!string.IsNullOrWhiteSpace(Query))
                Status = "Searching indexed projects, audio, MIDI and plug-ins…";
            IsSearching = true;
            var response = await Task.Run(() => LibrarySearchEngine.Search(_index, Query, Scope), current.Token);
            if (current.IsCancellationRequested) return;
            Results.ReplaceAll(response.Results.Select(document => new LibrarySearchRow(document)));
            TotalMatches = response.MatchCount;
            Status = string.IsNullOrWhiteSpace(Query)
                ? $"Ready · {_index.Length:N0} searchable records. Enter a name, file, track, or plug-in."
                : response.Limited
                    ? $"Showing the first {response.Results.Count:N0} of {response.MatchCount:N0} matches. Add a word or choose a category to narrow the results."
                    : $"{response.MatchCount:N0} matches · search covers saved projects, referenced audio, MIDI tracks, and plug-in references.";
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_searchCancellation, current)) _searchCancellation = null;
            if (_searchCancellation is null) IsSearching = false;
            current.Dispose();
        }
    }
}
