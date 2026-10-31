using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CubaseInspector.App.ViewModels;
using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement IoPage()
    {
        var vm = shell.Tools;
        vm.ResetMediaAuditForDifferentProject();
        var root = Page("Audio / MIDI I/O", shell.Workspace.IoSummary);
        root.Children.Add(Note("External media status means only that a file path exists. This check does not read audio, inspect embedded archive entries, prove loadability, or infer native CPR/BAK links."));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 8) };
        var check = Button("Check declared external media", async () => await vm.CheckMediaCommand.ExecuteAsync(null));
        var export = Button("Export handoff checklist…", async () => await vm.ExportMediaAuditCommand.ExecuteAsync(null));
        check.IsEnabled = !vm.IsBusy;
        export.IsEnabled = vm.HasCurrentMediaAudit && !vm.IsBusy;
        actions.Children.Add(check);
        actions.Children.Add(export);
        AddPageActions(root, actions);

        var summary = new TextBlock { Text = vm.MediaAuditSummary, Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"],
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        var status = new TextBlock { Text = vm.MediaAuditStatus, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(summary);
        root.Children.Add(status);
        var rows = new ListView { ItemsSource = vm.MediaAuditRows, SelectionMode = ListViewSelectionMode.None,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, MaxHeight = 560 };
        rows.ItemTemplate = new DataTemplate(() =>
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 7, 10, 7) };
            text.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(".") });
            return text;
        });
        root.Children.Add(rows);
        var hasStructuredMediaEvidence = shell.Workspace.Project is { Evidence: EvidenceLevel.Confirmed } project &&
            System.IO.Path.GetExtension(project.FilePath).Equals(".dawproject", StringComparison.OrdinalIgnoreCase);
        var empty = Empty(vm.HasMediaAudit
            ? hasStructuredMediaEvidence ? "No audio media references were declared in this export." : "Media references are unavailable or not decoded for this project format."
            : "Run the explicit external-media check to build a handoff checklist. Embedded audio is not inspected.");
        empty.Visibility = vm.MediaAuditRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        root.Children.Add(empty);
        void UpdateMediaAuditState()
        {
            empty.Visibility = vm.MediaAuditRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (empty.Child is TextBlock emptyText)
                emptyText.Text = vm.HasMediaAudit
                    ? hasStructuredMediaEvidence ? "No audio media references were declared in this export." : "Media references are unavailable or not decoded for this project format."
                    : "Run the explicit external-media check to build a handoff checklist. Embedded audio is not inspected.";
            check.IsEnabled = !vm.IsBusy;
            export.IsEnabled = vm.HasCurrentMediaAudit && !vm.IsBusy;
        }
        NotifyCollectionChangedEventHandler rowsChanged = (_, _) => UpdateMediaAuditState();
        vm.MediaAuditRows.CollectionChanged += rowsChanged;
        root.Unloaded += (_, _) => vm.MediaAuditRows.CollectionChanged -= rowsChanged;

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(ProjectToolsViewModel.MediaAuditSummary)) summary.Text = vm.MediaAuditSummary;
            if (args.PropertyName == nameof(ProjectToolsViewModel.MediaAuditStatus)) status.Text = vm.MediaAuditStatus;
            if (args.PropertyName is nameof(ProjectToolsViewModel.IsBusy) or nameof(ProjectToolsViewModel.HasMediaAudit))
                UpdateMediaAuditState();
        };
        vm.PropertyChanged += handler;
        root.Unloaded += (_, _) => vm.PropertyChanged -= handler;
        return root;
    }

    private UIElement OverviewPage()
    {
        var vm = shell.Workspace;
        var root = Page(vm.ProjectName, vm.ProjectPath);
        if (vm.Project is { } selectedProject)
        {
            var findProjectSamples = Button("Find samples used in this project", () =>
            {
                mediaLibrary.SetSampleProjectScope(selectedProject.FilePath);
                shell.NavigateTo("Samples");
                navigate("Samples");
            });
            AddPageActions(root, findProjectSamples);
        }
        var cards = new Grid { ColumnSpacing = 12, RowSpacing = 12, Margin = new Thickness(0, 12, 0, 16) };
        for (var i = 0; i < 3; i++) cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var metrics = new[] { ("TEMPO", vm.Bpm), ("TIME SIGNATURE", vm.TimeSignature), ("TRACK RECORDS", vm.TrackCount), ("PLUGIN REFERENCES", vm.PluginCount), ("ROUTES", vm.RouteCount) };
        for (var i = 0; i < metrics.Length; i++)
        {
            var card = Metric(metrics[i].Item1, metrics[i].Item2);
            Grid.SetColumn(card, i % 3); Grid.SetRow(card, i / 3); cards.Children.Add(card);
        }
        root.Children.Add(cards);
        root.Children.Add(Note(vm.OverviewSummary));
        var details = new Grid { ColumnSpacing = 16, RowSpacing = 12, Margin = new Thickness(0, 14, 0, 0) };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        details.Children.Add(DetailCard("SOURCE", ("Format", vm.Format), ("Saved by", vm.SavedBy), ("Source quality", vm.Evidence), ("Warnings", vm.WarningCount)));
        var next = DetailCard("WORKSPACES", ("Tracks & Channels", "Inspect track records and evidence"), ("Plug-ins", vm.PluginEvidence), ("Routing", vm.RoutingSummary), ("Audio / MIDI I/O", vm.IoSummary));
        Grid.SetColumn(next, 1); details.Children.Add(next); root.Children.Add(details);
        var coveragePanel = new Border { Background = Surface, Padding = new Thickness(12) };
        var coverageRows = vm.Project is null
            ? Array.Empty<(string Name, string Value)>()
            : ProjectFieldCoverage.Evaluate(vm.Project).Select(field => (field.Key, field.Value.ToString()))
                .Concat((vm.Project.NativeMetadataCandidates ?? []).Select(candidate =>
                    ($"{candidate.Field} · offset {candidate.ArchiveOffset}", candidate.Value + " · candidate")))
                .ToArray();
        coveragePanel.Child = DetailCard("FIELD AVAILABILITY", coverageRows);
        var coverageButton = new Button { Content = "Field availability…",
            Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        coverageButton.Flyout = Popup(coveragePanel, 720, 600);
        root.Children.Add(coverageButton);
        if (vm.Project?.NativeMidiNoteCandidates is { Count: > 0 } noteCandidates)
        {
            var preview = noteCandidates.Take(128).Select(note =>
                $"Position {note.PositionTicks.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} ticks · " +
                $"duration {note.DurationTicks.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} ticks · " +
                $"pitch {note.MidiPitch} · velocity {note.Velocity} · offset 0x{note.FileOffset:X}").ToArray();
            IEnumerable<string> noteRows = preview;
            if (noteCandidates.Count > preview.Length)
                noteRows = preview.Append($"{noteCandidates.Count - preview.Length:N0} additional candidates are preserved in JSON inspection output.");
            var candidateNotes = new StackPanel { Spacing = 6 };
            candidateNotes.Children.Add(Note("Cubase 15.0.40 RIF2 note-layout candidates. Tick units, tempo, clip placement, and track ownership are not verified; these are not normalized MIDI events."));
            candidateNotes.Children.Add(Section("Observed note records · first " + preview.Length + " of " + noteCandidates.Count,
                noteRows));
            root.Children.Add(new Expander { Header = $"Unverified RIF2 MIDI note candidates · {noteCandidates.Count:N0}",
                Content = candidateNotes, IsExpanded = false });
        }
        if (vm.Project?.WarpPoints is { Count: > 0 } warpPoints)
            root.Children.Add(new Expander { Header = $"Exported audio warp points · {warpPoints.Count:N0}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = Section("Declared coordinates; playback transformation is not reconstructed", warpPoints.Select(point =>
                    $"Track {point.TrackId ?? "unknown"} · {point.Time:0.###} {point.TimeUnit ?? "unit unknown"} → {point.ContentTime:0.###} {point.ContentTimeUnit ?? "unit unknown"}\n{point.SourcePath}")) });
        return root;
    }

    private UIElement TracksPage()
    {
        var root = Page("Tracks & Channels", shell.Workspace.TrackEvidence + " · saved values are shown only when supplied by the source.");
        var search = FilterTextBox("Search tracks or channels", 240);
        var kinds = new[] { "All" }.Concat(shell.Workspace.Tracks.Select(track => track.Kind)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)).ToArray();
        var kindFilter = Combo("Track type", kinds, 150);
        search.Width = 260;
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(search); controls.Children.Add(kindFilter); AddPageActions(root, controls);
        var count = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 188, 199)), Margin = new Thickness(0, 0, 0, 5) };
        root.Children.Add(count);
        var rows = new ResettableObservableCollection<TrackDisplayRow>();
        var empty = Empty("No track records match the current search and type."); empty.Visibility = Visibility.Collapsed;
        var selected = new Border { Background = Surface, Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var detail = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        selected.Child = detail;
        void Refresh()
        {
            selected.Visibility = Visibility.Collapsed;
            var query = search.Text.Trim(); var kind = kindFilter.SelectedItem?.ToString() ?? "All";
            IEnumerable<TrackInfo> items = shell.Workspace.Tracks.Where(track =>
                (kind == "All" || track.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) &&
                (query.Length == 0 || track.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 track.Kind.Contains(query, StringComparison.OrdinalIgnoreCase) || (track.ChannelId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));
            items = items.OrderBy(track => track.Name, StringComparer.OrdinalIgnoreCase);
            rows.ReplaceAll(items.Select(TrackDisplayRow.From));
            empty.Visibility = rows.Count == 0 && shell.Workspace.Tracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            count.Text = $"{rows.Count:N0} of {shell.Workspace.Tracks.Count:N0} records";
        }
        search.TextChanged += (_, _) => Refresh(); kindFilter.SelectionChanged += (_, _) => Refresh();
        void ShowTrackDetails(TrackDisplayRow row)
        {
            detail.Text = $"{row.Name} · {row.Type}\nChannel: {row.Channel} · Destination: {row.Destination}\nSaved value: {row.SavedValue} · {row.Status}\nSource quality: {row.Evidence}";
            selected.Visibility = Visibility.Visible;
        }
        var browser = new DataBrowserControl<TrackDisplayRow>(
            [new("Track", "Name", row => row.Name, row => row.Name),
             new("Type", "Type", row => row.Type, row => row.Type),
             new("Channel", "Channel", row => row.Channel, row => row.Channel),
             new("Destination", "Destination", row => row.Destination, row => row.Destination),
             new("Saved value · state", "SavedValue", row => row.SavedValue + " · " + row.Status, row => row.SavedValue),
             new("Evidence", "Evidence", row => row.Evidence, row => row.Evidence)],
            rows, ["None", "Type", "Evidence"], "None",
            (row, mode) => mode switch { "Type" => row.Type, "Evidence" => row.Evidence, _ => "" },
            _ => { }, row => { ShowTrackDetails(row); return Task.CompletedTask; }, row =>
            {
                var menu = new MenuFlyout();
                var inspect = new MenuFlyoutItem { Text = "Show track details" };
                inspect.Click += (_, _) => ShowTrackDetails(row);
                menu.Items.Add(inspect);
                return menu;
            });
        Refresh(); root.Children.Add(browser); root.Children.Add(empty); root.Children.Add(selected);
        if (shell.Workspace.Tracks.Count == 0) root.Children.Add(Empty("No track records were supplied by this project."));
        return root;
    }

    private UIElement PluginsPage()
    {
        var project = shell.Workspace.Project;
        var root = Page("Plug-ins used in this project", "Repeated plug-in names are grouped together. Select a card to see every observed location and its source quality.");
        var hasReferences = project?.Plugins.Count > 0;
        var search = FilterTextBox("Search plug-in or track name", 280);
        var formats = new[] { "All" }.Concat(shell.Workspace.Plugins.Select(plugin => plugin.Format)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)).ToArray();
        var format = Combo("Format", formats, 150);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(search); controls.Children.Add(format);
        if (hasReferences == true) AddPageActions(root, controls);

        var groups = project is null ? [] : ProjectPluginGrouping.Group(project);
        var artwork = project is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : shell.PluginInventory.FindVerifiedArtworkByGroup(project);
        var cards = new ResettableObservableCollection<PluginGroupCardRow>();
        var count = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], Margin = new Thickness(0, 0, 0, 8) };
        var cardTemplate = new DataTemplate(() =>
        {
            var image = new Image { Width = 56, Height = 56, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
            image.SetBinding(Image.SourceProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Item.Artwork") });
            var icon = new FontIcon { Glyph = "\uE71B", FontSize = 30, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var artOverlay = new Grid { Children = { icon, image } };
            var art = new Border { Width = 64, Height = 64, Background = Surface, CornerRadius = new CornerRadius(6), Child = artOverlay };
            ToolTipService.SetToolTip(art, "Local VST3 image associated by exact CID. This does not verify the project's plug-in version or loadability.");
            var title = new TextBlock { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            // DataBrowser templates receive BrowserRow<T>, whose display columns are
            // projected into ValueN. Binding directly to PluginGroupCardRow leaves
            // every custom card label blank.
            title.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value0") });
            var vendor = new TextBlock { FontSize = 12, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
            vendor.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value1") });
            var formatText = new TextBlock { FontSize = 11, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
            formatText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value2") });
            var uses = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            uses.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value3") });
            var reliability = new TextBlock { FontSize = 11, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
            reliability.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value4") });
            var locations = new TextBlock { FontSize = 11, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
            locations.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value5") });
            var body = new StackPanel { Spacing = 5, Children = { title, vendor, formatText, uses, reliability, locations } };
            var row = new Grid { ColumnSpacing = 12, Children = { art, body } };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(body, 1);
            var card = new Border { Width = 312, MinHeight = 152, Padding = new Thickness(14), Margin = new Thickness(4),
                Background = Surface, BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = row };
            card.SetBinding(ToolTipService.ToolTipProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(PluginGroupCardRow.Locations)) });
            return card;
        });
        var empty = Empty("No plug-in references match this search.");
        PluginGroupCardRow? selectedCard = null;
        var usageRows = new ObservableCollection<string>();
        var eqRows = new ObservableCollection<string>();
        var details = new StackPanel { Spacing = 8, Background = Surface, Padding = SectionInset, Margin = new Thickness(0, 10, 0, 0) };
        var detailTitle = new TextBlock { Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] };
        var detailSummary = new TextBlock { Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextWrapping = TextWrapping.Wrap };
        details.Children.Add(detailTitle); details.Children.Add(detailSummary);
        details.Children.Add(new TextBlock { Text = "WHERE THIS NAME APPEARS", Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] });
        var usageList = new ListView { ItemsSource = usageRows, SelectionMode = ListViewSelectionMode.None, MaxHeight = 220 };
        details.Children.Add(usageList);
        var eqDetails = new Expander { Header = "Exported channel EQ bands", Visibility = Visibility.Collapsed,
            Content = new ListView { ItemsSource = eqRows, SelectionMode = ListViewSelectionMode.None, MaxHeight = 240 } };
        details.Children.Add(eqDetails);
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var searchCatalog = Button("Search catalog", () =>
        {
            if (selectedCard is not { } card) return;
            shell.PluginCatalog.SearchFor(card.Name); navigate("Catalog");
        });
        var searchLocal = Button("Find in local VST inventory", () =>
        {
            if (selectedCard is not { } card) return;
            shell.PluginInventory.Search = card.Name; navigate("Inventory");
        });
        actionRow.Children.Add(searchCatalog); actionRow.Children.Add(searchLocal); details.Children.Add(actionRow);
        void SelectGroup(PluginGroupCardRow? card)
        {
            selectedCard = card;
            if (card is null) { details.Visibility = Visibility.Collapsed; return; }
            detailTitle.Text = card.Name;
            detailSummary.Text = $"{card.Vendor} · {card.Format} · {card.Uses}\n{EvidencePresentation.Explanation(card.Group.Evidence)}";
            usageRows.Clear();
            foreach (var use in card.Group.Uses)
            {
                var source = use.Reference;
                var suffix = source.SourceByteOffset is long offset ? $" · file position {offset:N0}" : string.Empty;
                usageRows.Add($"{use.Location}{suffix} · {EvidencePresentation.Label(source.Evidence)}");
            }
            details.Visibility = Visibility.Visible;
            eqRows.Clear();
            foreach (var band in project?.EqBands ?? [])
                if (card.Group.Uses.Any(use => use.Reference.Format == "Equalizer" &&
                    use.Reference.TrackId == band.TrackId && use.Reference.InstanceId == band.DeviceInstanceId))
                    eqRows.Add($"Track {band.TrackId} · band {band.BandIndex + 1} · {band.Type ?? "unknown"} · filter order {band.Order?.ToString() ?? "unknown"}\n" +
                        $"Frequency {band.Frequency?.ToString("0.###") ?? "unknown"} {band.FrequencyUnit ?? "unit unknown"} · gain {band.Gain?.ToString("0.###") ?? "unknown"} {band.GainUnit ?? "unit unknown"} · Q {band.Q?.ToString("0.###") ?? "unknown"} {band.QUnit ?? "unit unknown"} · enabled {band.Enabled?.ToString() ?? "unknown"}\n{band.SourcePath}");
            eqDetails.Visibility = eqRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        var browser = new DataBrowserControl<PluginGroupCardRow>(
            [new("Plug-in", "Name", row => row.Name, row => row.Name),
             new("Vendor", "Vendor", row => row.Vendor, row => row.Vendor),
             new("Format", "Format", row => row.Format, row => row.Format),
             new("Uses", "Uses", row => row.Uses, row => row.Group.Observations),
             new("Evidence", "Evidence", row => row.Reliability, row => row.Reliability),
             new("Locations", "Locations", row => row.Locations, row => row.Locations)],
            cards, ["None", "Format", "Vendor", "Evidence"], "None",
            (row, mode) => mode switch { "Format" => row.Format, "Vendor" => row.Vendor, "Evidence" => row.Reliability, _ => "" },
            _ => { }, row => { SelectGroup(row); return Task.CompletedTask; },
            row =>
            {
                var menu = new MenuFlyout();
                var more = new MenuFlyoutItem { Text = "Show all project locations" };
                more.Click += (_, _) => SelectGroup(row);
                var catalogItem = new MenuFlyoutItem { Text = "Search plug-in catalog" };
                catalogItem.Click += (_, _) => { shell.PluginCatalog.SearchFor(row.Name); navigate("Catalog"); };
                var localItem = new MenuFlyoutItem { Text = "Find in local VST inventory" };
                localItem.Click += (_, _) => { shell.PluginInventory.Search = row.Name; navigate("Inventory"); };
                menu.Items.Add(more); menu.Items.Add(catalogItem); menu.Items.Add(localItem);
                return menu;
            }, cardTemplate, cardsInitiallyVisible: true);
        void Refresh()
        {
            var selectedIdentity = selectedCard?.Group.Identity;
            SelectGroup(null);
            var query = search.Text.Trim(); var selectedFormat = format.SelectedItem?.ToString() ?? "All";
            var filtered = groups.Where(group =>
                (selectedFormat == "All" || group.Format.Contains(selectedFormat, StringComparison.OrdinalIgnoreCase)) &&
                (query.Length == 0 || group.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 (group.Vendor?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                 group.Uses.Any(use => use.Location.Contains(query, StringComparison.OrdinalIgnoreCase))))
                .ToArray();
            var nextCards = new List<PluginGroupCardRow>();
            foreach (var group in filtered)
            {
                ImageSource? image = null;
                if (artwork.TryGetValue(group.Identity, out var path))
                {
                    try { image = new BitmapImage { UriSource = new Uri(path, UriKind.Absolute) }; }
                    catch (UriFormatException) { }
                }
                nextCards.Add(new PluginGroupCardRow(group, image));
            }
            cards.ReplaceAll(nextCards);
            selectedCard = selectedIdentity is null ? null : cards.FirstOrDefault(card => card.Group.Identity == selectedIdentity);
            browser.SelectItem(selectedCard);
            if (selectedCard is not null) SelectGroup(selectedCard);
            count.Text = $"{filtered.Length:N0} plug-in names · {filtered.Sum(group => group.Observations):N0} project references · {filtered.Count(group => artwork.ContainsKey(group.Identity)):N0} local images";
            empty.Visibility = filtered.Length == 0 && groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => Refresh(); format.SelectionChanged += (_, _) => Refresh();
        PropertyChangedEventHandler artworkChanged = (_, args) =>
        {
            if (args.PropertyName != nameof(PluginInventoryViewModel.ArtworkRevision) || project is null) return;
            artwork = shell.PluginInventory.FindVerifiedArtworkByGroup(project);
            Refresh();
        };
        shell.PluginInventory.PropertyChanged += artworkChanged;
        root.Unloaded += (_, _) => shell.PluginInventory.PropertyChanged -= artworkChanged;
        root.Children.Add(count);
        if (hasReferences == true) { root.Children.Add(browser); root.Children.Add(empty); root.Children.Add(details); }
        else root.Children.Add(Empty(project?.Evidence == EvidenceLevel.Candidate
            ? "This CPR file contains possible plug-in records, but this data does not tell Cubase-confirmed plug-in use."
            : "No plug-in references are listed in this project file."));
        Refresh();
        return root;
    }

    private static void AddCard(Grid grid, UIElement card)
    {
        var index = grid.Children.Count;
        var row = index / 2;
        while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(card, row); Grid.SetColumn(card, index % 2); grid.Children.Add(card);
    }
}
