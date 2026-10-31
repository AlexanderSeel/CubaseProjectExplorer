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
    private Button? _openButton;
    private Button? _scanButton;
    private PluginInventoryViewModel? _progressInventory;
    private readonly NavigationHistory _history = new("Overview");
    private bool _startupHydrating;
    private bool _rendering;

    public MainPage()
    {
        InitializeComponent();
        _openButton = OpenProjectButton;
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

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (_shell is null) return;
        await _shell.OpenProjectCommand.ExecuteAsync(null);
        StatusText.Text = _shell.Library.Status;
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

    private void Evidence_Click(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 12, MaxWidth = 900 };
        content.Children.Add(new TextBlock
        {
            Text = "Evidence and limitations",
            Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"]
        });
        content.Children.Add(new TextBlock
        {
            Text = "CPR/BAK track and plug-in markers remain candidates until verified against Cubase. Unknown values do not mean zero or absent.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"]
        });
        content.Children.Add(new TextBlock
        {
            Text = "Project files are inspection sources. Originals are never modified.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"]
        });
        var theme = _shell is null ? ElementTheme.Dark : _shell.Theme switch
        {
            "Light" => ElementTheme.Light,
            "System" => ElementTheme.Default,
            _ => ElementTheme.Dark
        };
        InfoWindowPresenter.Show("Evidence and limitations", content, theme, 760, 420);
    }

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
        CancelScanButton.Visibility = library.IsScanning ? Visibility.Visible : Visibility.Collapsed;
        if (_openButton is not null) _openButton.IsEnabled = !library.IsBusy;
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
                    _shell.MediaLibrary).Create(page) });
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
        "Samples" => "Sample finder", "Midi" => "MIDI tracks",
        _ => page
    };

    private void UpdateBusyIndicator()
    {
        if (_shell is null) return;
        var busy = _shell.IsAnyBusy;
        BusyIndicatorPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusySpinner.IsActive = busy;
        BusyText.Text = busy ? _shell.BusyStatus : string.Empty;
        ToolTipService.SetToolTip(BusyText, BusyText.Text);

        // Startup has its own full-window loading overlay. The detached progress
        // window is reserved for operations the user may continue working beside.
        var hasLongOperation = !_startupHydrating && ((_shell.Library.IsBusy && !_shell.Library.IsLoading) || _shell.PluginInventory.IsBusy ||
            _shell.Comparison.IsBusy || _shell.Tools.IsBusy || _shell.LibraryAdmin.IsBusy ||
            _shell.LibraryAdmin.IsRecordBrowserBusy || _shell.LibraryAdmin.IsQueryBusy ||
            _shell.PluginCatalog.IsBusy || _shell.Preflight.IsBusy || _shell.LibrarySearch.IsSearching ||
            _shell.MediaLibrary.IsBusy || _shell.MediaLibrary.IsMidiPreviewBusy ||
            _shell.Statistics.IsLibraryUsageBusy);
        if (!hasLongOperation)
        {
            ProgressWindowPresenter.Close();
            return;
        }

        var theme = _shell.Theme switch
        {
            "Light" => ElementTheme.Light,
            "System" => ElementTheme.Default,
            _ => ElementTheme.Dark
        };
        ProgressWindowPresenter.ShowOrUpdate(
            _shell.Library.IsScanning ? "Scanning project library" : _shell.MediaLibrary.IsIndexLoading ? "Media / MIDI index activity" : "Working in background",
            _shell.MediaLibrary.IsIndexLoading && !_shell.Library.IsScanning
                ? $"{_shell.MediaLibrary.IndexStage}\n{_shell.MediaLibrary.IndexProcessed:N0}/{_shell.MediaLibrary.IndexTotal:N0} projects · {_shell.MediaLibrary.IndexElapsed} elapsed\n{_shell.MediaLibrary.IndexCurrentProject}\n{_shell.MediaLibrary.IndexOutcome}"
                : _shell.BusyStatus,
            _shell.Library.IsScanning && _shell.Library.Progress > 0 ? _shell.Library.Progress :
                _shell.MediaLibrary.IsIndexLoading && _shell.MediaLibrary.IndexTotal > 0 ? _shell.MediaLibrary.IndexProgress : null,
            _shell.Library.IsScanning ? () => _shell.Library.CancelScanCommand.Execute(null) :
                _shell.MediaLibrary.IsIndexLoading && !_shell.MediaLibrary.IsIndexCancelling ? () => _shell.MediaLibrary.CancelIndexLoadCommand.Execute(null) : null,
            theme, _shell.Library.IsScanning ? "Cancel scan" : "Cancel index load");
    }
}

/// <summary>Builds focused task pages; shared spacing, type, and surfaces keep navigation consistent.</summary>
internal sealed partial class WorkspacePageFactory(AppShellViewModel shell, Action<string> navigate,
    DataTemplate changeRowTemplate,
    DataTemplate scanIssueTemplate, Action<ElementTheme> setTheme,
    MediaLibraryViewModel mediaLibrary)
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
            "Settings" => SettingsPage(),
            _ => OverviewPage()
        };
    }

}
