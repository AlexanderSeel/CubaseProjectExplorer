using System.Collections.Specialized;
using System.ComponentModel;
using CubaseInspector.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement DashboardPage()
    {
        var library = shell.Library;
        var root = Page("Studio home", "Your sessions, instruments and ideas — all within reach.");
        if (shell.DatabasePathWarning is { Length: > 0 } databaseWarning)
            root.Children.Add(Note(databaseWarning));
        var scan = Button("Add a project folder", async () =>
        {
            await shell.ScanFolderCommand.ExecuteAsync(null);
            navigate("Projects");
        });
        var open = Button("Open a project", async () =>
        {
            await shell.OpenProjectCommand.ExecuteAsync(null);
            if (shell.HasProject) navigate("Overview");
        });
        var browse = Button("Browse project library", () => navigate("Projects"));
        AddPageActions(root, browse);
        open.Style = (Style)Application.Current.Resources["InspectorPrimaryButtonStyle"];
        var heroCopy = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        heroCopy.Children.Add(new TextBlock { Text = "Open a session. Find what you need.", FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        heroCopy.Children.Add(new TextBlock { Text = "Find a project, explore its tracks and plug-ins, and see where the music connects.",
            FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Left });
        var heroActions = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, MinWidth = 220,
            Children = { open, scan, new TextBlock { Text = "Your source files stay unchanged.", FontSize = 11,
                Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center } } };
        var hero = new Grid { ColumnSpacing = 24, RowSpacing = 12 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.Children.Add(heroCopy); Grid.SetColumn(heroActions, 1); hero.Children.Add(heroActions);
        hero.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 720;
            Grid.SetColumn(heroActions, narrow ? 0 : 1); Grid.SetRow(heroActions, narrow ? 1 : 0);
            heroActions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        };
        root.Children.Add(new Border { Background = Surface, BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16), Child = hero });

        var metrics = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (var column = 0; column < 4; column++)
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 2; row++)
            metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var projectMetric = Metric("INDEXED PROJECTS", "—");
        var visibleMetric = Metric("VISIBLE IN LIBRARY", "—");
        var issueMetric = Metric("LATEST SCAN ISSUES", "—");
        var typeMetric = Metric("PROJECT FORMATS", "—");
        AddMetric(0, 0, projectMetric);
        AddMetric(1, 0, visibleMetric);
        AddMetric(0, 1, issueMetric);
        AddMetric(1, 1, typeMetric);
        metrics.SizeChanged += (_, args) =>
        {
            var columns = args.NewSize.Width >= 960 ? 4 : args.NewSize.Width >= 500 ? 2 : 1;
            for (var i = 0; i < 4; i++)
            {
                metrics.ColumnDefinitions[i].Width = i < columns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Grid.SetColumn((FrameworkElement)metrics.Children[i], i % columns);
                Grid.SetRow((FrameworkElement)metrics.Children[i], i / columns);
            }
            while (metrics.RowDefinitions.Count < 4) metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        };
        root.Children.Add(metrics);

        var health = new StackPanel { Spacing = 5, Background = Surface, Padding = SectionInset,
            Margin = new Thickness(0, 2, 0, 10) };
        health.Children.Add(new TextBlock { Text = "LIBRARY STATUS", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"] };
        var folder = new TextBlock { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] };
        var healthNote = new TextBlock { TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] };
        health.Children.Add(status);
        health.Children.Add(folder);
        health.Children.Add(healthNote);
        var healthCard = new Border { Background = Surface, CornerRadius = new CornerRadius(20), Child = health };

        var recentPanel = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset,
            Margin = new Thickness(0, 0, 0, 10) };
        recentPanel.Children.Add(new TextBlock { Text = "Recently changed projects",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] });
        recentPanel.Children.Add(new TextBlock { Text = "Pick a session to explore. Dates come from the source files in your library.",
            TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] });
        var recentRows = new StackPanel { Spacing = 4 };
        recentPanel.Children.Add(recentRows);
        root.Children.Add(new Border { Background = Surface, CornerRadius = new CornerRadius(20), Child = recentPanel });
        root.Children.Add(healthCard);
        var evidenceNote = Note("Details marked for review always retain their source. Open Help & reader FAQ to understand what the reader can show.");
        root.Children.Add(evidenceNote);

        void AddMetric(int column, int row, Border card)
        {
            Grid.SetColumn(card, column);
            Grid.SetRow(card, row);
            metrics.Children.Add(card);
        }

        static void SetMetric(Border card, string value)
        {
            if (card.Child is StackPanel panel && panel.Children.OfType<TextBlock>().LastOrDefault() is { } text)
                text.Text = value;
        }

        void Refresh()
        {
            var all = library.AllEntries;
            var startupLoading = library.IsLoading && all.Count == 0;
            var formats = all.Select(entry => Path.GetExtension(entry.Project.FilePath).TrimStart('.').ToUpperInvariant())
                .Where(value => value.Length > 0).GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Key + " " + group.Count().ToString("N0"));
            SetMetric(projectMetric, startupLoading ? "—" : all.Count.ToString("N0"));
            SetMetric(visibleMetric, startupLoading ? "—" : library.Projects.Count.ToString("N0"));
            SetMetric(issueMetric, startupLoading ? "—" : library.ScanIssueCount.ToString("N0"));
            SetMetric(typeMetric, formats.Any() ? string.Join(" · ", formats)
                : startupLoading ? "—" : "No projects indexed");
            health.Visibility = startupLoading ? Visibility.Collapsed : Visibility.Visible;
            recentPanel.Visibility = startupLoading ? Visibility.Collapsed : Visibility.Visible;
            status.Text = library.IsScanning || library.IsFiltering
                ? string.IsNullOrWhiteSpace(library.ProgressText) ? library.Status : library.ProgressText
                : library.Status;
            folder.Text = string.IsNullOrWhiteSpace(library.LastScannedFolder)
                ? "Last scan folder: not recorded"
                : "Last scan folder: " + library.LastScannedFolder;
            healthNote.Text = library.ScanIssueCount == 0
                ? "No issues recorded for the latest completed scan."
                : $"{library.ScanIssueCount:N0} files failed or were only partially read. Open Project Library → Scan issues for details.";
            recentRows.Children.Clear();
            var recent = all.OrderByDescending(entry => entry.LastModifiedUtc).Take(6).ToArray();
            if (recent.Length == 0)
            {
                recentRows.Children.Add(Empty("Your library is empty. Scan a project folder or open a project to get started."));
            }
            else
            {
                foreach (var entry in recent)
                {
                    var row = new LibraryProjectRow(entry);
                    var title = new TextBlock { Text = row.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis };
                    var metadata = new TextBlock
                    {
                        Text = $"{row.FileType} · {row.Version} · {row.Modified} · {row.Evidence}",
                        Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    var path = new TextBlock { Text = $"{row.Tracks} · {row.FileSize}",
                        Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
                        TextTrimming = TextTrimming.CharacterEllipsis };
                    var content = new StackPanel { Spacing = 3, Children = { title, metadata, path } };
                    var openProject = new Button { Content = content,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
                    AutomationProperties.SetName(openProject, "Open " + row.Name + ", " + row.FileType + ", " + row.Version);
                    ToolTipService.SetToolTip(openProject, row.FilePath);
                    openProject.Click += async (_, _) =>
                    {
                        if (library.IsBusy) return;
                        if (await library.SelectProjectAsync(row)) navigate("Overview");
                    };
                    recentRows.Children.Add(openProject);
                }
            }
            scan.IsEnabled = open.IsEnabled = !library.IsBusy;
        }

        NotifyCollectionChangedEventHandler rowsChanged = (_, _) => Refresh();
        PropertyChangedEventHandler libraryChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(ProjectLibraryViewModel.AllEntries) or
                nameof(ProjectLibraryViewModel.VisibleSummary) or nameof(ProjectLibraryViewModel.ScanIssueCount) or
                nameof(ProjectLibraryViewModel.LastScannedFolder) or nameof(ProjectLibraryViewModel.Status) or
                nameof(ProjectLibraryViewModel.ProgressText) or nameof(ProjectLibraryViewModel.IsBusy) or
                nameof(ProjectLibraryViewModel.IsLoading) or nameof(ProjectLibraryViewModel.IsScanning))
                Refresh();
        };
        library.Projects.CollectionChanged += rowsChanged;
        library.PropertyChanged += libraryChanged;
        root.Unloaded += (_, _) =>
        {
            library.Projects.CollectionChanged -= rowsChanged;
            library.PropertyChanged -= libraryChanged;
        };
        Refresh();
        return root;
    }
}
