using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CubaseInspector.App.Services;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;

namespace CubaseInspector.App.ViewModels;

public sealed record StatisticRow(string Name, int Count, string Evidence);

public sealed partial class ProjectStatisticsViewModel : ObservableObject
{
    private readonly ProjectSessionState _session;
    private readonly LibraryDocumentStore _store;
    private readonly ProjectStatisticsService _statistics = new();
    public ObservableCollection<StatisticRow> Tracks { get; } = [];
    public ObservableCollection<StatisticRow> Plugins { get; } = [];
    public ResettableObservableCollection<LibraryPluginUsageRow> LibraryPluginUsage { get; } = [];
    public ResettableObservableCollection<LibraryCandidateMarkerRow> LibraryCandidateMarkers { get; } = [];
    [ObservableProperty] private string _summary = "Select a project to see supported statistics.";
    [ObservableProperty] private string _libraryUsageStatus = "Refresh to rank confirmed references in stored project snapshots.";
    [ObservableProperty] private bool _isLibraryUsageBusy;

    public ProjectStatisticsViewModel(ProjectSessionState session, LibraryDocumentStore store)
    {
        _session = session;
        _store = store;
        session.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(session.SelectedProject)) Refresh();
        };
        Refresh();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public async Task LoadLibraryUsageAsync()
    {
        if (IsLibraryUsageBusy) return;
        IsLibraryUsageBusy = true;
        LibraryUsageStatus = "Streaming stored plug-in references…";
        try
        {
            var index = await _store.GetLibraryPluginUsageIndexAsync();
            LibraryPluginUsage.ReplaceAll(index.ConfirmedReferences);
            LibraryCandidateMarkers.ReplaceAll(index.CandidateMarkers);
            LibraryUsageStatus = index.StoredProjectCount == 0
                ? "No saved project snapshots are available. Scan a folder or inspect a project first."
                : $"{index.ConfirmedProjectCount:N0} confirmed projects among {index.StoredProjectCount:N0} saved snapshots · {index.CandidateReferenceCount:N0} candidate references kept separate ({index.CandidateWithoutTokenCount:N0} without a token) · {index.UnknownReferenceCount:N0} unknown-evidence references excluded. Ranked by exact displayed name, format and vendor fields; export-local IDs are not treated as cross-project identities or last-use times.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        { LibraryUsageStatus = "Could not load library-wide plug-in references: " + ex.Message; }
        finally { IsLibraryUsageBusy = false; }
    }

    private void Refresh()
    {
        Tracks.Clear(); Plugins.Clear();
        if (_session.SelectedProject is not { } project)
        {
            Summary = "Select a project to see supported statistics.";
            return;
        }
        var result = _statistics.Summarize(project);
        foreach (var row in result.TrackTypes.Select(pair => new StatisticRow(pair.Key, pair.Value,
                     project.Evidence == EvidenceLevel.Candidate ? "Recognized serialized class" : "Read from project export"))) Tracks.Add(row);
        foreach (var row in result.PluginFormats.Select(pair => new StatisticRow(pair.Key, pair.Value,
                     "Confirmed exported references"))) Plugins.Add(row);
        Summary = result.CandidatePluginReferences == 0
            ? $"{project.Tracks.Count:N0} track records · {project.Plugins.Count:N0} confirmed plugin references"
            : $"{project.Tracks.Count:N0} recognized serialized track classes · {result.CandidatePluginReferences:N0} stored plug-in markers; format and identity charts require more evidence";
    }
}
