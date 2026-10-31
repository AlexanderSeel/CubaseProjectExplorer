using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CubaseInspector.App.ViewModels;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement PreflightPage()
    {
        var vm = shell.Preflight;
        vm.Refresh();
        var root = Page("Dependency preflight", "Review confirmed project plug-in dependencies against an explicitly scanned local declaration inventory.");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("Scan local plug-in inventory", async () =>
        {
            await shell.PluginInventory.ScanCommand.ExecuteAsync(null);
            vm.Refresh();
            navigate("Preflight");
        }));
        actions.Children.Add(Button("Refresh report", () => { vm.Refresh(); navigate("Preflight"); }));
        var exportMarkdown = Button("Export Markdown…", async () => await vm.ExportMarkdownCommand.ExecuteAsync(null));
        var exportCsv = Button("Export CSV…", async () => await vm.ExportCsvCommand.ExecuteAsync(null));
        actions.Children.Add(exportMarkdown);
        actions.Children.Add(exportCsv);
        AddPageActions(root, actions);
        root.Children.Add(new TextBlock { Text = vm.Evidence, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], Margin = new Thickness(0, 8, 0, 2) });
        root.Children.Add(Note(vm.Summary));
        var exportStatus = new TextBlock { Text = vm.Status, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
            Margin = new Thickness(0, 0, 0, 6) };
        if (!string.IsNullOrWhiteSpace(vm.Status)) root.Children.Add(exportStatus);
        root.Children.Add(new TextBlock { Text = "Dependency results", Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"], Margin = new Thickness(0, 10, 0, 4) });
        if (vm.Rows.Count == 0) root.Children.Add(Empty("No confirmed dependency rows are available for this project's current evidence."));
        else root.Children.Add(Section("Confirmed project references", vm.Rows));
        root.Children.Add(Section("Evidence and limitations", vm.Warnings));
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(ProjectPreflightViewModel.Status)) exportStatus.Text = vm.Status;
            if (args.PropertyName == nameof(ProjectPreflightViewModel.IsBusy))
                exportMarkdown.IsEnabled = exportCsv.IsEnabled = !vm.IsBusy;
        };
        vm.PropertyChanged += handler;
        root.Unloaded += (_, _) => vm.PropertyChanged -= handler;
        return root;
    }

    private UIElement PluginCatalogPage()
    {
        var vm = shell.PluginCatalog;
        if (!vm.IsBusy && vm.Results.Count == 0) _ = vm.LoadLibraryCommand.ExecuteAsync(null);
        var root = Page("Plug-in catalog", "Search public product metadata by name, manufacturer, category, or alias.");
        var catalogImportMenu = new MenuFlyout();
        foreach (var (label, command) in new[]
                 {
                     ("Download public CC0 catalog", vm.DownloadCommand),
                     ("Load offline catalog", vm.LoadOfflineCommand),
                     ("Search PluginDB API", vm.SearchApiCommand),
                     ("Grab VST3s products", vm.GrabVst3sCommand),
                     ("Grab KVR newest", vm.GrabKvrCommand)
                 })
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += async (_, _) => await command.ExecuteAsync(null);
            catalogImportMenu.Items.Add(item);
        }
        var catalogImport = new Button { Content = "Import or update…", Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        catalogImport.Click += (_, _) => catalogImportMenu.ShowAt(catalogImport);
        var loadDatabase = Button("Load saved products", async () => await vm.LoadLibraryCommand.ExecuteAsync(null));
        var downloadImages = new CheckBox
        {
            Content = "Download supplied images to database",
            IsChecked = vm.DownloadProductImages,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(downloadImages,
            "Opt in to downloading images explicitly listed by an import source. HTTPS only, at most 200 images and 64 MiB per import; image rights remain with the source.");
        downloadImages.Checked += (_, _) => vm.DownloadProductImages = true;
        downloadImages.Unchecked += (_, _) => vm.DownloadProductImages = false;
        AddPageActions(root, catalogImport, loadDatabase);
        root.Children.Add(downloadImages);

        var source = new TextBlock { Text = vm.Source, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], Margin = new Thickness(0, 8, 0, 2) };
        var status = new TextBlock { Text = vm.Status, Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextWrapping = TextWrapping.Wrap };
        root.Children.Add(source);
        root.Children.Add(status);
        var apiAddress = FilterTextBox("PluginDB API base URL", 300);
        apiAddress.Text = vm.ApiBaseAddress;
        apiAddress.TextChanged += (_, _) => vm.ApiBaseAddress = apiAddress.Text;
        root.Children.Add(apiAddress);
        var filters = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 12, 0, 8) };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var search = FilterTextBox("Product, alias, maker, or tag");
        search.TextChanged += (_, _) => vm.Search = search.Text;
        var manufacturer = FilterTextBox("Filter manufacturer");
        manufacturer.TextChanged += (_, _) => vm.Manufacturer = manufacturer.Text;
        Grid.SetColumn(search, 0); Grid.SetColumn(manufacturer, 1);
        filters.Children.Add(search); filters.Children.Add(manufacturer);
        root.Children.Add(filters);

        var results = new GridView { ItemsSource = vm.Results, SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 620, IsTabStop = true };
        results.ItemTemplate = new DataTemplate(() =>
        {
            var name = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(PluginMetadataEntry.Name)) });
            var vendor = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis };
            vendor.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(PluginMetadataEntry.Manufacturer)) });
            var category = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis };
            category.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(PluginMetadataEntry.Category)) });
            var format = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis };
            format.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(PluginMetadataEntry.FormatLabel)) });
            var card = new Border { Width = 280, MinHeight = 152, Padding = new Thickness(14), Margin = new Thickness(4),
                Background = Surface, BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Child = new StackPanel { Spacing = 7, Children = { name, vendor, category, format } } };
            card.SetBinding(ToolTipService.ToolTipProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(PluginMetadataEntry.Caption)) });
            return card;
        });
        var details = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset };
        var detailHeading = new TextBlock { Text = "Select a catalog product", Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] };
        var detailBody = new TextBlock { Text = "Catalog metadata is advisory. A name match does not prove a project instance, installed plug-in, or VST class identity.",
            Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextWrapping = TextWrapping.Wrap };
        details.Children.Add(detailHeading);
        details.Children.Add(detailBody);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        details.Children.Add(links);
        void ShowDetails(PluginMetadataEntry? item)
        {
            if (item is null)
            {
                detailHeading.Text = "Select a catalog product";
                detailBody.Text = "Catalog metadata is advisory. A name match does not prove a project instance, installed plug-in, or VST class identity.";
                links.Children.Clear();
                while (details.Children.Count > 3) details.Children.RemoveAt(3);
                return;
            }
            detailHeading.Text = item.Name;
            detailBody.Text = item.Description ?? "No description supplied.";
            links.Children.Clear();
            while (details.Children.Count > 3) details.Children.RemoveAt(3);
            foreach (var (label, value) in new[]
                     {
                         ("Manufacturer", item.Manufacturer), ("Category", item.Category),
                         ("Subcategory", item.Subcategory ?? "Not listed"), ("Formats", item.FormatLabel),
                         ("Aliases", item.Aliases.Count == 0 ? "None listed" : string.Join(", ", item.Aliases)),
                         ("Tags", item.TagLabel), ("Image", item.ImageUrl is null ? "No product image supplied" : "Product image supplied by catalog"), ("Source", item.Source)
                     })
                details.Children.Add(new TextBlock { Text = $"{label}   {value}", Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
                    TextWrapping = TextWrapping.Wrap });
            foreach (var (label, url) in new[] { ("Product website", item.Website), ("Manual", item.ManualUrl), ("Video", item.VideoUrl) })
            {
                if (url is null) continue;
                var link = Button(label, async () => await Windows.System.Launcher.LaunchUriAsync(new Uri(url)));
                links.Children.Add(link);
            }
        }
        results.SelectionChanged += (_, _) => { vm.SelectedEntry = results.SelectedItem as PluginMetadataEntry; ShowDetails(vm.SelectedEntry); };
        var resultsEmpty = new TextBlock { Text = "Download the public catalog or search a configured PluginDB API to browse products.",
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 8) };
        void UpdateCatalogEmpty() => resultsEmpty.Visibility = vm.Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifyCollectionChangedEventHandler catalogRowsChanged = (_, _) => UpdateCatalogEmpty();
        vm.Results.CollectionChanged += catalogRowsChanged;
        root.Unloaded += (_, _) => vm.Results.CollectionChanged -= catalogRowsChanged;
        UpdateCatalogEmpty();
        var body = new Grid { ColumnSpacing = 12, MinHeight = 300 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
        var catalogResults = new StackPanel { Spacing = 2, Children = { resultsEmpty, results } };
        Grid.SetColumn(catalogResults, 0); Grid.SetColumn(details, 1); body.Children.Add(catalogResults); body.Children.Add(details);
        root.Children.Add(body);

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(PluginCatalogViewModel.Status)) status.Text = vm.Status;
            if (args.PropertyName == nameof(PluginCatalogViewModel.Source)) source.Text = vm.Source;
            if (args.PropertyName == nameof(PluginCatalogViewModel.IsBusy))
                catalogImport.IsEnabled = loadDatabase.IsEnabled = downloadImages.IsEnabled = !vm.IsBusy;
        };
        vm.PropertyChanged += handler;
        root.Unloaded += (_, _) => vm.PropertyChanged -= handler;
        return root;
    }

    private UIElement InventoryPage()
    {
        var root = Page("Local plug-in inventory", shell.PluginInventory.Status);
        var vm = shell.PluginInventory;
        var scan = Button("Run VST grabber", async () => await vm.ScanCommand.ExecuteAsync(null));
        var cancelScan = Button("Cancel scan", () => vm.CancelScanCommand.Execute(null));
        scan.IsEnabled = !vm.IsBusy;
        cancelScan.Visibility = vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        AddPageActions(root, scan, cancelScan);
        var status = new TextBlock { Text = vm.Status, Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"],
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
        root.Children.Add(status);
        var search = FilterTextBox("Search name or path");
        search.Margin = new Thickness(0, 8, 0, 8);
        var tiles = new ObservableCollection<InstalledPluginCardRow>();
        var emptyInventory = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 8) };
        void RefreshTiles()
        {
            tiles.Clear();
            foreach (var plugin in vm.VisiblePlugins) tiles.Add(vm.BuildCardRow(plugin));
            emptyInventory.Text = vm.HasScanned ? "No local VST declarations were found in the supported scan roots."
                : "Run the VST grabber to inspect local VST declaration files.";
            emptyInventory.Visibility = tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => { vm.Search = search.Text; RefreshTiles(); };
        AddPageActions(root, search);
        var grid = new GridView { ItemsSource = tiles, SelectionMode = ListViewSelectionMode.None,
            MaxHeight = 520, IsTabStop = true };
        grid.ItemTemplate = new DataTemplate(() =>
        {
            var image = new Image { Width = 42, Height = 42, Stretch = Stretch.Uniform };
            image.SetBinding(Image.SourceProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPluginCardRow.Artwork)) });
            var fallback = new FontIcon { Glyph = "\uE71B", FontSize = 24, Foreground = Muted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var art = new Grid { Width = 48, Height = 48, Children = { fallback, image } };
            var name = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPluginCardRow.Name)) });
            var manufacturer = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis };
            manufacturer.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPluginCardRow.Manufacturer)) });
            var format = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] };
            format.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPluginCardRow.Format)) });
            var version = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] };
            version.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPluginCardRow.Version)) });
            var classes = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
                TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
            classes.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPluginCardRow.ClassSummary)) });
            var identity = new StackPanel { Spacing = 5, Children = { name, manufacturer, format, version, classes } };
            var body = new Grid { ColumnSpacing = 10, Children = { art, identity } };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(identity, 1);
            var card = new Border { Width = 286, MinHeight = 158, Padding = new Thickness(12), Margin = new Thickness(3),
                Background = Surface, BorderBrush = Divider, BorderThickness = new Thickness(1),
                Child = body };
            card.SetBinding(ToolTipService.ToolTipProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPluginCardRow.Path)) });
            return card;
        });
        RefreshTiles();
        root.Children.Add(grid);
        root.Children.Add(emptyInventory);
        root.Children.Add(Note("Local declarations come from the supported VST3 scan roots. A declaration does not prove that Cubase can load the plug-in; API catalog products are displayed separately."));
        PropertyChangedEventHandler inventoryHandler = (_, args) =>
        {
            if (args.PropertyName is nameof(PluginInventoryViewModel.Status) or nameof(PluginInventoryViewModel.IsBusy))
            {
                status.Text = vm.Status;
                scan.IsEnabled = !vm.IsBusy;
                cancelScan.Visibility = vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            }
            if (args.PropertyName is nameof(PluginInventoryViewModel.Plugins) or nameof(PluginInventoryViewModel.HasScanned))
                RefreshTiles();
        };
        vm.PropertyChanged += inventoryHandler;
        root.Unloaded += (_, _) => vm.PropertyChanged -= inventoryHandler;
        return root;
    }

    private UIElement ToolsPage()
    {
        var project = shell.Workspace.Project;
        var root = Page("Project report", project is null
            ? "Open a project or choose one from the Project Library to review its report."
            : "Review the selected project's available fields, source evidence and limitations before exporting.");
        var export = Button("Export Markdown report…", async () => await shell.Tools.ExportMarkdownCommand.ExecuteAsync(null));
        var copy = Button("Create byte-identical copy…", async () => await shell.Tools.CreateUnmodifiedCopyCommand.ExecuteAsync(null));
        AddPageActions(root, export, copy);
        if (project is null)
        {
            root.Children.Add(Empty("No project is selected. Open or select a project to build a report preview."));
            root.Children.Add(Button("Browse project library", () => navigate("Projects")));
            export.IsEnabled = copy.IsEnabled = false;
            return root;
        }

        root.Children.Add(Note(project.Evidence == EvidenceLevel.Candidate
            ? "This is a reader's interpretation of native CPR/BAK evidence. The report keeps source offsets and clearly separates observations from established Cubase meaning."
            : "This report summarizes fields declared by the structured project export. Missing fields remain unknown; the source project is unchanged."));

        var summary = DetailCard("SESSION SNAPSHOT", ("Project", project.Name), ("Format / saved by", project.Format + " · " + (project.CreatorVersion ?? "version unknown")),
            ("Tempo", project.Bpm is double bpm ? bpm.ToString("0.##") + " BPM" : "Not supplied"),
            ("Meter", project.TimeSignatureNumerator is int numerator && project.TimeSignatureDenominator is int denominator ? $"{numerator}/{denominator}" : "Not supplied"),
            ("Reader evidence", EvidencePresentation.Label(project.Evidence)), ("Source", project.FilePath));
        root.Children.Add(summary);

        var metrics = new Grid { ColumnSpacing = 10, RowSpacing = 10, Margin = new Thickness(0, 4, 0, 10) };
        for (var column = 0; column < 3; column++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 2; row++) metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var values = new[]
        {
            ("TRACK RECORDS", project.Tracks.Count), ("PLUG-IN REFERENCES", project.Plugins.Count),
            ("ROUTES", project.Routing.Count), ("EXPORTED MIDI NOTES", project.MidiNotes?.Count ?? 0),
            ("AUDIO REFERENCES", project.MediaReferences?.Count ?? 0), ("READER WARNINGS", project.Warnings.Count)
        };
        for (var index = 0; index < values.Length; index++)
        {
            var metric = Metric(values[index].Item1, values[index].Item2.ToString("N0"));
            Grid.SetColumn(metric, index % 3); Grid.SetRow(metric, index / 3); metrics.Children.Add(metric);
        }
        var coverageFields = ProjectFieldCoverage.Evaluate(project);
        var overview = new StackPanel { Spacing = 10 };
        overview.Children.Add(metrics);
        overview.Children.Add(DetailCard("READER COVERAGE", ("Fields with values", coverageFields.Count(item => item.Value > 0).ToString("N0")),
            ("Fields absent", coverageFields.Count(item => item.Value == 0).ToString("N0")),
            ("Known warnings", project.Warnings.Count.ToString("N0")), ("Detailed source fields", "See Evidence & source tab")));
        overview.Children.Add(Note("Use the other tabs to review arrangement content, studio connections, media paths and exact parser evidence. Export Markdown for the complete bounded report."));

        var arrangement = new StackPanel { Spacing = 10 };
        arrangement.Children.Add(Section("Tracks and channels", Bounded(project.Tracks.Select(track =>
            $"{track.Name} · {track.Kind} · {track.Evidence} · source offset {track.SourceOffset?.ToString() ?? "unknown"}"), project.Tracks.Count)));
        arrangement.Children.Add(Section("Plug-in references", Bounded(project.Plugins.Select(plugin =>
            $"{plugin.Name} · {plugin.Format} · {plugin.Evidence} · vendor {plugin.Manufacturer ?? "unknown"}"), project.Plugins.Count)));

        var mediaMidi = new StackPanel { Spacing = 10 };
        mediaMidi.Children.Add(Section("Exported MIDI events", Bounded((project.MidiNotes ?? []).Select(note =>
            $"Track {note.TrackId} · {note.Time:0.###} {note.TimeUnit ?? "unit unknown"} · length {note.Duration:0.###} · pitch {note.Key} · velocity {note.Velocity?.ToString("0.###") ?? "unknown"}"), project.MidiNotes?.Count ?? 0)));
        mediaMidi.Children.Add(Section("Native MIDI observations", Bounded((project.NativeMidiNoteCandidates ?? []).Select(note =>
            $"Possible track {note.CandidateTrackName ?? "unknown"} · part {note.CandidatePartName ?? "unknown"} · position {note.PositionTicks:0.###} · length {note.DurationTicks:0.###} · pitch {note.MidiPitch} · offset 0x{note.FileOffset:X}"), project.NativeMidiNoteCandidates?.Count ?? 0)));
        mediaMidi.Children.Add(Section("Audio references", Bounded((project.MediaReferences ?? []).Select(item =>
            $"{item.Path} · {item.External switch { true => "external", false => "embedded", _ => "location unknown" }} · {item.Channels?.ToString() ?? "?"} channels · {item.SampleRate?.ToString("N0") ?? "?"} Hz"), project.MediaReferences?.Count ?? 0)));

        var connections = new StackPanel { Spacing = 10 };
        connections.Children.Add(Section("Routing records", Bounded(project.Routing.Select(edge =>
            $"{edge.FromChannelId} → {edge.ToChannelId} · {edge.Kind} · level {edge.Level?.ToString("0.###") ?? "unknown"}"), project.Routing.Count)));
        connections.Children.Add(Section("Timeline clips and markers", Bounded((project.TimelineItems ?? []).Select(item =>
            $"{item.Kind} · {item.Name ?? "(unnamed)"} · {item.Time:0.###} · duration {item.Duration?.ToString("0.###") ?? "unknown"}"), project.TimelineItems?.Count ?? 0)));

        var evidenceDetails = new StackPanel { Spacing = 10 };
        evidenceDetails.Children.Add(Section("Field availability", Bounded(coverageFields.Select(field => $"{field.Key} · {field.Value}"), coverageFields.Count)));
        evidenceDetails.Children.Add(Section("Reader warnings and limitations", Bounded(project.Warnings, project.Warnings.Count)));
        evidenceDetails.Children.Add(Section("Source metadata observations", Bounded((project.NativeMetadataCandidates ?? []).Select(item =>
            $"{item.Field} · {item.Value} · offset {item.ArchiveOffset} · {item.Status}"), project.NativeMetadataCandidates?.Count ?? 0)));
        evidenceDetails.Children.Add(Note("Each detail list shows up to 100 rows. Markdown export retains the full report. CPR ownership, timing and routing interpretations remain explicit reader evidence."));
        root.Children.Add(DashboardTabs(("Session overview", overview), ("Tracks & plug-ins", arrangement),
            ("Audio & MIDI", mediaMidi), ("Connections & timeline", connections), ("Evidence & source", evidenceDetails)));
        var status = new TextBlock { Text = shell.Tools.Status, TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] };
        root.Children.Add(status);
        PropertyChangedEventHandler handler = (_, _) =>
        {
            status.Text = shell.Tools.Status;
            export.IsEnabled = copy.IsEnabled = !shell.Tools.IsBusy;
        };
        shell.Tools.PropertyChanged += handler;
        root.Unloaded += (_, _) => shell.Tools.PropertyChanged -= handler;
        return root;

        static IEnumerable<string> Bounded(IEnumerable<string> values, int total)
        {
            var rows = values.Take(100).ToArray();
            if (total == 0) return ["No values were supplied by the selected project."];
            return total > rows.Length
                ? rows.Append($"{total - rows.Length:N0} additional rows are included in the Markdown export.")
                : rows;
        }
    }

    private UIElement SettingsPage()
    {
        var root = Page("Settings", "Application preferences are local to this app and do not change project files.");
        var theme = Combo("Choose theme", ["Dark", "Light", "System"]);
        theme.SelectedIndex = shell.Theme switch { "Light" => 1, "System" => 2, _ => 0 };
        theme.SelectionChanged += (_, _) => setTheme(theme.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Default,
            _ => ElementTheme.Dark
        });
        AddPageActions(root, theme);

        var scanTypes = shell.ProjectScanTypes;
        var scanSettings = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset,
            Margin = new Thickness(0, 10, 0, 8) };
        scanSettings.Children.Add(new TextBlock { Text = "PROJECT FILE TYPES",
            Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] });
        scanSettings.Children.Add(new TextBlock
        {
            Text = "Choose which formats folder scans inspect and which saved project summaries load into the workspace. Turning a type off keeps its records in the database, but skips opening those source files and hydrating those records until re-enabled.",
            Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextWrapping = TextWrapping.Wrap
        });
        var scanCpr = new CheckBox { Content = "Cubase projects (.cpr)", IsChecked = scanTypes.Cpr };
        var scanBak = new CheckBox { Content = "Cubase backup projects (.bak)", IsChecked = scanTypes.Bak };
        var scanDawproject = new CheckBox { Content = "DAWproject files (.dawproject)", IsChecked = scanTypes.Dawproject };
        var restoringScanTypes = false;
        var scanSettingsStatus = new TextBlock
        {
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
            TextWrapping = TextWrapping.Wrap
        };
        scanSettings.Children.Add(scanCpr);
        scanSettings.Children.Add(scanBak);
        scanSettings.Children.Add(scanDawproject);
        scanSettings.Children.Add(scanSettingsStatus);
        void RestoreScanTypes()
        {
            var saved = shell.ProjectScanTypes;
            restoringScanTypes = true;
            try
            {
                scanCpr.IsChecked = saved.Cpr;
                scanBak.IsChecked = saved.Bak;
                scanDawproject.IsChecked = saved.Dawproject;
            }
            finally { restoringScanTypes = false; }
        }
        void SaveScanTypes()
        {
            if (restoringScanTypes) return;
            var selected = new ProjectScanTypes(scanCpr.IsChecked == true, scanBak.IsChecked == true,
                scanDawproject.IsChecked == true);
            if (!selected.AnyEnabled)
            {
                scanSettingsStatus.Text = "Keep at least one project type enabled for folder scans.";
                RestoreScanTypes();
                return;
            }
            try
            {
                shell.SetProjectScanTypes(selected);
                scanSettingsStatus.Text = "Saved. This selection will be used for the next scan or rescan.";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                RestoreScanTypes();
                scanSettingsStatus.Text = "Could not save scan types: " + exception.Message;
            }
        }
        scanCpr.Checked += (_, _) => SaveScanTypes();
        scanCpr.Unchecked += (_, _) => SaveScanTypes();
        scanBak.Checked += (_, _) => SaveScanTypes();
        scanBak.Unchecked += (_, _) => SaveScanTypes();
        scanDawproject.Checked += (_, _) => SaveScanTypes();
        scanDawproject.Unchecked += (_, _) => SaveScanTypes();
        root.Children.Add(scanSettings);
        var appVersion = typeof(App).Assembly.GetName().Version?.ToString(4) ?? "Unknown";
        root.Children.Add(DetailCard("APPEARANCE", ("Theme", "Select the application appearance."),
            ("Density", "Compact workspace layout")));
        root.Children.Add(DetailCard("ABOUT", ("Application", "Cubase Project Explorer"), ("Version", appVersion)));
        root.Children.Add(theme);
        root.Children.Add(Note("Native CPR/BAK fields are candidate evidence unless independently confirmed. Original project files are opened read-only."));
        return root;
    }

    private UIElement DatabasePage()
    {
        var admin = shell.LibraryAdmin;
        var root = Page("Database administration", "Manage this library’s indexed metadata, backups, transfers and maintenance.");
        var metricsGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10, Margin = new Thickness(0, 10, 0, 8) };
        metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var metricCards = new[]
        {
            Metric("INDEXED PROJECTS", admin.Projects), Metric("LOCAL PLUG-INS", admin.Plugins),
            Metric("CATALOG PRODUCTS", admin.CatalogProducts), Metric("SCAN FOLDERS", admin.Folders),
            Metric("MEDIA / MIDI INDEXES", admin.AssetIndexes), Metric("DATABASE FILE", admin.DatabaseSize)
        };
        for (var i = 0; i < metricCards.Length; i++)
        {
            Grid.SetColumn(metricCards[i], i % 3); Grid.SetRow(metricCards[i], i / 3);
            metricsGrid.Children.Add(metricCards[i]);
        }
        metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(metricsGrid);
        StackPanel DatabaseLocation() => DetailCard("DATABASE LOCATION", ("Active this session", admin.DatabasePath),
            ("Selected for next launch", admin.NextLaunchDatabasePath), ("Write-ahead log", admin.JournalSize));
        var location = DatabaseLocation();
        root.Children.Add(location);
        root.Children.Add(Note("The library stores scan metadata, plug-in/catalog records and searchable media/MIDI indexes. Scanning never edits or copies your original Cubase projects."));

        var actions = new MenuFlyout();
        var selectItem = new MenuFlyoutItem { Text = "Use an existing database on next launch…" };
        selectItem.Click += async (_, _) => await admin.SelectExistingDatabaseCommand.ExecuteAsync(null);
        var locationItem = new MenuFlyoutItem { Text = "Copy database and set new path…" };
        locationItem.Click += async (_, _) => await admin.ChangeDatabasePathCommand.ExecuteAsync(null);
        var refreshItem = new MenuFlyoutItem { Text = "Refresh database summary" };
        refreshItem.Click += async (_, _) => await admin.RefreshCommand.ExecuteAsync(null);
        actions.Items.Add(selectItem); actions.Items.Add(locationItem); actions.Items.Add(refreshItem);
        var more = new Button { Content = "Database settings", Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        more.Click += (_, _) => actions.ShowAt(more);
        var import = Button("Import / merge…", async () => await admin.ImportDatabaseCommand.ExecuteAsync(null));
        var export = Button("Export backup…", async () => await admin.BackupCommand.ExecuteAsync(null));
        AddPageActions(root, import, export, more);

        var transfer = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset };
        transfer.Children.Add(new TextBlock { Text = "TRANSFER", Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] });
        transfer.Children.Add(new TextBlock { Text = "Import merges supported project, plug-in, catalog, scan-folder and asset-index records. Matching IDs already in this database are preserved. Export creates a consistent SQLite backup.",
            Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextWrapping = TextWrapping.Wrap });
        transfer.Children.Add(new TextBlock { Text = "Changing the database path copies it to an empty destination and takes effect after restart. Selecting an existing database also applies after restart.",
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextWrapping = TextWrapping.Wrap });
        root.Children.Add(transfer);

        var maintenance = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset };
        maintenance.Children.Add(new TextBlock { Text = "MAINTENANCE", Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] });
        maintenance.Children.Add(new TextBlock { Text = "Cleanup removes checksum-identical project records and orphaned project media/MIDI indexes. Compact checks SQLite integrity, then reclaims unused database space.",
            Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextWrapping = TextWrapping.Wrap });
        var cleanup = Button("Clean duplicate records…", async () =>
        {
            var dialog = new ContentDialog { Title = "Clean database records?",
                Content = "Checksum-identical project records and indexes without a project will be removed. Original project files are never changed.",
                PrimaryButtonText = "Clean database", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                XamlRoot = root.XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await admin.CleanDatabaseCommand.ExecuteAsync(null);
        });
        var optimize = Button("Check integrity and compact…", async () =>
        {
            var dialog = new ContentDialog { Title = "Check and compact database?",
                Content = "SQLite will check the active database and reclaim unused space. This can take time for a large library.",
                PrimaryButtonText = "Check and compact", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                XamlRoot = root.XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await admin.OptimizeDatabaseCommand.ExecuteAsync(null);
        });
        var maintenanceButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { cleanup, optimize } };
        maintenance.Children.Add(maintenanceButtons);
        root.Children.Add(maintenance);

        var recordsSection = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset,
            Margin = new Thickness(0, 10, 0, 0) };
        recordsSection.Children.Add(new TextBlock { Text = "TABLES & DOCUMENT RECORDS",
            Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] });
        recordsSection.Children.Add(Note("Inspect SQLite tables and supported document types. Record create/update/delete uses validated JSON and the app's document store; deleting a project also removes its media/MIDI index. Table structure and custom SQL are read-only."));
        var tableList = new ListView { ItemsSource = admin.DatabaseTables, MaxHeight = 190,
            SelectionMode = ListViewSelectionMode.Single };
        tableList.SelectionChanged += (_, _) =>
            admin.PrepareDatabaseTableQuery(tableList.SelectedItem as LibraryDatabaseTableInfo);
        tableList.ItemTemplate = new DataTemplate(() =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(6, 4) };
            var name = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            name.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Name") });
            var count = new TextBlock { Foreground = Muted };
            count.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("RowCount") });
            var columnsText = new TextBlock { Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
            columnsText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("ColumnsSummary") });
            row.Children.Add(name); row.Children.Add(count); row.Children.Add(columnsText);
            return row;
        });
        recordsSection.Children.Add(tableList);

        var recordToolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var recordType = new ComboBox { ItemsSource = admin.DatabaseRecordTypes, SelectedItem = admin.SelectedDatabaseRecordType,
            MinWidth = 260, MaxWidth = 420, Style = (Style)Application.Current.Resources["InspectorFilterComboBoxStyle"] };
        recordType.SelectionChanged += (_, _) =>
        {
            admin.SelectedDatabaseRecordType = recordType.SelectedItem?.ToString() ?? "";
            _ = admin.LoadDatabaseRecordsCommand.ExecuteAsync(null);
        };
        var recordSearch = FilterTextBox("Search record ID or JSON…", 280);
        recordSearch.TextChanged += (_, _) => admin.DatabaseRecordSearch = recordSearch.Text;
        var findRecords = Button("Find records", async () => await admin.LoadDatabaseRecordsCommand.ExecuteAsync(null));
        var newRecord = Button("New record…", admin.NewDatabaseRecord);
        var previousRecords = Button("Previous", async () => await admin.ChangeDatabaseRecordPageCommand.ExecuteAsync(-1));
        var nextRecords = Button("Next", async () => await admin.ChangeDatabaseRecordPageCommand.ExecuteAsync(1));
        recordToolbar.Children.Add(recordType); recordToolbar.Children.Add(recordSearch);
        recordToolbar.Children.Add(findRecords); recordToolbar.Children.Add(newRecord);
        recordToolbar.Children.Add(previousRecords); recordToolbar.Children.Add(nextRecords);
        recordsSection.Children.Add(recordToolbar);
        var recordStatus = new TextBlock { Text = admin.RecordEditorStatus, FontSize = 10, Foreground = Muted,
            TextWrapping = TextWrapping.Wrap };
        recordsSection.Children.Add(recordStatus);
        var recordColumns = new DataBrowserColumn<LibraryDatabaseRecordRow>[]
        {
            new("Record type", "Type", row => row.Type, row => row.Type),
            new("ID", "Id", row => row.Id, row => row.Id),
            new("Updated", "UpdatedAt", row => row.UpdatedAt, row => row.UpdatedAt),
            new("JSON preview", "Preview", row => row.Preview, row => row.Preview)
        };
        var recordBrowser = new DataBrowserControl<LibraryDatabaseRecordRow>(recordColumns, admin.DatabaseRecords,
            ["None", "Type"], "None", (row, mode) => mode == "Type" ? row.Type : "", _ => { },
            row => { admin.SelectDatabaseRecord(row); return Task.CompletedTask; },
            row =>
            {
                var menu = new MenuFlyout();
                var editItem = new MenuFlyoutItem { Text = "Edit JSON" };
                editItem.Click += (_, _) => admin.SelectDatabaseRecord(row);
                menu.Items.Add(editItem);
                return menu;
            });
        recordBrowser.MinHeight = 250; recordBrowser.MaxHeight = 460;
        recordsSection.Children.Add(recordBrowser);
        var editor = new TextBox { Text = admin.RecordJson, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"), FontSize = 11, MinHeight = 240, MaxHeight = 420,
            PlaceholderText = "Select a record or create a new one to edit its JSON…" };
        editor.TextChanged += (_, _) => admin.RecordJson = editor.Text;
        recordsSection.Children.Add(editor);
        var recordActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var createRecord = Button("Create record", async () => await admin.SaveDatabaseRecordCommand.ExecuteAsync(true));
        var updateRecord = Button("Update selected record", async () => await admin.SaveDatabaseRecordCommand.ExecuteAsync(false));
        var deleteRecord = Button("Delete selected record…", async () =>
        {
            if (admin.SelectedDatabaseRecord is null) return;
            var dialog = new ContentDialog { Title = "Delete this database record?",
                Content = $"Delete {admin.SelectedDatabaseRecord.Type} · {admin.SelectedDatabaseRecord.Id}? This changes indexed database metadata only.",
                PrimaryButtonText = "Delete record", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                XamlRoot = root.XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await admin.DeleteDatabaseRecordCommand.ExecuteAsync(null);
        });
        recordActions.Children.Add(createRecord); recordActions.Children.Add(updateRecord); recordActions.Children.Add(deleteRecord);
        recordsSection.Children.Add(recordActions);
        root.Children.Add(recordsSection);

        var querySection = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset,
            Margin = new Thickness(0, 10, 0, 0) };
        querySection.Children.Add(new TextBlock { Text = "READ-ONLY SQL QUERY",
            Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] });
        var queryEditor = new TextBox { Text = admin.DatabaseQuery, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"), FontSize = 11, MinHeight = 80, MaxHeight = 180 };
        queryEditor.TextChanged += (_, _) => admin.DatabaseQuery = queryEditor.Text;
        querySection.Children.Add(queryEditor);
        var runQuery = Button("Run query", async () => await admin.RunDatabaseQueryCommand.ExecuteAsync(null));
        var queryStatus = new TextBlock { Text = admin.DatabaseQueryStatus, FontSize = 10, Foreground = Muted,
            TextWrapping = TextWrapping.Wrap };
        querySection.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { runQuery, queryStatus } });
        var queryResults = new ListView { ItemsSource = admin.DatabaseQueryRows, MaxHeight = 360,
            SelectionMode = ListViewSelectionMode.None };
        queryResults.ItemTemplate = new DataTemplate(() =>
        {
            var line = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 10,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 3) };
            line.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Values") });
            return new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 0, 1), Child = line };
        });
        querySection.Children.Add(queryResults);
        root.Children.Add(querySection);

        var databaseNote = Note(admin.Status);
        var progress = new ProgressRing { Width = 18, Height = 18, IsActive = admin.IsBusy, Visibility = admin.IsBusy ? Visibility.Visible : Visibility.Collapsed };
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { progress, databaseNote } };
        root.Children.Add(statusRow);

        void UpdateMetric(int index, string value)
        {
            if (metricCards[index].Child is StackPanel stack && stack.Children.Count > 1 && stack.Children[1] is TextBlock text) text.Text = value;
        }
        PropertyChangedEventHandler handler = (_, args) =>
        {
            UpdateMetric(0, admin.Projects); UpdateMetric(1, admin.Plugins); UpdateMetric(2, admin.CatalogProducts);
            UpdateMetric(3, admin.Folders); UpdateMetric(4, admin.AssetIndexes); UpdateMetric(5, admin.DatabaseSize);
            if (args.PropertyName is nameof(admin.NextLaunchDatabasePath) or nameof(admin.JournalSize))
            {
                var locationIndex = root.Children.IndexOf(location);
                var updatedLocation = DatabaseLocation();
                root.Children[locationIndex] = updatedLocation;
                location = updatedLocation;
            }
            if (databaseNote.Child is TextBlock note) note.Text = admin.Status;
            if (args.PropertyName == nameof(admin.RecordJson)) editor.Text = admin.RecordJson;
            if (args.PropertyName == nameof(admin.RecordEditorStatus)) recordStatus.Text = admin.RecordEditorStatus;
            if (args.PropertyName == nameof(admin.DatabaseRecordTypes)) recordType.ItemsSource = admin.DatabaseRecordTypes;
            if (args.PropertyName == nameof(admin.SelectedDatabaseRecordType)) recordType.SelectedItem = admin.SelectedDatabaseRecordType;
            if (args.PropertyName == nameof(admin.DatabaseQueryStatus)) queryStatus.Text = admin.DatabaseQueryStatus;
            if (args.PropertyName == nameof(admin.DatabaseQuery)) queryEditor.Text = admin.DatabaseQuery;
            var recordBusy = admin.IsRecordBrowserBusy;
            recordType.IsEnabled = findRecords.IsEnabled = newRecord.IsEnabled = previousRecords.IsEnabled = nextRecords.IsEnabled = !recordBusy;
            createRecord.IsEnabled = updateRecord.IsEnabled = deleteRecord.IsEnabled = !recordBusy;
            runQuery.IsEnabled = !admin.IsQueryBusy;
            progress.IsActive = admin.IsBusy;
            progress.Visibility = admin.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            import.IsEnabled = export.IsEnabled = more.IsEnabled = cleanup.IsEnabled = optimize.IsEnabled = !admin.IsBusy;
            selectItem.IsEnabled = locationItem.IsEnabled = refreshItem.IsEnabled = !admin.IsBusy;
        };
        admin.PropertyChanged += handler;
        root.Unloaded += (_, _) => admin.PropertyChanged -= handler;
        _ = admin.RefreshCommand.ExecuteAsync(null);
        _ = admin.LoadDatabaseBrowserCommand.ExecuteAsync(null);
        _ = admin.RunDatabaseQueryCommand.ExecuteAsync(null);
        return root;
    }

    private UIElement CollectionPage<T>(string title, string description, IEnumerable<T> data, Func<T, string> label)
    {
        var root = Page(title, description);
        var search = FilterTextBox($"Search {title.ToLowerInvariant()}");
        AddPageActions(root, search);
        var visible = new ObservableCollection<string>();
        var count = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 175, 184, 196)),
            Margin = new Thickness(0, 0, 0, 5) };
        void RefreshRows()
        {
            visible.Clear();
            foreach (var row in data.Select(label).Where(row => string.IsNullOrWhiteSpace(search.Text) ||
                         row.Contains(search.Text, StringComparison.OrdinalIgnoreCase))) visible.Add(row);
            count.Text = $"{visible.Count:N0} shown";
        }
        search.TextChanged += (_, _) => RefreshRows();
        RefreshRows();
        root.Children.Add(count);
        root.Children.Add(new ListView { ItemsSource = visible, SelectionMode = ListViewSelectionMode.Single,
            HorizontalContentAlignment = HorizontalAlignment.Stretch });
        if (visible.Count == 0) root.Children.Add(Empty("No data is available from this project's current evidence."));
        return root;
    }
}
