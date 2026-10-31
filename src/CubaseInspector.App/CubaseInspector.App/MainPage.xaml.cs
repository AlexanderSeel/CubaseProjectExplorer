using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using CubaseInspector.App.ViewModels;
using CubaseInspector.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Automation;
using Windows.Foundation;
using Windows.UI;

namespace CubaseInspector.App;

public sealed partial class MainPage : Page
{
    private AppShellViewModel? _shell;
    private ProjectLibraryViewModel? _progressLibrary;
    private Button? _analyzeButton;
    private Button? _scanButton;
    private PluginInventoryViewModel? _progressInventory;
    private readonly NavigationHistory _history = new("Overview");
    private bool _startupHydrating;
    private bool _rendering;

    public MainPage()
    {
        InitializeComponent();
        _analyzeButton = AnalyzeProjectButton;
        _scanButton = ScanFolderButton;
        RequestedTheme = ElementTheme.Dark;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _shell = e.Parameter as AppShellViewModel;
        if (_shell is null) return;
        _shell.PropertyChanged += ShellPropertyChanged;
        if (_progressLibrary is not null) _progressLibrary.PropertyChanged -= LibraryProgressChanged;
        _progressLibrary = _shell.Library;
        _progressLibrary.PropertyChanged += LibraryProgressChanged;
        if (_progressInventory is not null) _progressInventory.PropertyChanged -= InventoryProgressChanged;
        _progressInventory = _shell.PluginInventory;
        _progressInventory.PropertyChanged += InventoryProgressChanged;
        _startupHydrating = true;
        _progressLibrary.Status = "Loading indexed projects…";
        UpdateScanProgress();
        _shell.SetTheme(_shell.Theme);
        RequestedTheme = _shell.Theme switch { "Light" => ElementTheme.Light, "System" => ElementTheme.Default, _ => ElementTheme.Dark };
        StatusText.Text = _progressLibrary.Status;
        Render(_shell.CurrentPage, recordHistory: false);
        _ = LoadStartupDataAsync(_shell, _progressLibrary);
    }

    private async Task LoadStartupDataAsync(AppShellViewModel shell, ProjectLibraryViewModel library)
    {
        // Let the first dashboard frame render before hydrating the local database.
        await Task.Delay(60);
        try
        {
            await library.LoadSavedProjectsAsync(shell.ProjectScanTypes);
            await shell.LibraryAdmin.RefreshCommand.ExecuteAsync(null);
            StatusText.Text = library.Status;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            library.Status = "The local database could not be fully loaded: " + ex.Message;
            StatusText.Text = library.Status;
        }
        finally
        {
            _startupHydrating = false;
            UpdateBusyIndicator();
        }
    }

    private async void AnalyzeProject_Click(object sender, RoutedEventArgs e)
    {
        if (_shell is null) return;
        await _shell.AnalyzeProjectCommand.ExecuteAsync(null);
        StatusText.Text = _shell.Status;
        if (_shell.HasProject) Render(_shell.CurrentPage);
    }

    private async void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_shell is null) return;
        await _shell.ScanFolderCommand.ExecuteAsync(null);
        StatusText.Text = _shell.Library.Status;
        Render("Projects");
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e) => _shell?.Library.CancelScanCommand.Execute(null);

    private void ActivityCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_shell?.MediaLibrary.IsIndexLoading == true)
            _shell.MediaLibrary.CancelIndexLoadCommand.Execute(null);
        else if (_shell?.Library.IsScanning == true)
            _shell.Library.CancelScanCommand.Execute(null);
    }

    private void Evidence_Click(object sender, RoutedEventArgs e) => Render("Help");

    private void LibraryProgressChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProjectLibraryViewModel.IsScanning) or
            nameof(ProjectLibraryViewModel.IsBusy) or nameof(ProjectLibraryViewModel.Progress) or
            nameof(ProjectLibraryViewModel.ProgressText) or nameof(ProjectLibraryViewModel.Status))
        {
            UpdateScanProgress();
            if (_progressLibrary is not null && !_progressLibrary.IsScanning)
                StatusText.Text = _progressLibrary.Status;
        }
    }

    private void UpdateScanProgress()
    {
        if (_shell is null) return;
        var library = _shell.Library;
        ScanProgressPanel.Visibility = library.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        ScanProgressLabel.Text = string.IsNullOrWhiteSpace(library.ProgressText) ? library.Status : library.ProgressText;
        ToolTipService.SetToolTip(ScanProgressLabel, ScanProgressLabel.Text);
        ScanProgressBar.Value = library.Progress;
        ScanProgressBar.IsIndeterminate = library.IsBusy && library.Progress <= 0;
        ScanProgressPercent.Text = library.IsBusy && library.Progress > 0
            ? $"{library.Progress:0}%" : string.Empty;
        CancelScanButton.Visibility = library.IsScanning ? Visibility.Visible : Visibility.Collapsed;
        if (_analyzeButton is not null) _analyzeButton.IsEnabled = _shell is { IsAnyBusy: false };
        if (_scanButton is not null) _scanButton.IsEnabled = !library.IsBusy;
    }

    private void InventoryProgressChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_progressInventory is not null && args.PropertyName is nameof(PluginInventoryViewModel.Status) or nameof(PluginInventoryViewModel.IsBusy))
            StatusText.Text = _progressInventory.Status;
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_rendering && args.SelectedItem is NavigationViewItem item && item.Tag is string page) Render(page);
    }

    private void ShellPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_shell is null) return;
        if (args.PropertyName == nameof(AppShellViewModel.CurrentPage) && !_rendering &&
            !string.Equals(_history.Current, _shell.CurrentPage, StringComparison.Ordinal))
            Render(_shell.CurrentPage);
        if (args.PropertyName is nameof(AppShellViewModel.IsAnyBusy) or nameof(AppShellViewModel.BusyStatus))
            UpdateBusyIndicator();
    }

    private void Render(string page, bool recordHistory = true)
    {
        if (_shell is null) return;
        if (recordHistory) _history.Navigate(page);
        _rendering = true;
        try
        {
            _shell.NavigateTo(page);
            Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, page, StringComparison.Ordinal));
            PageHost.Children.Clear();
            PageHost.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new WorkspacePageFactory(_shell, targetPage => Render(targetPage),
                    (DataTemplate)Resources["ProjectChangeTemplate"],
                    (DataTemplate)Resources["ScanIssueTemplate"],
                    theme => { RequestedTheme = theme; _shell.SetTheme(theme switch { ElementTheme.Light => "Light", ElementTheme.Default => "System", _ => "Dark" }); },
                    _shell.MediaLibrary, XamlRoot!).Create(page) });
        }
        finally { _rendering = false; }
        UpdateNavigationControls();
        UpdateBusyIndicator();
        StatusText.Text = page == "Projects" ? _shell.Library.Status
            : _shell.HasProject ? $"{_shell.Workspace.ProjectName} · {_shell.Workspace.TrackCount} track records · {_shell.Workspace.SavedBy}" : "No project selected";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Back()) Render(_history.Current, recordHistory: false);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Forward()) Render(_history.Current, recordHistory: false);
    }

    private void UpdateNavigationControls()
    {
        BackButton.IsEnabled = _history.CanGoBack;
        ForwardButton.IsEnabled = _history.CanGoForward;
        BreadcrumbHost.Children.Clear();
        var trail = _history.Trail;
        if (trail.Count > 1)
        {
            var previousIndex = trail.Count - 2;
            var previous = new Button { Content = PageLabel(trail[previousIndex]), Padding = new Thickness(6, 2, 6, 2), MinHeight = 26, Tag = previousIndex };
            previous.Click += Breadcrumb_Click;
            AutomationProperties.SetName(previous, "Go to previous page " + PageLabel(trail[previousIndex]));
            BreadcrumbHost.Children.Add(previous);
            BreadcrumbHost.Children.Add(new TextBlock { Text = "›", Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center });
        }
        BreadcrumbHost.Children.Add(new TextBlock { Text = PageLabel(_history.Current), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
    }

    private void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index } && _history.JumpTo(index))
            Render(_history.Current, recordHistory: false);
    }

    private static string PageLabel(string page) => page switch
    {
        "Projects" => "Project Library", "Search" => "Search everything", "Calendar" => "Project Calendar", "IO" => "Audio / MIDI I/O",
        "Inventory" => "Local plug-in inventory", "Catalog" => "Plug-in catalog", "Preflight" => "Dependency preflight",
        "Overview" => "Studio overview", "Tools" => "Reports & tools", "Database" => "Library database", "Compare" => "Compare projects",
        "Samples" => "Audio", "Midi" => "MIDI tracks", "Help" => "Help & reader FAQ",
        _ => page
    };

    private void UpdateBusyIndicator()
    {
        if (_shell is null) return;
        var busy = _shell.IsAnyBusy;
        if (_analyzeButton is not null) _analyzeButton.IsEnabled = !busy;
        BusyIndicatorPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusySpinner.IsActive = busy;
        BusyText.Text = busy ? ActivityStatus(_shell) : string.Empty;
        var activityDetails = BusyText.Text;
        if (_shell.MediaLibrary.IsIndexLoading)
            activityDetails += $"\n{_shell.MediaLibrary.IndexCurrentProject}\n{_shell.MediaLibrary.IndexOutcome}";
        ToolTipService.SetToolTip(BusyText, activityDetails);
        var scan = !_startupHydrating && _shell.Library.IsScanning;
        var analysis = _shell.MediaLibrary.IsIndexLoading || _shell.MediaLibrary.IsIndexFiltering;
        ActivityProgressBar.Visibility = scan || analysis ? Visibility.Visible : Visibility.Collapsed;
        ActivityProgressBar.IsIndeterminate = scan ? _shell.Library.Progress <= 0
            : _shell.MediaLibrary.IsIndexFiltering || analysis && _shell.MediaLibrary.IndexTotal <= 0;
        ActivityProgressBar.Value = scan ? _shell.Library.Progress : _shell.MediaLibrary.IndexProgress;
        ActivityPercentText.Text = scan && _shell.Library.Progress > 0
            ? $"{_shell.Library.Progress:0}%"
            : _shell.MediaLibrary.IsIndexLoading && _shell.MediaLibrary.IndexTotal > 0
                ? $"{_shell.MediaLibrary.IndexProgress:0}%" : string.Empty;
        var canCancelAnalysis = analysis && !_shell.MediaLibrary.IsIndexCancelling;
        ActivityCancelButton.Visibility = canCancelAnalysis || scan ? Visibility.Visible : Visibility.Collapsed;
        ActivityCancelButton.IsEnabled = canCancelAnalysis || scan;
        ActivityCancelButton.Content = _shell.MediaLibrary.IsIndexCancelling ? "Stopping…" : "Cancel";
    }

    private static string ActivityStatus(AppShellViewModel shell)
    {
        if (shell.MediaLibrary.IsIndexLoading)
            return shell.MediaLibrary.IndexTotal > 0
                ? $"{FriendlyAnalysisStage(shell.MediaLibrary.IndexStage)} · {shell.MediaLibrary.IndexProcessed:N0} of {shell.MediaLibrary.IndexTotal:N0} · {shell.MediaLibrary.IndexElapsed}"
                : $"{FriendlyAnalysisStage(shell.MediaLibrary.IndexStage)} · {shell.MediaLibrary.IndexElapsed}";
        if (shell.MediaLibrary.IsIndexFiltering) return "Organizing audio and MIDI results…";
        if (shell.Library.IsScanning)
            return string.IsNullOrWhiteSpace(shell.Library.ProgressText) ? "Scanning projects…" : shell.Library.ProgressText;
        if (shell.MediaLibrary.IsMidiPreviewBusy) return "Preparing MIDI preview…";
        return shell.BusyStatus;
    }

    private static string FriendlyAnalysisStage(string stage) => stage switch
    {
        "Opening saved asset indexes" => "Loading saved audio and MIDI…",
        "Reading saved asset indexes" => "Loading audio and MIDI details…",
        "Preparing the open project's current audio/MIDI data" => "Reading this project's audio and MIDI…",
        "Open project prepared" => "Project details ready",
        "Preparing rebuild" => "Preparing analysis…",
        "Rebuilding project asset index" => "Reading project details…",
        "Project asset index prepared" => "Project analyzed…",
        "Saving rebuilt asset indexes" => "Saving audio and MIDI details…",
        "Rebuilt asset indexes saved" => "Analysis results saved",
        "Source unavailable; keeping previous saved index" => "Some project files unavailable; previous details kept",
        "Preparing browser results" => "Organizing results…",
        "Index ready" => "Analysis ready",
        "Load interrupted" => "Analysis stopped",
        "Cancelled · partial results available" => "Analysis stopped · results so far are available",
        "Could not prepare browser results" => "Could not organize the results",
        _ => stage
    };
}

/// <summary>Builds focused task pages; shared spacing, type, and surfaces keep navigation consistent.</summary>
internal sealed partial class WorkspacePageFactory(AppShellViewModel shell, Action<string> navigate,
    DataTemplate changeRowTemplate,
    DataTemplate scanIssueTemplate, Action<ElementTheme> setTheme,
    MediaLibraryViewModel mediaLibrary, XamlRoot xamlRoot)
{
    public UIElement Create(string page)
    {
        return page switch
        {
            "Projects" => LibraryPage(),
            "Search" => LibrarySearchPage(),
            "Calendar" => CalendarPage(),
            "Overview" => OverviewPage(),
            "Tracks" => TracksPage(),
            "Plugins" => PluginsPage(),
            "Routing" => RoutingPage(),
            "IO" => IoPage(),
            "Statistics" => StatisticsPage(),
            "Compare" => ComparisonPage(),
            "Inventory" => InventoryPage(),
            "Catalog" => PluginCatalogPage(),
            "Preflight" => PreflightPage(),
            "Tools" => ToolsPage(),
            "Database" => DatabasePage(),
            "Samples" => SamplesPage(),
            "Midi" => MidiTracksPage(),
            "Help" => ReaderFaqPage(),
            "Settings" => SettingsPage(),
            _ => OverviewPage()
        };
    }

}
