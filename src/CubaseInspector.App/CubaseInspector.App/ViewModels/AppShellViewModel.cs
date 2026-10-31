using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubaseInspector.App.Services;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using System.ComponentModel;

namespace CubaseInspector.App.ViewModels;

public sealed partial class AppShellViewModel : ObservableObject
{
    private readonly IProjectPathPicker _picker;
    private readonly ProjectSessionState _session;
    private readonly AppPreferencesService _preferences;

    [ObservableProperty] private string _currentPage = "Overview";
    [ObservableProperty] private string _status = "Read-only project inspection";
    [ObservableProperty] private bool _isBusy;

    public ProjectLibraryViewModel Library { get; }
    public ProjectWorkspaceViewModel Workspace { get; }
    public ProjectComparisonViewModel Comparison { get; }
    public PluginInventoryViewModel PluginInventory { get; }
    public ProjectToolsViewModel Tools { get; }
    public ProjectStatisticsViewModel Statistics { get; }
    public LibraryAdminViewModel LibraryAdmin { get; }
    public MediaLibraryViewModel MediaLibrary { get; }
    public LibrarySearchViewModel LibrarySearch { get; }
    public PluginCatalogViewModel PluginCatalog { get; }
    public ProjectPreflightViewModel Preflight { get; }
    public bool HasProject => _session.SelectedProject is not null;
    public bool IsAnyBusy => IsBusy || Library.IsBusy || Library.IsFiltering || PluginInventory.IsBusy || Comparison.IsBusy ||
        Tools.IsBusy || LibraryAdmin.IsBusy || PluginCatalog.IsBusy || Preflight.IsBusy || LibrarySearch.IsBusy ||
        Statistics.IsLibraryUsageBusy || MediaLibrary.IsBusy || MediaLibrary.IsMidiPreviewBusy || LibrarySearch.IsSearching ||
        LibraryAdmin.IsRecordBrowserBusy || LibraryAdmin.IsQueryBusy;
    public string BusyStatus => Library.IsBusy
        ? (string.IsNullOrWhiteSpace(Library.ProgressText) ? Library.Status : Library.ProgressText)
        : Library.IsFiltering ? "Updating project results…"
        : PluginInventory.IsBusy ? PluginInventory.Status
        : Comparison.IsBusy ? Comparison.Status
        : PluginCatalog.IsBusy ? PluginCatalog.Status
        : Preflight.IsBusy ? Preflight.Status
        : LibrarySearch.IsBusy ? LibrarySearch.Status
        : LibrarySearch.IsSearching ? LibrarySearch.Status
        : MediaLibrary.IsBusy ? MediaLibrary.Status
        : MediaLibrary.IsMidiPreviewBusy ? MediaLibrary.MidiPianoRollStatus
        : Statistics.IsLibraryUsageBusy ? Statistics.LibraryUsageStatus
        : Tools.IsBusy ? Tools.Status
        : LibraryAdmin.IsBusy ? LibraryAdmin.Status
        : LibraryAdmin.IsRecordBrowserBusy ? LibraryAdmin.RecordEditorStatus
        : LibraryAdmin.IsQueryBusy ? LibraryAdmin.DatabaseQueryStatus
        : Status;

    public AppShellViewModel(IProjectPathPicker picker, ProjectSessionState session, AppPreferencesService preferences,
        ProjectLibraryViewModel library, ProjectWorkspaceViewModel workspace,
        ProjectComparisonViewModel comparison, PluginInventoryViewModel pluginInventory,
        ProjectToolsViewModel tools, ProjectStatisticsViewModel statistics, LibraryAdminViewModel libraryAdmin,
        PluginCatalogViewModel pluginCatalog, ProjectPreflightViewModel preflight, MediaLibraryViewModel mediaLibrary,
        LibrarySearchViewModel librarySearch)
    {
        _picker = picker;
        _session = session;
        _preferences = preferences;
        Library = library;
        Workspace = workspace;
        Comparison = comparison;
        PluginInventory = pluginInventory;
        Tools = tools;
        Statistics = statistics;
        LibraryAdmin = libraryAdmin;
        MediaLibrary = mediaLibrary;
        LibrarySearch = librarySearch;
        PluginCatalog = pluginCatalog;
        Preflight = preflight;
        foreach (var component in new System.ComponentModel.INotifyPropertyChanged[]
                 { Library, PluginInventory, Comparison, Tools, LibraryAdmin, PluginCatalog, Preflight, LibrarySearch, MediaLibrary, Statistics })
            component.PropertyChanged += ComponentPropertyChanged;
        session.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(session.SelectedProject))
            {
                OnPropertyChanged(nameof(HasProject));
                if (session.SelectedProject is not null)
                {
                    NavigateTo("Overview");
                    _ = PluginInventory.EnsureInitializedAsync();
                }
            }
        };
    }

    private void ComponentPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is "IsBusy" or "IsSearching" or "IsMidiPreviewBusy" or "IsLibraryUsageBusy" or "LibraryUsageStatus" or
            "IsRecordBrowserBusy" or "IsQueryBusy" or "RecordEditorStatus" or "DatabaseQueryStatus" or
            "IsFiltering" or "Status" or "MidiPianoRollStatus" or "ProgressText")
        {
            OnPropertyChanged(nameof(IsAnyBusy));
            OnPropertyChanged(nameof(BusyStatus));
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyBusy));
        OnPropertyChanged(nameof(BusyStatus));
    }

    public void NavigateTo(string page) => CurrentPage = page;

    public string Theme => _preferences.Theme;

    public ProjectScanTypes ProjectScanTypes => _preferences.ProjectScanTypes;

    public void SetTheme(string theme) => _preferences.SetTheme(theme);

    public void SetProjectScanTypes(ProjectScanTypes types) => _preferences.SetProjectScanTypes(types);

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        IsBusy = true;
        Status = "Choose a project to open…";
        try
        {
            var path = await _picker.PickProjectAsync();
            if (path is null) return;
            await Library.OpenProjectAsync(path);
            Status = Library.Status;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ScanFolderAsync()
    {
        IsBusy = true;
        Status = "Choose a folder to scan…";
        try
        {
            var path = await _picker.PickFolderAsync();
            if (path is null) return;
            NavigateTo("Projects");
            await Library.ScanFolderAsync(path, projectScanTypes: _preferences.ProjectScanTypes);
            Status = Library.Status;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (Library.IsBusy)
        {
            Status = "Wait for the current library load or scan to finish before rescanning.";
            return;
        }

        await Library.LoadSavedProjectsAsync();
        var currentPath = Library.LastScannedFolder;
        if (string.IsNullOrWhiteSpace(currentPath) || !Directory.Exists(currentPath))
        {
            Status = string.IsNullOrWhiteSpace(currentPath)
                ? "No previous scan folder is saved. Choose a folder to scan."
                : "The previous scan folder is unavailable. Choose the folder to scan again.";
            await ScanFolderAsync();
            return;
        }
        IsBusy = true;
        try { await Library.ScanFolderAsync(currentPath, projectScanTypes: _preferences.ProjectScanTypes); Status = Library.Status; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Status = "Project folder rescan failed: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
