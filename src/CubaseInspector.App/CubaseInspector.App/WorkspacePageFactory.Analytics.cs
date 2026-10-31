using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CubaseInspector.App.ViewModels;
using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement StatisticsPage()
    {
        var root = Page("Project statistics", shell.Statistics.Summary);
        root.Children.Add(Distribution("Track record types", shell.Statistics.Tracks));
        root.Children.Add(Distribution("Confirmed plug-in formats", shell.Statistics.Plugins));
        root.Children.Add(LibraryPluginUsagePanel());
        return root;
    }

    private UIElement LibraryPluginUsagePanel()
    {
        _ = shell.Statistics.LoadLibraryUsageCommand.ExecuteAsync(null);
        var panel = new StackPanel { Background = Surface, Padding = SectionInset, Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Across the saved library", FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var status = new TextBlock { Text = shell.Statistics.LibraryUsageStatus, TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] };
        panel.Children.Add(status);
        var refresh = Button("Refresh library ranking", async () => await shell.Statistics.LoadLibraryUsageCommand.ExecuteAsync(null));
        panel.Children.Add(refresh);
        var confirmedSources = new TextBlock { Text = "Select a row to see the saved project sources.",
            FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = Muted };
        void ShowConfirmedSources(LibraryPluginUsageRow? row)
        {
            if (row is not null)
                confirmedSources.Text = "Saved project sources (" + row.ProjectPaths.Count + "):" + Environment.NewLine + string.Join(Environment.NewLine, row.ProjectPaths);
            else confirmedSources.Text = "Select a row to see the saved project sources.";
        }
        var confirmed = new DataBrowserControl<LibraryPluginUsageRow>(
            [new("Plug-in label", "Name", row => row.Name, row => row.Name),
             new("Format", "Format", row => row.Format, row => row.Format),
             new("Manufacturer", "Manufacturer", row => row.Manufacturer, row => row.Manufacturer),
             new("Projects", "ProjectCount", row => row.ProjectCount.ToString("N0"), row => row.ProjectCount),
             new("References", "ReferenceCount", row => row.ReferenceCount.ToString("N0"), row => row.ReferenceCount),
             new("Export IDs¹", "DistinctExportedIds", row => row.DistinctExportedIds.ToString("N0"), row => row.DistinctExportedIds)],
            shell.Statistics.LibraryPluginUsage, ["None", "Format", "Manufacturer"], "None",
            (row, mode) => mode == "Format" ? row.Format : mode == "Manufacturer" ? row.Manufacturer : "",
            _ => { }, row => { ShowConfirmedSources(row); return Task.CompletedTask; }, row =>
            {
                var menu = new MenuFlyout();
                var sources = new MenuFlyoutItem { Text = "Show saved project sources" };
                sources.Click += (_, _) => ShowConfirmedSources(row);
                menu.Items.Add(sources);
                return menu;
            });
        AutomationProperties.SetName(confirmed, "Confirmed plug-in reference ranking");
        panel.Children.Add(confirmed);
        panel.Children.Add(confirmedSources);
        panel.Children.Add(new TextBlock { Text = "¹ IDs are reported only as a count; they are export-local and are not used to merge rows.",
            FontSize = 10, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        var candidateDetails = new StackPanel { Spacing = 5 };
        var candidateSources = new TextBlock { Text = "Select an exact token to see which saved projects contain it.",
            FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = Muted };
        void ShowCandidateSources(LibraryCandidateMarkerRow? row)
        {
            if (row is not null)
                candidateSources.Text = "Exact observed token; still unverified. Source projects (" + row.ProjectPaths.Count + "):" + Environment.NewLine + string.Join(Environment.NewLine, row.ProjectPaths);
            else candidateSources.Text = "Select an exact token to see which saved projects contain it.";
        }
        var candidates = new DataBrowserControl<LibraryCandidateMarkerRow>(
            [new("Exact token", "Token", row => row.Token, row => row.Token),
             new("Example label", "ExampleName", row => row.ExampleName, row => row.ExampleName),
             new("Projects", "ProjectCount", row => row.ProjectCount.ToString("N0"), row => row.ProjectCount),
             new("Occurrences", "OccurrenceCount", row => row.OccurrenceCount.ToString("N0"), row => row.OccurrenceCount)],
            shell.Statistics.LibraryCandidateMarkers, ["None"], "None", (_, _) => "", _ => { },
            row => { ShowCandidateSources(row); return Task.CompletedTask; }, row =>
            {
                var menu = new MenuFlyout();
                var sources = new MenuFlyoutItem { Text = "Show exact-token source projects" };
                sources.Click += (_, _) => ShowCandidateSources(row);
                menu.Items.Add(sources);
                return menu;
            });
        AutomationProperties.SetName(candidates, "Unverified candidate tokens");
        candidateDetails.Children.Add(candidates);
        candidateDetails.Children.Add(candidateSources);
        panel.Children.Add(new Expander { Header = "Unverified CPR / BAK candidate tokens · never confirmed plug-in identities",
            Content = candidateDetails, IsExpanded = false });
        PropertyChangedEventHandler statusChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(shell.Statistics.LibraryUsageStatus))
                status.Text = shell.Statistics.LibraryUsageStatus;
            refresh.IsEnabled = !shell.Statistics.IsLibraryUsageBusy;
        };
        shell.Statistics.PropertyChanged += statusChanged;
        panel.Unloaded += (_, _) => shell.Statistics.PropertyChanged -= statusChanged;
        return panel;
    }

    private UIElement ComparisonPage()
    {
        var root = Page("Compare projects", shell.Comparison.Status);
        var status = Note(shell.Comparison.Status);
        root.Children.Add(status);
        var choose = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 2) };
        var chooseA = Button("Choose project A…", async () => await shell.Comparison.ChooseBeforeCommand.ExecuteAsync(null));
        var chooseB = Button("Choose project B…", async () => await shell.Comparison.ChooseAfterCommand.ExecuteAsync(null));
        choose.Children.Add(chooseA);
        choose.Children.Add(chooseB);
        AddPageActions(root, choose);
        var assign = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        assign.Children.Add(Button("Use selected as A", () => shell.Comparison.UseSelectedAsBeforeCommand.Execute(null)));
        assign.Children.Add(Button("Use selected as B", () => shell.Comparison.UseSelectedAsAfterCommand.Execute(null)));
        assign.Children.Add(Button("Swap A / B", () => shell.Comparison.SwapCommand.Execute(null)));
        root.Children.Add(assign);
        var review = Button("Review track matches…", () => shell.Comparison.ReviewMatchesCommand.Execute(null));
        AddPageActions(root, review);
        var names = new Grid { ColumnSpacing = 8 };
        names.ColumnDefinitions.Add(new ColumnDefinition()); names.ColumnDefinitions.Add(new ColumnDefinition());
        names.Children.Add(Metric("PROJECT A", shell.Comparison.BeforeName));
        var b = Metric("PROJECT B", shell.Comparison.AfterName); Grid.SetColumn(b, 1); names.Children.Add(b); root.Children.Add(names);
        var matching = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset, Margin = new Thickness(0, 10, 0, 8) };
        matching.Children.Add(new TextBlock { Text = "Match tracks for this comparison", FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var matchingStatus = new TextBlock { Text = shell.Comparison.MatchingStatus, TextWrapping = TextWrapping.Wrap };
        matching.Children.Add(matchingStatus);
        var matchRows = new ListView
        {
            ItemsSource = shell.Comparison.TrackMatches, MaxHeight = 320,
            SelectionMode = ListViewSelectionMode.None, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <StackPanel Spacing="4" Margin="0,4,0,8">
                    <TextBlock Text="{Binding Label}" TextWrapping="Wrap"/>
                    <ComboBox ItemsSource="{Binding Targets}" DisplayMemberPath="Label"
                      SelectedItem="{Binding SelectedTarget, Mode=TwoWay}"
                      HorizontalAlignment="Stretch" MinHeight="36"
                      AutomationProperties.Name="{Binding Label}"
                      AutomationProperties.HelpText="Choose the corresponding project B track, or leave unmatched."/>
                  </StackPanel>
                </DataTemplate>
                """)
        };
        AutomationProperties.SetName(matchRows, "Project A to project B track matches");
        matching.Children.Add(matchRows);
        var saveMatches = Button("Save matches and compare", async () => await shell.Comparison.SaveMatchesCommand.ExecuteAsync(null));
        var compareOnce = Button("Compare once without saving", async () => await shell.Comparison.CompareOnceCommand.ExecuteAsync(null));
        matching.Children.Add(saveMatches);
        matching.Children.Add(compareOnce);
        root.Children.Add(matching);
        void UpdateMatching()
        {
            matching.Visibility = shell.Comparison.NeedsMatchReview ? Visibility.Visible : Visibility.Collapsed;
            matchingStatus.Text = shell.Comparison.MatchingStatus;
            matchRows.IsEnabled = saveMatches.IsEnabled = compareOnce.IsEnabled = !shell.Comparison.IsBusy;
            review.Visibility = shell.Comparison.CanReviewMatches ? Visibility.Visible : Visibility.Collapsed;
            review.IsEnabled = !shell.Comparison.IsBusy;
            foreach (var action in assign.Children.OfType<Button>()) action.IsEnabled = !shell.Comparison.IsBusy;
            if (names.Children[0] is Border { Child: StackPanel aPanel } && aPanel.Children.LastOrDefault() is TextBlock aName)
                aName.Text = shell.Comparison.BeforeName;
            if (b.Child is StackPanel bPanel && bPanel.Children.LastOrDefault() is TextBlock bName)
                bName.Text = shell.Comparison.AfterName;
        }
        UpdateMatching();
        var search = FilterTextBox("Find a change");
        search.TextChanged += (_, _) => shell.Comparison.Search = search.Text;
        var category = Combo("All change types", ["All", "Track", "Plugin", "Ambiguous", "CPR plugin UID", "CPR metadata coverage"], 180);
        category.SelectionChanged += (_, _) => shell.Comparison.Category = category.SelectedItem?.ToString() ?? "All";
        var filterBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { search, category } };
        root.Children.Add(filterBar);
        var heading = new TextBlock { Text = "Changes · " + shell.Comparison.ChangeCount, FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) };
        var changeRows = new ListView { ItemsSource = shell.Comparison.Changes, ItemTemplate = changeRowTemplate,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = ListViewSelectionMode.Single };
        shell.Comparison.Changes.CollectionChanged += (_, _) => heading.Text = "Changes · " + shell.Comparison.ChangeCount;
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (status.Child is TextBlock text) text.Text = shell.Comparison.Status;
            UpdateMatching();
            if (args.PropertyName == nameof(ProjectComparisonViewModel.IsBusy))
                chooseA.IsEnabled = chooseB.IsEnabled = !shell.Comparison.IsBusy;
        };
        shell.Comparison.PropertyChanged += handler;
        root.Unloaded += (_, _) => shell.Comparison.PropertyChanged -= handler;
        root.Children.Add(heading); root.Children.Add(changeRows);
        return root;
    }

    private UIElement Distribution(string heading, IEnumerable<StatisticRow> source)
    {
        var rows = source.ToArray();
        var panel = new StackPanel { Background = Surface, Padding = SectionInset, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (rows.Length == 0)
        {
            panel.Children.Add(new TextBlock { Text = "No supported values are available.", FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 175, 184, 196)) });
            return panel;
        }
        var maximum = Math.Max(1, rows.Max(row => row.Count));
        foreach (var row in rows)
        {
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            line.Children.Add(new TextBlock { Text = row.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis });
            var bar = new ProgressBar { Minimum = 0, Maximum = maximum, Value = row.Count, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1); line.Children.Add(bar);
            var value = new TextBlock { Text = row.Count.ToString("N0"), FontSize = 12, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(value, 2); line.Children.Add(value); panel.Children.Add(line);
        }
        panel.Children.Add(new TextBlock { Text = rows[0].Evidence, FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 175, 184, 196)) });
        return panel;
    }
}
