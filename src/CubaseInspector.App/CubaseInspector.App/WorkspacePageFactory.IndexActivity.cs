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
        var scope = new ComboBox { Header = "Projects", Width = 270, DisplayMemberPath = nameof(SampleProjectScopeOption.DisplayName) };
        AutomationProperties.SetName(scope, "Choose which projects to analyze");
        void UpdateScopes()
        {
            var options = new List<SampleProjectScopeOption> { new("", "Whole library") };
            if (shell.Workspace.Project is { } selected) options.Add(new(selected.FilePath, "Selected project · " + selected.Name));
            options.AddRange(vm.SampleProjects);
            scope.ItemsSource = options.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            var selectedPath = string.IsNullOrWhiteSpace(vm.IndexProjectPath)
                ? shell.Workspace.Project?.FilePath ?? ""
                : vm.IndexProjectPath;
            scope.SelectedItem = ((IEnumerable<SampleProjectScopeOption>)scope.ItemsSource)
                .FirstOrDefault(option => option.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        }
        UpdateScopes();
        var cancel = Button("Cancel · keep results", () => vm.CancelIndexLoadCommand.Execute(null));
        var analyze = Button("Analyze", async () =>
            await shell.AnalyzeScopeAsync((scope.SelectedItem as SampleProjectScopeOption)?.Path));
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { scope, analyze, cancel } };
        var detailsButton = InfoButton(() =>
        {
            var panel = new StackPanel { Spacing = 12, DataContext = vm };
            panel.Children.Add(DetailCard("ANALYSIS DETAILS", ("Database", shell.LibraryAdmin.DatabasePath), ("Active scope", vm.IndexScope),
                ("Audio/track display limit", "50,000 rows per category"), ("Browser display limit", "2,000 matches per view")));
            panel.Children.Add(Note("Analyze uses the shared project-reading workflow for this scope. The selected project is re-read when it is open; other projects and the whole library refresh their audio and MIDI details. Unavailable or unreadable source files keep their previous results."));
            var limit = new ComboBox { Header = "MIDI note detail limit", Width = 210,
                ItemsSource = new[] { 10000, 50000, 100000, 250000 }, SelectedItem = vm.IndexNoteLimit };
            AutomationProperties.SetName(limit, "Maximum MIDI notes retained during analysis");
            limit.SelectionChanged += (_, _) => { if (limit.SelectedItem is int value) vm.IndexNoteLimit = value; };
            panel.Children.Add(limit);
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
            ShowInformationWindow("Analysis details", panel, 940, 720);
        }, "Technical details · index activity log");
        controls.Children.Add(detailsButton);
        content.Children.Add(stage); content.Children.Add(progress); content.Children.Add(current);
        content.Children.Add(counts); content.Children.Add(outcome); content.Children.Add(controls);
        content.Children.Add(new TextBlock { Text = "Choose a project or the whole library, then select Analyze to read its project and refresh audio and MIDI details.",
            FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        void Update()
        {
            stage.Text = vm.IsIndexFiltering ? "Organizing the results…" : vm.IndexStage switch
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
                _ => vm.IndexStage
            };
            progress.Visibility = vm.IsIndexLoading || vm.IsIndexFiltering ? Visibility.Visible : Visibility.Collapsed;
            progress.IsIndeterminate = vm.IsIndexFiltering || vm.IndexTotal == 0;
            progress.Value = vm.IndexProgress;
            current.Text = Path.GetFileName(vm.IndexCurrentProject);
            ToolTipService.SetToolTip(current, vm.IndexCurrentProject);
            counts.Text = vm.IndexTotal > 0
                ? $"{vm.IndexProcessed:N0} of {vm.IndexTotal:N0} projects · {vm.IndexProgress:0.#}% · {vm.IndexElapsed} elapsed · {vm.IndexSkipped:N0} skipped · Projects: {vm.IndexScope}"
                : $"Preparing project list · {vm.IndexElapsed} elapsed · Projects: {vm.IndexScope}";
            outcome.Text = vm.IndexOutcome;
            analyze.IsEnabled = scope.IsEnabled = !shell.IsAnyBusy;
            cancel.Visibility = vm.IsIndexLoading ? Visibility.Visible : Visibility.Collapsed;
            cancel.IsEnabled = !vm.IsIndexCancelling;
        }
        PropertyChangedEventHandler changed = (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SampleProjects)) UpdateScopes();
            Update();
        };
        vm.PropertyChanged += changed;
        shell.PropertyChanged += (_, _) => Update();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { if (vm.IsIndexLoading) Update(); };
        var host = new Border { Background = Surface, BorderBrush = Divider, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Margin = new Thickness(0, 8, 0, 12), Child = content };
        host.Loaded += (_, _) => timer.Start();
        host.SizeChanged += (_, args) =>
        {
            controls.Orientation = args.NewSize.Width < 560 ? Orientation.Vertical : Orientation.Horizontal;
        };
        host.Unloaded += (_, _) => { timer.Stop(); vm.PropertyChanged -= changed; };
        Update();
        return host;
    }
}
