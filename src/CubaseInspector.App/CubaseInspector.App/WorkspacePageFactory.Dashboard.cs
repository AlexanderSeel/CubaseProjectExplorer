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
        var root = Page("Studio overview", "A quick view of your local Cubase project library and the next useful actions.");
        if (shell.DatabasePathWarning is { Length: > 0 } databaseWarning)
            root.Children.Add(Note(databaseWarning));
        var scan = Button("Scan a project folder…", async () =>
        {
            await shell.ScanFolderCommand.ExecuteAsync(null);
            navigate("Projects");
        });
        var open = Button("Open a project…", async () =>
        {
            await shell.OpenProjectCommand.ExecuteAsync(null);
            if (shell.HasProject) navigate("Overview");
        });
        var browse = Button("Browse project library", () => navigate("Projects"));
        AddPageActions(root, scan, open, browse);

        var metrics = new Grid { ColumnSpacing = 12, RowSpacing = 12, Margin = new Thickness(0, 12, 0, 8) };
        for (var column = 0; column < 2; column++)
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 2; row++)
            metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var projectMetric = Metric("INDEXED PROJECTS", "0");
        var visibleMetric = Metric("VISIBLE IN LIBRARY", "0");
        var issueMetric = Metric("LATEST SCAN ISSUES", "0");
        var typeMetric = Metric("PROJECT FORMATS", "No projects indexed");
        AddMetric(0, 0, projectMetric);
        AddMetric(1, 0, visibleMetric);
        AddMetric(0, 1, issueMetric);
        AddMetric(1, 1, typeMetric);
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
        root.Children.Add(health);

        var recentPanel = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset,
            Margin = new Thickness(0, 0, 0, 10) };
        recentPanel.Children.Add(new TextBlock { Text = "RECENTLY MODIFIED PROJECT FILES",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] });
        recentPanel.Children.Add(new TextBlock { Text = "Ordered by source file modification time; dates are local file metadata, not Cubase edit history.",
            TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] });
        var recentRows = new StackPanel { Spacing = 4 };
        recentPanel.Children.Add(recentRows);
        root.Children.Add(recentPanel);
        var evidenceNote = Note("Native CPR/BAK track and plug-in markers remain candidates. The dashboard counts indexed files and never treats unknown values as zero or absent.");
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
            var formats = all.Select(entry => Path.GetExtension(entry.Project.FilePath).TrimStart('.').ToUpperInvariant())
                .Where(value => value.Length > 0).GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Key + " " + group.Count().ToString("N0"));
            SetMetric(projectMetric, all.Count.ToString("N0"));
            SetMetric(visibleMetric, library.Projects.Count.ToString("N0"));
            SetMetric(issueMetric, library.ScanIssueCount.ToString("N0"));
            SetMetric(typeMetric, formats.Any() ? string.Join(" · ", formats)
                : library.IsLoading ? "Loading project formats…" : "No projects indexed");
            status.Text = library.IsLoading ? "Loading saved projects…" : library.IsScanning || library.IsFiltering
                ? string.IsNullOrWhiteSpace(library.ProgressText) ? library.Status : library.ProgressText
                : library.Status;
            folder.Text = string.IsNullOrWhiteSpace(library.LastScannedFolder)
                ? "Last scan folder: not recorded"
                : "Last scan folder: " + library.LastScannedFolder;
            healthNote.Text = library.IsLoading ? "The dashboard stays available while the saved library loads."
                : library.ScanIssueCount == 0
                ? "No issues recorded for the latest completed scan."
                : $"{library.ScanIssueCount:N0} files failed or were only partially read. Open Project Library → Scan issues for details.";
            recentRows.Children.Clear();
            var recent = all.OrderByDescending(entry => entry.LastModifiedUtc).Take(8).ToArray();
            if (recent.Length == 0)
            {
                recentRows.Children.Add(Empty(library.IsLoading
                    ? "Loading saved projects… You can browse other pages while the local library hydrates."
                    : "Your library is empty. Scan a project folder or open a project to get started."));
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
                    var path = new TextBlock { Text = row.FilePath,
                        Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
                        TextTrimming = TextTrimming.CharacterEllipsis };
                    var content = new StackPanel { Spacing = 3, Children = { title, metadata, path } };
                    var openProject = new Button { Content = content,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
                    AutomationProperties.SetName(openProject, "Open " + row.Name + ", " + row.FileType + ", " + row.Version);
                    ToolTipService.SetToolTip(openProject, row.FilePath);
                    openProject.Click += async (_, _) =>
                    {
                        if (library.IsBusy) return;
                        await library.SelectProjectAsync(row);
                        if (shell.HasProject) navigate("Overview");
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
