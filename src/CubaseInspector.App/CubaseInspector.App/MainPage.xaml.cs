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
    private readonly NavigationHistory _history = new("Projects");
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
        _progressLibrary.Status = "Loading indexed projects…";
        UpdateScanProgress();
        _shell.SetTheme(_shell.Theme);
        RequestedTheme = _shell.Theme switch { "Light" => ElementTheme.Light, "System" => ElementTheme.Default, _ => ElementTheme.Dark };
        StatusText.Text = _progressLibrary.Status;
        StartupOverlay.Visibility = Visibility.Visible;
        StartupSpinner.IsActive = true;
        StartupProgress.IsIndeterminate = true;
        StartupProgress.Value = 10;
        StartupStatus.Text = "Preparing local project and plug-in indexes…";
        StartupLoadedSummary.Text = "Your project files are not modified.";
        Render("Projects", recordHistory: false);
        _ = LoadStartupDataAsync(_shell, _progressLibrary);
    }

    private async Task LoadStartupDataAsync(AppShellViewModel shell, ProjectLibraryViewModel library)
    {
        // Let the first startup frame render before loading the local database.
        await Task.Delay(60);
        PropertyChangedEventHandler progressChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(ProjectLibraryViewModel.ProgressText) &&
                !string.IsNullOrWhiteSpace(library.ProgressText))
                StartupStatus.Text = library.ProgressText;
            if (args.PropertyName == nameof(ProjectLibraryViewModel.Progress))
            {
                StartupProgress.IsIndeterminate = library.Progress <= 0;
                StartupProgress.Value = Math.Clamp(library.Progress * 0.65, 10, 65);
            }
        };
        library.PropertyChanged += progressChanged;
        try
        {
            StartupStatus.Text = "Loading saved project snapshots and scan folders…";
            await library.LoadSavedProjectsAsync();
            StartupProgress.Value = 72;
            StartupStatus.Text = "Loading plug-in, catalog, and scan-folder counts…";
            StartupProgress.IsIndeterminate = false;
            await shell.LibraryAdmin.RefreshCommand.ExecuteAsync(null);
            StartupProgress.Value = 100;
            var hadDatabaseError = library.Status.StartsWith("Saved library could not be loaded", StringComparison.OrdinalIgnoreCase) ||
                shell.LibraryAdmin.Status.StartsWith("Database could not be read", StringComparison.OrdinalIgnoreCase);
            StartupStatus.Text = hadDatabaseError ? "Library opened with database warnings" : "Local library ready";
            StartupLoadedSummary.Text = $"{library.AllEntries.Count:N0} projects · {shell.LibraryAdmin.Plugins} local plug-in declarations · " +
                $"{shell.LibraryAdmin.CatalogProducts} catalog records · {shell.LibraryAdmin.Folders} scan folders" +
                (hadDatabaseError ? Environment.NewLine + library.Status + " " + shell.LibraryAdmin.Status : string.Empty);
            StatusText.Text = library.Status;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StartupStatus.Text = "The local database could not be fully loaded.";
            StartupLoadedSummary.Text = ex.Message;
            StatusText.Text = StartupStatus.Text;
        }
        finally
        {
            library.PropertyChanged -= progressChanged;
            await Task.Delay(250);
            StartupSpinner.IsActive = false;
            StartupOverlay.Visibility = Visibility.Collapsed;
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
            : _shell.HasProject ? $"{_shell.Workspace.Evidence} evidence" : "No project selected";
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
        "Tools" => "Reports & tools", "Database" => "Library database", "Compare" => "Compare projects",
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
