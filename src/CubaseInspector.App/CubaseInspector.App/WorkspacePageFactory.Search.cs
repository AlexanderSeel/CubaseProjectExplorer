using System.ComponentModel;
using CubaseInspector.App.ViewModels;
using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement LibrarySearchPage()
    {
        var viewModel = shell.LibrarySearch;
        var root = Page("Search everything", "One search over saved projects and the indexed audio, MIDI and plug-in references.");
        var search = FilterTextBox("Search names, paths, tracks, plug-ins…", 360);
        search.Text = viewModel.Query;
        search.TextChanged += (_, _) => viewModel.Query = search.Text;
        var scope = new ComboBox { ItemsSource = viewModel.Scopes, SelectedItem = viewModel.Scope, Width = 150,
            Style = (Style)Application.Current.Resources["InspectorFilterComboBoxStyle"] };
        scope.SelectionChanged += (_, _) => viewModel.Scope = scope.SelectedItem?.ToString() ?? "All";
        var controls = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 8) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.Children.Add(search); Grid.SetColumn(scope, 1); controls.Children.Add(scope);
        AddPageActions(root, controls);
        var status = new TextBlock { Text = viewModel.Status, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(status);
        var loadProgress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Visibility = viewModel.IsBusy || viewModel.IsSearching ? Visibility.Visible : Visibility.Collapsed,
            Children = { new ProgressRing { Width = 18, Height = 18, IsActive = viewModel.IsBusy || viewModel.IsSearching },
                new TextBlock { Text = "Preparing searchable records…", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center } } };
        root.Children.Add(loadProgress);

        var columns = new DataBrowserColumn<LibrarySearchRow>[]
        {
            new("Type", "Category", row => row.Category, row => row.Category),
            new("Name", "Name", row => row.Name, row => row.Name),
            new("Details", "Details", row => row.Details, row => row.Details),
            new("Project", "Project", row => row.Project, row => row.Project),
            new("Location", "Location", row => row.Location, row => row.Location),
            new("Evidence", "Evidence", row => row.Evidence, row => row.Evidence)
        };
        var browser = new DataBrowserControl<LibrarySearchRow>(columns, viewModel.Results,
            ["None", "Type"], "Type", (row, _) => row.Category, _ => { },
            async row => await ActivateSearchResultAsync(row),
            row =>
            {
                var menu = new MenuFlyout();
                var open = new MenuFlyoutItem { Text = row.Category is "Project" or "Plug-in" ? "Open source project" : "Show in browser" };
                open.Click += async (_, _) => await ActivateSearchResultAsync(row);
                menu.Items.Add(open);
                return menu;
            });
        root.Children.Add(browser);
        var empty = Empty("No matching records. Try another name, path, project, or type.");
        empty.Visibility = viewModel.IsBusy || viewModel.IsSearching || viewModel.Results.Count > 0
            ? Visibility.Collapsed : Visibility.Visible;
        root.Children.Add(empty);
        PropertyChangedEventHandler changed = (_, args) =>
        {
            if (args.PropertyName is nameof(viewModel.Status) or nameof(viewModel.TotalMatches) or nameof(viewModel.IsBusy))
                status.Text = viewModel.Status;
            if (args.PropertyName is nameof(viewModel.IsBusy) or nameof(viewModel.IsSearching) or nameof(viewModel.TotalMatches))
            {
                var busy = viewModel.IsBusy || viewModel.IsSearching;
                loadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
                if (loadProgress.Children[0] is ProgressRing ring) ring.IsActive = busy;
                empty.Visibility = busy || viewModel.Results.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        };
        viewModel.PropertyChanged += changed;
        root.Unloaded += (_, _) => viewModel.PropertyChanged -= changed;
        _ = viewModel.LoadAsync();
        return root;
    }

    private async Task ActivateSearchResultAsync(LibrarySearchRow row)
    {
        var result = row.Document;
        switch (result.Category)
        {
            case "Audio":
                shell.MediaLibrary.Search = result.Target ?? result.Title;
                shell.NavigateTo("Samples");
                navigate("Samples");
                break;
            case "MIDI":
            case "MIDI candidate":
                shell.MediaLibrary.MidiSearch = result.Title;
                shell.NavigateTo("Midi");
                navigate("Midi");
                break;
            case "Plug-in":
                if (result.Project is not null)
                {
                    await shell.Library.OpenProjectAsync(result.Project.FilePath);
                    if (shell.HasProject) { shell.NavigateTo("Plugins"); navigate("Plugins"); }
                }
                break;
            default:
                if (result.Project is not null)
                {
                    await shell.Library.OpenProjectAsync(result.Project.FilePath);
                    if (shell.HasProject) { shell.NavigateTo("Overview"); navigate("Overview"); }
                }
                break;
        }
    }
}
