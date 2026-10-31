using System.Collections.Specialized;
using System.ComponentModel;
using CubaseInspector.App.ViewModels;
using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement LibraryPage()
    {
        var root = Page("Project Library", "Scan, filter and select projects. Native CPR details remain labelled by evidence.");
        var summary = new TextBlock { FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
        void UpdateSummary() => summary.Text = shell.Library.VisibleSummary;
        UpdateSummary();

        var search = FilterTextBox("Search project name or path…");
        search.Text = shell.Library.Search;
        search.MinWidth = 200;
        search.MaxWidth = 640;
        search.HorizontalAlignment = HorizontalAlignment.Stretch;
        search.TextChanged += (_, _) => shell.Library.Search = search.Text;
        var fileType = Combo("All file types", ["All", "CPR", "BAK", "DAWPROJECT"], 130);
        fileType.SelectedItem = shell.Library.FileType;
        fileType.SelectionChanged += (_, _) => shell.Library.FileType = fileType.SelectedItem?.ToString() ?? "All";
        var rescan = Button("Rescan", async () => { await shell.RescanCommand.ExecuteAsync(null); navigate("Projects"); });
        var exportMenu = new MenuFlyout();
        foreach (var (name, command) in new[]
                 {
                     ("CSV…", shell.Library.ExportCsvCommand),
                     ("Markdown…", shell.Library.ExportMarkdownCommand),
                     ("JSON…", shell.Library.ExportJsonCommand)
                 })
        {
            var item = new MenuFlyoutItem { Text = name };
            item.Click += async (_, _) => await command.ExecuteAsync(null);
            exportMenu.Items.Add(item);
        }
        var export = new Button { Content = "Export visible…", Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        export.Click += (_, _) => exportMenu.ShowAt(export);
        AddPageActions(root, rescan, export);

        var topBar = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBar.Children.Add(search);
        Grid.SetColumn(fileType, 1); topBar.Children.Add(fileType);
        var filterButton = new Button { Content = "Filters", MinWidth = 82,
            Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        Grid.SetColumn(filterButton, 2); topBar.Children.Add(filterButton);
        var issuesButton = new Button { Visibility = Visibility.Collapsed,
            Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        Grid.SetColumn(issuesButton, 3); topBar.Children.Add(issuesButton);
        root.Children.Add(topBar);

        var evidence = Combo("Evidence", ["All", EvidencePresentation.Label(EvidenceLevel.Confirmed),
            EvidencePresentation.Label(EvidenceLevel.Candidate), EvidencePresentation.Label(EvidenceLevel.Unknown)], 170);
        evidence.SelectedItem = shell.Library.Evidence;
        evidence.SelectionChanged += (_, _) => shell.Library.Evidence = evidence.SelectedItem?.ToString() ?? "All";
        var version = FilterTextBox("Saved-by version…", 180);
        version.Text = shell.Library.VersionContains;
        version.TextChanged += (_, _) => shell.Library.VersionContains = version.Text;
        var folder = FilterTextBox("Folder contains…", 200);
        folder.Text = shell.Library.FolderContains;
        folder.TextChanged += (_, _) => shell.Library.FolderContains = folder.Text;
        var warnings = new CheckBox { Content = "Warnings only", VerticalAlignment = VerticalAlignment.Center, IsChecked = shell.Library.WarningsOnly };
        warnings.Checked += (_, _) => shell.Library.WarningsOnly = true;
        warnings.Unchecked += (_, _) => shell.Library.WarningsOnly = false;
        var clear = Button("Clear filters", () =>
        {
            fileType.SelectedItem = "All";
            evidence.SelectedItem = "All";
            version.Text = "";
            folder.Text = "";
            warnings.IsChecked = false;
        });
        void UpdateFilterButton()
        {
            var active = 0;
            if (fileType.SelectedItem?.ToString() is { } selectedType && selectedType != "All") active++;
            if (evidence.SelectedItem?.ToString() is { } selectedEvidence && selectedEvidence != "All") active++;
            if (!string.IsNullOrWhiteSpace(version.Text)) active++;
            if (!string.IsNullOrWhiteSpace(folder.Text)) active++;
            if (warnings.IsChecked == true) active++;
            filterButton.Content = active == 0 ? "Filters" : $"Filters · {active}";
        }
        fileType.SelectionChanged += (_, _) => UpdateFilterButton();
        evidence.SelectionChanged += (_, _) => UpdateFilterButton();
        version.TextChanged += (_, _) => UpdateFilterButton();
        folder.TextChanged += (_, _) => UpdateFilterButton();
        warnings.Checked += (_, _) => UpdateFilterButton();
        warnings.Unchecked += (_, _) => UpdateFilterButton();
        UpdateFilterButton();
        var filterPanel = new StackPanel { Spacing = 10, Padding = new Thickness(16), MinWidth = 320,
            Children = { new TextBlock { Text = "Refine projects", Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] } } };
        var filterGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(evidence, 0); Grid.SetRow(evidence, 0); filterGrid.Children.Add(evidence);
        Grid.SetColumn(version, 1); Grid.SetRow(version, 0); filterGrid.Children.Add(version);
        Grid.SetColumn(folder, 0); Grid.SetRow(folder, 1); Grid.SetColumnSpan(folder, 2); filterGrid.Children.Add(folder);
        Grid.SetColumn(warnings, 0); Grid.SetRow(warnings, 2); filterGrid.Children.Add(warnings);
        Grid.SetColumn(clear, 1); Grid.SetRow(clear, 2); filterGrid.Children.Add(clear);
        filterPanel.Children.Add(filterGrid);
        filterButton.Flyout = Popup(filterPanel, 520, 320);

        var issuesList = new ListView { ItemsSource = shell.Library.ScanIssues, ItemTemplate = scanIssueTemplate,
            SelectionMode = ListViewSelectionMode.None, MaxHeight = 420 };
        var issuesContent = new StackPanel { Spacing = 8, Padding = new Thickness(16), MinWidth = 420,
            Children = { new TextBlock { Text = "Scan issues", Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] },
                new TextBlock { Text = "Files that could not be indexed or were only partially read. Source files remain unchanged.",
                    TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] }, issuesList } };
        issuesButton.Flyout = Popup(issuesContent, 640, 520);
        void UpdateIssues()
        {
            issuesButton.Content = $"Scan issues · {shell.Library.ScanIssueCount:N0}";
            issuesButton.Visibility = shell.Library.ScanIssueCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        PropertyChangedEventHandler issueCountChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(shell.Library.ScanIssueCount)) UpdateIssues();
        };
        shell.Library.PropertyChanged += issueCountChanged;
        root.Unloaded += (_, _) => shell.Library.PropertyChanged -= issueCountChanged;
        UpdateIssues();
        root.Children.Add(summary);

        var columns = new DataBrowserColumn<LibraryProjectRow>[]
        {
            new("Project", "Name", row => row.Name, row => row.Name),
            new("File type", "FileType", row => row.FileType + " · " + row.Version, row => row.FileType),
            new("Tracks · evidence", "Tracks", row => row.Tracks + " · " + row.Evidence, row => row.TrackCount),
            new("Plug-in references", "Plugins", row => row.Plugins, row => row.PluginReferenceCount),
            new("Modified", "Modified", row => row.Modified, row => row.Entry.LastModifiedUtc),
            new("File size", "FileSize", row => row.FileSize, row => row.Entry.SizeBytes)
        };
        var browser = new DataBrowserControl<LibraryProjectRow>(columns, shell.Library.Projects,
            ProjectLibraryGrouping.Modes, shell.Library.GroupMode,
            (row, mode) => ProjectLibraryGrouping.Key(row.Entry, mode, shell.Library.LastScannedFolder),
            mode => shell.Library.GroupMode = mode,
            async row =>
            {
                await shell.Library.SelectProjectAsync(row);
                shell.NavigateTo("Overview");
                navigate("Overview");
            },
            row =>
            {
                var menu = new MenuFlyout();
                var inspect = new MenuFlyoutItem { Text = "Inspect project" };
                inspect.Click += async (_, _) =>
                {
                    await shell.Library.SelectProjectAsync(row);
                    shell.NavigateTo("Overview");
                    navigate("Overview");
                };
                menu.Items.Add(inspect);
                return menu;
            });
        root.Children.Add(browser);

        var emptyMessage = new TextBlock { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center };
        var emptyPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Children = { emptyMessage,
                new TextBlock { Text = "Scan a folder or clear the active filters.", FontSize = 12, Foreground = Muted,
                    HorizontalAlignment = HorizontalAlignment.Center },
                Button("Scan folder…", async () => { await shell.ScanFolderCommand.ExecuteAsync(null); navigate("Projects"); }) } };
        var emptyCard = new Border { Background = Surface, Padding = new Thickness(20), Height = 160,
            Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed, Child = emptyPanel };
        void UpdateEmptyState()
        {
            var empty = shell.Library.Projects.Count == 0;
            emptyCard.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            browser.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            emptyMessage.Text = shell.Library.AllEntries.Count == 0 ? "Your project library is empty." : "No projects match these filters.";
            UpdateSummary();
            export.IsEnabled = !empty && !shell.Library.IsBusy;
            rescan.IsEnabled = !shell.Library.IsBusy;
        }
        NotifyCollectionChangedEventHandler rowsChanged = (_, _) => UpdateEmptyState();
        shell.Library.Projects.CollectionChanged += rowsChanged;
        PropertyChangedEventHandler busyChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(ProjectLibraryViewModel.VisibleSummary)) UpdateSummary();
            if (args.PropertyName == nameof(ProjectLibraryViewModel.IsBusy)) UpdateEmptyState();
        };
        shell.Library.PropertyChanged += busyChanged;
        root.Unloaded += (_, _) =>
        {
            shell.Library.Projects.CollectionChanged -= rowsChanged;
            shell.Library.PropertyChanged -= busyChanged;
        };
        UpdateEmptyState();
        root.Children.Add(emptyCard);
        return root;
    }
}
