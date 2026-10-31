using System.ComponentModel;
using CubaseInspector.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement MediaIndexActivityPanel()
    {
        var vm = mediaLibrary;
        var content = new StackPanel { Spacing = 8 };
        var stage = new TextBlock { FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(stage, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5 };
        var current = new TextBlock { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var counts = new TextBlock { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var outcome = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var scope = new ComboBox { Header = "Load scope", Width = 270, DisplayMemberPath = nameof(SampleProjectScopeOption.DisplayName) };
        var limit = new ComboBox { Header = "Notes to load", Width = 155, ItemsSource = new[] { 10000, 50000, 100000, 250000 }, SelectedItem = vm.IndexNoteLimit };
        AutomationProperties.SetName(scope, "Saved asset index scope");
        AutomationProperties.SetName(limit, "Maximum native notes retained when loading the index");
        var updating = false;
        void UpdateScopes()
        {
            updating = true;
            var options = new List<SampleProjectScopeOption> { new("", "Whole library") };
            if (shell.Workspace.Project is { } selected) options.Add(new(selected.FilePath, "Selected project · " + selected.Name));
            options.AddRange(vm.SampleProjects);
            scope.ItemsSource = options.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            scope.SelectedItem = ((IEnumerable<SampleProjectScopeOption>)scope.ItemsSource)
                .FirstOrDefault(option => option.Path.Equals(vm.IndexProjectPath, StringComparison.OrdinalIgnoreCase));
            updating = false;
        }
        UpdateScopes();
        limit.SelectionChanged += (_, _) => { if (!updating && limit.SelectedItem is int value) vm.IndexNoteLimit = value; };
        var reload = Button("Load saved index", async () =>
            await vm.LoadIndexScopeAsync((scope.SelectedItem as SampleProjectScopeOption)?.Path));
        var cancel = Button("Cancel · keep results", () => vm.CancelIndexLoadCommand.Execute(null));
        var openProject = Button("Use open project", async () =>
        {
            if (shell.Workspace.Project is { } project) await vm.LoadOpenProjectAsync(project);
        });
        var rebuild = Button("Rebuild from projects…", () =>
        {
            var selectedScope = scope.SelectedItem as SampleProjectScopeOption;
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(Note("Rebuilding updates saved indexes from project snapshots. Compact entries require reading their original project files. Missing or unreadable sources keep their previous index. You can cancel during the rebuild."));
            panel.Children.Add(DetailCard("REBUILD SCOPE", ("Projects", selectedScope?.DisplayName ?? "Whole library"), ("Database", shell.LibraryAdmin.DatabasePath)));
            var start = Button("Start rebuild", async () => await vm.RebuildIndexScopeAsync(selectedScope?.Path));
            start.IsEnabled = !vm.IsBusy;
            panel.Children.Add(start);
            ShowInformationWindow("Rebuild media and MIDI indexes", panel, 780, 470);
        });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { scope, limit } };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { reload, cancel, openProject, rebuild } };
        var detailsButton = Button("Activity details…", () =>
        {
            var panel = new StackPanel { Spacing = 12, DataContext = vm };
            panel.Children.Add(DetailCard("INDEX OPERATION", ("Database", shell.LibraryAdmin.DatabasePath), ("Active scope", vm.IndexScope),
                ("Notes to load", vm.IndexNoteLimit.ToString("N0")), ("Audio/track loading limit", "50,000 rows per category"), ("Browser rows", "2,000 matches per view")));
            foreach (var property in new[] { nameof(vm.IndexStage), nameof(vm.IndexCurrentProject), nameof(vm.IndexOutcome) })
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
                text.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(property) });
                panel.Children.Add(text);
            }
            var log = new ListView { ItemsSource = vm.IndexMessages, SelectionMode = ListViewSelectionMode.None, MaxHeight = 400 };
            log.ItemTemplate = new DataTemplate(() =>
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(6) };
                text.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(".") });
                return text;
            });
            panel.Children.Add(log);
            ShowInformationWindow("Media / MIDI index activity", panel, 940, 720);
        });
        content.Children.Add(stage); content.Children.Add(progress); content.Children.Add(current);
        content.Children.Add(counts); content.Children.Add(outcome); content.Children.Add(controls); content.Children.Add(actions);
        content.Children.Add(detailsButton);
        content.Children.Add(new TextBlock { Text = "Saved-index loading reads the database. Rebuild is a separate source-reading operation. Browsers show up to 2,000 rows per view; use search or a project scope to narrow larger results.",
            FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        void Update()
        {
            stage.Text = vm.IsIndexFiltering ? "Filtering and grouping browser results…" : vm.IndexStage;
            progress.Visibility = vm.IsIndexLoading || vm.IsIndexFiltering ? Visibility.Visible : Visibility.Collapsed;
            progress.IsIndeterminate = vm.IsIndexFiltering || vm.IndexTotal == 0;
            progress.Value = vm.IndexProgress;
            current.Text = vm.IndexCurrentProject;
            counts.Text = $"{vm.IndexProcessed:N0}/{vm.IndexTotal:N0} projects · {vm.IndexElapsed} elapsed · {vm.IndexSkipped:N0} skipped · active scope: {vm.IndexScope}";
            outcome.Text = vm.IndexOutcome;
            reload.Content = vm.IndexStage == "Load interrupted" ? "Retry saved-index load" : vm.IndexProcessed > 0 ? "Reload saved index" : "Load saved index";
            reload.IsEnabled = rebuild.IsEnabled = scope.IsEnabled = limit.IsEnabled = !vm.IsBusy;
            openProject.IsEnabled = !vm.IsBusy && shell.Workspace.Project is not null;
            cancel.Visibility = vm.IsIndexLoading ? Visibility.Visible : Visibility.Collapsed;
            cancel.IsEnabled = !vm.IsIndexCancelling;
        }
        PropertyChangedEventHandler changed = (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SampleProjects)) UpdateScopes();
            Update();
        };
        vm.PropertyChanged += changed;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { if (vm.IsIndexLoading) Update(); };
        var host = new Border { Background = Surface, BorderBrush = Divider, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Margin = new Thickness(0, 8, 0, 12), Child = content };
        host.Loaded += (_, _) => timer.Start();
        host.SizeChanged += (_, args) =>
        {
            controls.Orientation = actions.Orientation = args.NewSize.Width < 560 ? Orientation.Vertical : Orientation.Horizontal;
        };
        host.Unloaded += (_, _) => { timer.Stop(); vm.PropertyChanged -= changed; };
        Update();
        return host;
    }
}
