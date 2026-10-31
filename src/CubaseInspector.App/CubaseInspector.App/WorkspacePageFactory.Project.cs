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
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement IoPage()
    {
        var vm = shell.Tools;
        vm.ResetMediaAuditForDifferentProject();
        var root = Page("Studio map", shell.Workspace.Project is null
            ? "See how your project is connected, then check audio paths and MIDI parts."
            : "Follow tracks through plug-ins, buses and outputs. Switch to Media paths to check the samples and MIDI in this project.");
        var studioPanel = new StackPanel { Spacing = 10 };
        if (shell.Workspace.Project is { } selectedProject)
            studioPanel.Children.Add(StudioIoView(selectedProject));
        else studioPanel.Children.Add(Empty("Choose Analyze to open a project, then follow its tracks, effects, buses and outputs here."));
        var mediaPanel = new StackPanel { Spacing = 10 };
        mediaPanel.Children.Add(new TextBlock { Text = "EXTERNAL MEDIA CHECKLIST", Margin = new Thickness(0, 8, 0, 4),
            Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] });
        mediaPanel.Children.Add(Note("Check project-declared paths against disk and prepare a handoff checklist. This reads the referenced file locations; it does not change the project."));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 8) };
        var check = Button("Check declared external media", async () => await vm.CheckMediaCommand.ExecuteAsync(null));
        var export = Button("Export handoff checklist…", async () => await vm.ExportMediaAuditCommand.ExecuteAsync(null));
        check.IsEnabled = !vm.IsBusy;
        export.IsEnabled = vm.HasCurrentMediaAudit && !vm.IsBusy;
        actions.Children.Add(check);
        actions.Children.Add(export);
        mediaPanel.Children.Add(actions);

        var summary = new TextBlock { Text = vm.MediaAuditSummary, Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"],
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        var status = new TextBlock { Text = vm.MediaAuditStatus, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        mediaPanel.Children.Add(summary);
        mediaPanel.Children.Add(status);
        var rows = new ListView { ItemsSource = vm.MediaAuditRows, SelectionMode = ListViewSelectionMode.None,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, MaxHeight = 560 };
        rows.ItemTemplate = new DataTemplate(() =>
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 7, 10, 7) };
            text.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(".") });
            return text;
        });
        mediaPanel.Children.Add(rows);
        var hasStructuredMediaEvidence = shell.Workspace.Project is { Evidence: EvidenceLevel.Confirmed } project &&
            System.IO.Path.GetExtension(project.FilePath).Equals(".dawproject", StringComparison.OrdinalIgnoreCase);
        var empty = Empty(vm.HasMediaAudit
            ? hasStructuredMediaEvidence ? "No audio media references were declared in this export." : "Media references are unavailable or not decoded for this project format."
            : "Run the explicit external-media check to build a handoff checklist. Embedded audio is not inspected.");
        empty.Visibility = vm.MediaAuditRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        mediaPanel.Children.Add(empty);
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
        root.Children.Add(DashboardTabs(("Signal flow", studioPanel), ("Media paths", mediaPanel)));
        return root;
    }

    private UIElement OverviewPage() => shell.Workspace.Project is null
        ? DashboardPage()
        : SelectedProjectOverviewPage();

    private UIElement SelectedProjectOverviewPage()
    {
        var vm = shell.Workspace;
        var root = Page(vm.ProjectName, vm.ProjectPath);
        if (vm.Project is { } selectedProject)
        {
            var analyze = Button("Analyze", async () => await shell.AnalyzeProjectCommand.ExecuteAsync(null));
            var findProjectSamples = Button("Find project audio", () =>
            {
                mediaLibrary.SetSampleProjectScope(selectedProject.FilePath);
                shell.NavigateTo("Samples");
                navigate("Samples");
            });
            AddPageActions(root, analyze, findProjectSamples);
        }
        var cards = new Grid { ColumnSpacing = 12, RowSpacing = 12, Margin = new Thickness(0, 12, 0, 16) };
        for (var i = 0; i < 3; i++) cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var metrics = new[] { ("TEMPO", vm.Bpm), ("TIME SIGNATURE", vm.TimeSignature), ("TRACKS", vm.TrackCount), ("PLUG-INS", vm.PluginCount), ("CONNECTIONS", vm.RouteCount) };
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
        var next = DetailCard("EXPLORE THIS PROJECT", ("Tracks & Channels", "Review tracks, mix settings and audio"), ("Plug-ins", vm.PluginEvidence), ("Routing", vm.RoutingSummary), ("Studio map", vm.IoSummary));
        Grid.SetColumn(next, 1); details.Children.Add(next); root.Children.Add(details);
        var coveragePanel = new Border { Background = Surface, Padding = new Thickness(12) };
        var coverageRows = vm.Project is null
            ? Array.Empty<(string Name, string Value)>()
            : ProjectFieldCoverage.Evaluate(vm.Project).Select(field => (field.Key, field.Value.ToString()))
                .Concat((vm.Project.NativeMetadataCandidates ?? []).Select(candidate =>
                    ($"{candidate.Field} · offset {candidate.ArchiveOffset}", candidate.Value + " · mapping unverified")))
                .ToArray();
        coveragePanel.Child = DetailCard("FIELD AVAILABILITY", coverageRows);
        AddPageActions(root, InfoButton(() => ShowInformationWindow("Field availability", coveragePanel, 920, 720),
            "Technical details · field availability and raw evidence"));
        if (vm.Project?.NativeMidiNoteCandidates is { Count: > 0 } noteCandidates)
        {
            var midiSummary = new StackPanel { Spacing = 8 };
            midiSummary.Children.Add(DetailCard("MIDI IN THIS PROJECT", ("Note events found", noteCandidates.Count.ToString("N0")),
                ("Next", "Browse notes and parts on the MIDI tracks page.")));
            midiSummary.Children.Add(Button("Browse MIDI notes", () => navigate("Midi")));
            root.Children.Add(midiSummary);
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
        var project = shell.Workspace.Project;
        var mixerTrackObservations = project is null ? Array.Empty<TrackInfo>() : CprSignalFlowPresentation.Create(project).Tracks
            .Where(item => item.SourceRecordClass == "CPR named record" && item.ParentTrackId is not null).ToArray();
        var root = Page("Tracks & Channels", "Review the arrangement, saved mix settings, and audio used by each track.");
        var search = FilterTextBox("Search track or type", 240);
        var kinds = new[] { "All" }.Concat(shell.Workspace.Tracks.Select(TrackDisplayRow.TypeFor)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)).ToArray();
        var kindFilter = Combo("Track type", kinds, 150);
        search.Width = 260;
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(search); controls.Children.Add(kindFilter); AddPageActions(root, controls);
        var count = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 188, 199)), Margin = new Thickness(0, 0, 0, 5) };
        root.Children.Add(count);
        var rows = new ResettableObservableCollection<TrackDisplayRow>();
        var empty = Empty("No tracks match the current search and type."); empty.Visibility = Visibility.Collapsed;
        var selected = new Border { Background = Surface, Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var detail = new StackPanel { Spacing = 8 };
        selected.Child = detail;
        void Refresh()
        {
            selected.Visibility = Visibility.Collapsed;
            var query = search.Text.Trim(); var kind = kindFilter.SelectedItem?.ToString() ?? "All";
            var allDisplayTracks = shell.Workspace.Tracks.OrderBy(track => track.Name, StringComparer.OrdinalIgnoreCase)
                .Select((track, index) => (Track: track, Row: TrackDisplayRow.From(track, index + 1)))
                .ToArray();
            var displayTracks = allDisplayTracks.Where(item =>
                (kind == "All" || item.Row.Type.Equals(kind, StringComparison.OrdinalIgnoreCase)) &&
                (query.Length == 0 || item.Row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 item.Row.Type.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
            var channelNames = displayTracks.Where(item => TrackDisplayRow.HasSourceValue(item.Track.ChannelId))
                .GroupBy(item => item.Track.ChannelId!, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single().Row.Name, StringComparer.Ordinal);
            rows.ReplaceAll(displayTracks.Select(item => item.Row with
            {
                Destination = TrackDisplayRow.HasSourceValue(item.Track.DestinationChannelId) &&
                    channelNames.TryGetValue(item.Track.DestinationChannelId!, out var destination)
                        ? destination : TrackDisplayRow.HasSourceValue(item.Track.DestinationChannelId) ? "Output not found" : "—"
            }));
            empty.Visibility = rows.Count == 0 && shell.Workspace.Tracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            count.Text = $"{rows.Count:N0} of {shell.Workspace.Tracks.Count:N0} tracks";
        }
        search.TextChanged += (_, _) => Refresh(); kindFilter.SelectionChanged += (_, _) => Refresh();
        void ShowTrackDetails(TrackDisplayRow row)
        {
            detail.Children.Clear();
            detail.Children.Add(new TextBlock { Text = row.Name, FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            detail.Children.Add(new TextBlock { Text = $"{row.Type} · Output {row.Destination}",
                FontSize = 12, TextWrapping = TextWrapping.Wrap });
            if (row.MixSummary != "—")
                detail.Children.Add(new TextBlock { Text = row.MixSummary,
                    Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextWrapping = TextWrapping.Wrap });
            if (project is not null && row.Source is { Evidence: EvidenceLevel.Candidate } source)
            {
                var mixerRecords = mixerTrackObservations
                    .Where(item => item.ParentTrackId == source.Id)
                    .ToArray();
                if (mixerRecords.Length == 1 && mixerRecords[0].SourceOffset is long mixerOffset &&
                    project.NativeMixerObservations?.SingleOrDefault(item => item.ArchiveOffset == mixerOffset) is { } mixer &&
                    (mixer.VolumeDb is not null || mixer.Mute is not null || mixer.Solo is not null || mixer.Pan is not null))
                {
                    var values = new List<(string Name, string Value)>();
                    if (mixer.VolumeDb is double level) values.Add(("Level", level.ToString("0.##") + " dB"));
                    if (mixer.Pan is double pan) values.Add(("Pan", pan.ToString("0.###")));
                    if (mixer.Mute is bool mute) values.Add(("Mute", mute ? "On" : "Off"));
                    if (mixer.Solo is bool solo) values.Add(("Solo", solo ? "On" : "Off"));
                    detail.Children.Add(DetailCard("MIXER SETTINGS FOUND", values.ToArray()));
                    detail.Children.Add(InfoButton(() =>
                    {
                        var evidence = new StackPanel { Spacing = 10 };
                        evidence.Children.Add(Note("These saved mixer values are linked to this track record by their byte range. The Cubase 15 ASM names solo and solo-defeat properties; exact field encoding and behavior still need comparison with controlled saves and other Cubase versions."));
                        evidence.Children.Add(DetailCard("SOURCE", ("Record name", mixer.Name), ("Source stream", mixer.SourceStream),
                            ("File position", $"0x{mixer.ArchiveOffset:X}"), ("Reader", mixer.Evidence)));
                        evidence.Children.Add(Section("Saved fields", (mixer.CandidateFields ?? []).Select(field =>
                            $"{field.Path} · {field.Type} · {field.Value} · 0x{field.ArchiveOffset:X}")));
                        ShowInformationWindow("Mixer settings · " + row.Name, evidence, 920, 680);
                    }, "More about these mixer settings"));
                }
            }
            detail.Children.Add(InfoButton(() =>
            {
                var source = row.Source;
                var evidence = new StackPanel { Spacing = 10 };
                evidence.Children.Add(DetailCard("SOURCE RECORD",
                    ("Project", project?.FilePath ?? "Not available"),
                    ("Stored class", source?.SourceRecordClass ?? "Not supplied"),
                    ("Serializer version", source?.SourceClassVersion is ushort version ? version.ToString() : "Not read"),
                    ("Record size", source?.SourceBodyBytes is long bodyBytes ? $"{bodyBytes:N0} bytes" : "Not read"),
                    ("Header format", source?.SourceClassEncoding ?? "Not read"),
                    ("Header offset", source?.SourceOffset is long offset ? $"0x{offset:X}" : "Not supplied"),
                    ("Record end", source?.SourceEndOffset is long endOffset ? $"0x{endOffset:X}" : "Not supplied")));
                evidence.Children.Add(Note("Cubase 15's assembly contains readers with revision-gated paths for earlier saved class revisions. This number is the object's serializer revision, not the Cubase release. The exact field meaning and track relationships still need comparison against projects from known Cubase versions."));
                if (source is not null) evidence.Children.Add(Note(EvidencePresentation.TrackExplanation(source)));
                ShowInformationWindow("Technical details · " + row.Name, evidence, 900, 620);
            }, "Technical details · source record, version and offsets"));
            selected.Visibility = Visibility.Visible;
        }
        void ShowTrackAudioReferences(TrackDisplayRow row)
        {
            if (project is null || row.Source is not { } track)
            {
                shell.Status = "Select a project to find its audio.";
                return;
            }
            var references = TrackAudioReferencesPresentation.Build(project, track);
            var firstPath = references.Associated.FirstOrDefault()?.Path;
            mediaLibrary.FocusNativeAudioTab = true;
            mediaLibrary.SetSampleProjectScope(project.FilePath);
            mediaLibrary.Search = firstPath ?? row.Name;
            _ = mediaLibrary.LoadOpenProjectAsync(project);
            shell.Status = firstPath is null
                ? $"Looking for audio used by {row.Name}…"
                : $"Finding audio for {row.Name}…";
            shell.NavigateTo("Samples");
            navigate("Samples");
        }
        var browser = new DataBrowserControl<TrackDisplayRow>(
            [new("Track", "Name", row => row.Name, row => row.Name),
             new("Type", "Type", row => row.Type, row => row.Type),
             new("Output", "Destination", row => row.Destination, row => row.Destination),
             new("Mix", "MixSummary", row => row.MixSummary, row => row.MixSummary)],
            rows, ["None", "Type"], "None",
            (row, mode) => mode switch { "Type" => row.Type, _ => "" },
            _ => { }, row => { ShowTrackDetails(row); return Task.CompletedTask; }, row =>
            {
                var menu = new MenuFlyout();
                var inspect = new MenuFlyoutItem { Text = "Track details" };
                inspect.Click += (_, _) => ShowTrackDetails(row);
                menu.Items.Add(inspect);
                var media = new MenuFlyoutItem { Text = "Find track audio" };
                media.Click += (_, _) => ShowTrackAudioReferences(row);
                menu.Items.Add(media);
                return menu;
            });
        Refresh(); root.Children.Add(browser); root.Children.Add(empty); root.Children.Add(selected);
        if (shell.Workspace.Tracks.Count == 0) root.Children.Add(Empty("No track records were supplied by this project."));
        return root;
    }

    private UIElement PluginsPage()
    {
        var project = shell.Workspace.Project;
        var root = Page("Plug-ins used in this project", "Browse plug-ins by name, maker, and where they appear in the session.");
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
            ToolTipService.SetToolTip(art, "Plug-in artwork from this computer, when available");
            var title = new TextBlock { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            // DataBrowser templates receive BrowserRow<T>, whose display columns are
            // projected into ValueN. Binding directly to PluginGroupCardRow leaves
            // every custom card label blank.
            title.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value0") });
            var uses = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            uses.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value3") });
            var locations = new TextBlock { FontSize = 11, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
            locations.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Value4") });
            var body = new StackPanel { Spacing = 7, Children = { title, uses, locations } };
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
        var productInfo = new TextBlock { Text = "Choose Find product info to see a saved description.",
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4) };
        var productArtwork = new Image { Width = 112, Height = 112, Stretch = Stretch.Uniform };
        var productProfile = new Grid { ColumnSpacing = 12, Children = { productArtwork, productInfo } };
        productProfile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        productProfile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(productInfo, 1);
        void ShowProductArtwork(ImageSource? source)
        {
            productArtwork.Source = source;
            productArtwork.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
            productProfile.ColumnDefinitions[0].Width = source is null ? new GridLength(0) : new GridLength(112);
        }
        details.Children.Add(detailTitle); details.Children.Add(detailSummary);
        details.Children.Add(productProfile);
        details.Children.Add(new TextBlock { Text = "IN THIS SESSION", Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] });
        var usageList = new ListView { ItemsSource = usageRows, SelectionMode = ListViewSelectionMode.None, MaxHeight = 220 };
        details.Children.Add(usageList);
        var eqDetails = new Expander { Header = "Exported channel EQ bands", Visibility = Visibility.Collapsed,
            Content = new ListView { ItemsSource = eqRows, SelectionMode = ListViewSelectionMode.None, MaxHeight = 240 } };
        details.Children.Add(eqDetails);
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        async Task FindProductInfoAsync()
        {
            if (selectedCard?.Group.Uses.FirstOrDefault()?.Reference is { } reference)
            {
                await shell.PluginCatalog.CheckProjectReferenceAsync(reference);
                var assessment = shell.PluginCatalog.Assess(reference);
                var product = assessment.Candidates.FirstOrDefault();
                productInfo.Text = product is null
                    ? "No saved product profile yet. Open product page to search for it."
                    : $"Catalog suggestion · {product.Name} · {product.Manufacturer} · {product.Category}" +
                      (string.IsNullOrWhiteSpace(product.Description) ? "" : "\n" + product.Description) +
                      $"\n{product.Source}";
                ShowProductArtwork(selectedCard.Artwork);
            }
        }
        async Task OpenProductPageAsync()
        {
            if (selectedCard is not { } card) return;
            var reference = card.Group.Uses.FirstOrDefault()?.Reference;
            var product = reference is null ? null : shell.PluginCatalog.Assess(reference).Candidates.FirstOrDefault();
            var url = product?.Source.Contains("KVR", StringComparison.OrdinalIgnoreCase) == true
                ? product.Website : ExternalPluginDiscovery.KvrSearchUrl(card.Name, card.Vendor);
            if (url is not null) await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
        async Task SaveProductDetailsAsync()
        {
            if (selectedCard?.Group.Uses.FirstOrDefault()?.Reference is { } reference)
            {
                await shell.PluginCatalog.GrabKvrForProjectReferenceAsync(reference);
                productInfo.Text = shell.PluginCatalog.ProjectLookupStatus.Contains("Added", StringComparison.Ordinal)
                    ? "Product details saved to the catalog."
                    : shell.PluginCatalog.ProjectLookupStatus.Contains("already in", StringComparison.Ordinal)
                        ? "Product details are already in the catalog."
                        : "No product details were added. Open product page to search manually.";
            }
        }
        void SearchProductCatalog()
        {
            if (selectedCard is not { } card) return;
            shell.PluginCatalog.SearchFor(card.Name); navigate("Catalog");
        }
        void FindOnThisComputer()
        {
            if (selectedCard is not { } card) return;
            shell.PluginInventory.Search = card.Name; navigate("Inventory");
        }
        var checkCatalog = Button("Find product info", async () => await FindProductInfoAsync());
        var researchKvr = Button("Search KVR", async () => await OpenProductPageAsync());
        actionRow.Children.Add(checkCatalog); actionRow.Children.Add(researchKvr);
        var moreActions = new Button { Content = "More", Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        var moreMenu = new MenuFlyout();
        var saveItem = new MenuFlyoutItem { Text = "Save product details" };
        saveItem.Click += async (_, _) => await SaveProductDetailsAsync();
        var catalogItem = new MenuFlyoutItem { Text = "Search product catalog" };
        catalogItem.Click += (_, _) => SearchProductCatalog();
        var localItem = new MenuFlyoutItem { Text = "Find on this computer" };
        localItem.Click += (_, _) => FindOnThisComputer();
        moreMenu.Items.Add(saveItem); moreMenu.Items.Add(catalogItem); moreMenu.Items.Add(localItem);
        moreActions.Flyout = moreMenu;
        actionRow.Children.Add(moreActions);
        details.Children.Add(actionRow);
        details.Children.Add(InfoButton(() =>
        {
            if (selectedCard is not { } card) return;
            var evidence = new StackPanel { Spacing = 10 };
            evidence.Children.Add(Note(EvidencePresentation.PluginGroupExplanation(card.Group)));
            evidence.Children.Add(Section("Project references", card.Group.Uses.Select(use =>
                use.Location + " · " + EvidencePresentation.PluginLabel(use.Reference) +
                (use.Reference.SourceByteOffset is long offset ? $" · file position {offset:N0}" : ""))));
            ShowInformationWindow("More about " + card.Name, evidence, 900, 620);
        }, "More about this plug-in"));
        PropertyChangedEventHandler lookupHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(PluginCatalogViewModel.IsBusy))
                checkCatalog.IsEnabled = researchKvr.IsEnabled = !shell.PluginCatalog.IsBusy;
        };
        shell.PluginCatalog.PropertyChanged += lookupHandler;
        root.Unloaded += (_, _) => shell.PluginCatalog.PropertyChanged -= lookupHandler;
        void SelectGroup(PluginGroupCardRow? card)
        {
            selectedCard = card;
            if (card is null) { details.Visibility = Visibility.Collapsed; return; }
            detailTitle.Text = card.Name;
            detailSummary.Text = string.Join(" · ", new[] { card.Vendor, card.Format, card.Uses }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            productInfo.Text = "Choose Find product info to see a saved description.";
            ShowProductArtwork(card.Artwork);
            usageRows.Clear();
            foreach (var use in card.Group.Uses)
                usageRows.Add(use.Reference.Evidence == EvidenceLevel.Confirmed
                    ? use.Location : "Project location not identified");
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
             new("Locations", "Locations", row => row.Locations, row => row.Locations)],
            cards, ["None", "Format", "Vendor"], "None",
            (row, mode) => mode switch { "Format" => row.Format, "Vendor" => row.Vendor, _ => "" },
            _ => { }, row => { SelectGroup(row); return Task.CompletedTask; },
            row =>
            {
                var menu = new MenuFlyout();
                var more = new MenuFlyoutItem { Text = "Show all project locations" };
                more.Click += (_, _) => SelectGroup(row);
                var catalogItem = new MenuFlyoutItem { Text = "Search plug-in catalog" };
                catalogItem.Click += (_, _) => { shell.PluginCatalog.SearchFor(row.Name); navigate("Catalog"); };
                var localItem = new MenuFlyoutItem { Text = "Find on this computer" };
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
