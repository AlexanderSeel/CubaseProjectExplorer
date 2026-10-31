using System.Collections.Specialized;
using System.ComponentModel;
using CubaseInspector.Core;
using CubaseInspector.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Shapes;
using Windows.Media.Core;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement SamplesPage()
    {
        var activeProject = shell.Workspace.Project;
        var root = Page("Audio", activeProject is null
            ? "Open a project to browse and preview its audio."
            : $"Browse and preview audio used in {activeProject.Name}.");
        if (activeProject is not null) mediaLibrary.SetSampleProjectScope(activeProject.FilePath);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var search = FilterTextBox("Search filename, folder, project, track…", 300);
        search.Text = mediaLibrary.Search;
        search.TextChanged += (_, _) => mediaLibrary.Search = search.Text;
        var type = new ComboBox { ItemsSource = mediaLibrary.SampleTypes, SelectedIndex = 0, Width = 130,
            Style = (Style)Application.Current.Resources["InspectorFilterComboBoxStyle"] };
        type.SelectedItem = mediaLibrary.SampleType;
        type.SelectionChanged += (_, _) => mediaLibrary.SampleType = type.SelectedItem?.ToString() ?? "All";
        actions.Children.Add(search); actions.Children.Add(type);
        AddPageActions(root, actions);
        var projectScope = new ComboBox { Width = 290, DisplayMemberPath = nameof(SampleProjectScopeOption.DisplayName),
            SelectedValuePath = nameof(SampleProjectScopeOption.Path),
            Style = (Style)Application.Current.Resources["InspectorFilterComboBoxStyle"] };
        void UpdateProjectScopeOptions()
        {
            projectScope.ItemsSource = new[] { new SampleProjectScopeOption("", "All projects") }
                .Concat(mediaLibrary.SampleProjects).ToArray();
            projectScope.SelectedValue = mediaLibrary.SampleProjectPath;
        }
        UpdateProjectScopeOptions();
        projectScope.SelectionChanged += (_, _) =>
        {
            if (projectScope.SelectedValue is string path) mediaLibrary.SampleProjectPath = path;
        };
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 4, 0, 8), Children =
            { new TextBlock { Text = "Samples used in", Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
                VerticalAlignment = VerticalAlignment.Center }, projectScope } });
        var duplicateActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        var duplicateScan = Button("Find exact duplicates…", async () => await mediaLibrary.FindByteIdenticalSamplesCommand.ExecuteAsync(null));
        var cancelDuplicateScan = Button("Cancel", () => mediaLibrary.CancelDuplicateScan());
        cancelDuplicateScan.Visibility = Visibility.Collapsed;
        var duplicateHint = new TextBlock { Text = "Explicit scan of filtered external audio only · up to 500 files · 150 MiB/file · 1 GiB total",
            FontSize = 10, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        duplicateActions.Children.Add(duplicateScan); duplicateActions.Children.Add(cancelDuplicateScan); duplicateActions.Children.Add(duplicateHint);
        var duplicateStatus = new TextBlock { Text = mediaLibrary.DuplicateScanStatus, FontSize = 10,
            Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        var summary = new TextBlock { Text = mediaLibrary.Status, Margin = new Thickness(0, 8, 0, 4),
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextWrapping = TextWrapping.Wrap };
        var loadRing = new ProgressRing { Width = 18, Height = 18, IsActive = mediaLibrary.IsBusy,
            Visibility = mediaLibrary.IsBusy ? Visibility.Visible : Visibility.Collapsed };
        SampleUseRow? selectedSample = null;
        void UpdateSummary() => summary.Text = mediaLibrary.Status + "  ·  " + mediaLibrary.SampleSummary;
        UpdateSummary();
        PropertyChangedEventHandler statusChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(mediaLibrary.Status) or nameof(mediaLibrary.SampleSummary)) UpdateSummary();
            if (args.PropertyName == nameof(mediaLibrary.SampleTypes)) type.ItemsSource = mediaLibrary.SampleTypes;
            if (args.PropertyName == nameof(mediaLibrary.SampleProjects)) UpdateProjectScopeOptions();
            if (args.PropertyName == nameof(mediaLibrary.IsBusy))
            {
                loadRing.IsActive = mediaLibrary.IsBusy;
                loadRing.Visibility = mediaLibrary.IsBusy ? Visibility.Visible : Visibility.Collapsed;
                duplicateScan.IsEnabled = !mediaLibrary.IsBusy && !mediaLibrary.IsDuplicateScanBusy;
            }
            if (args.PropertyName is nameof(mediaLibrary.DuplicateScanStatus) or nameof(mediaLibrary.IsDuplicateScanBusy))
            {
                duplicateStatus.Text = mediaLibrary.DuplicateScanStatus;
                duplicateScan.IsEnabled = !mediaLibrary.IsDuplicateScanBusy;
                cancelDuplicateScan.Visibility = mediaLibrary.IsDuplicateScanBusy ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        mediaLibrary.PropertyChanged += statusChanged;
        root.Children.Add(new Border { Background = Surface, Padding = new Thickness(14, 10),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { loadRing, summary } } });

        var layout = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var detail = new StackPanel { Spacing = 10, Padding = new Thickness(14) };
        var selectedText = new TextBlock { Text = "Select a media reference to inspect its source and folder.", TextWrapping = TextWrapping.Wrap };
        var nativeSelectedText = new TextBlock { Text = "Select audio to see where it is used.", TextWrapping = TextWrapping.Wrap };
        var nativePath = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            MinHeight = 40, MaxHeight = 86, PlaceholderText = "Audio location" };
        Button? nativePreview = null;
        NativeSampleCandidateRow? selectedNativeSample = null;
        var preview = Button("Preview", () =>
        {
            if (selectedSample is { } row) _ = mediaLibrary.PreviewSampleAsync(row);
            else if (selectedNativeSample is { } candidate) _ = mediaLibrary.PrepareNativeSamplePreviewCommand.ExecuteAsync(candidate);
        });
        preview.IsEnabled = false;
        var export = Button("Export copy…", () =>
        {
            if (selectedSample is { } row) _ = mediaLibrary.ExportSampleAsync(row);
        });
        export.IsEnabled = false;
        var collect = Button("Collect project media…", () =>
        {
            if (selectedSample is { } row) _ = mediaLibrary.CollectProjectMediaAsync(row);
        });
        collect.IsEnabled = false;
        void ShowSample(SampleUseRow? row)
        {
            mediaLibrary.StopPreview();
            selectedNativeSample = null;
            selectedSample = row;
            if (row is not null)
            {
                preview.IsEnabled = true;
                export.IsEnabled = true;
                collect.IsEnabled = true;
                selectedText.Text = $"{row.TrackName} · {row.ProjectName}\n{row.UseCount:N0} project use{(row.UseCount == 1 ? "" : "s")} · {row.FileType}";
            }
            else { preview.IsEnabled = false; export.IsEnabled = false; collect.IsEnabled = false; selectedText.Text = "Select a media reference to inspect its source and folder."; }
        }
        void ShowNativeSampleCandidate(NativeSampleCandidateRow? row)
        {
            mediaLibrary.StopPreview();
            selectedSample = null;
            selectedNativeSample = row;
            preview.IsEnabled = row is not null && !mediaLibrary.IsBusy;
            export.IsEnabled = collect.IsEnabled = false;
            nativeSelectedText.Text = row is null
                ? "Select audio to see where it is used."
                : $"{row.TrackName} · {row.ProjectName}\n{row.FileType} audio file";
            nativePath.Text = row?.DeclaredPath ?? string.Empty;
            if (nativePreview is not null) nativePreview.IsEnabled = row is not null && !mediaLibrary.IsBusy;
        }
        PropertyChangedEventHandler actionBusyChanged = (_, args) =>
        {
            if (args.PropertyName != nameof(mediaLibrary.IsBusy)) return;
            preview.IsEnabled = (selectedSample is not null || selectedNativeSample is not null) && !mediaLibrary.IsBusy;
            export.IsEnabled = collect.IsEnabled = selectedSample is not null && !mediaLibrary.IsBusy;
        };
        mediaLibrary.PropertyChanged += actionBusyChanged;
        detail.Children.Add(new TextBlock { Text = "Selected media", Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] });
        detail.Children.Add(selectedText);
        var mediaActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        mediaActions.Children.Add(preview); mediaActions.Children.Add(export);
        detail.Children.Add(mediaActions);
        detail.Children.Add(collect);
        var player = new MediaPlayerElement { AreTransportControlsEnabled = true, AutoPlay = false,
            MinHeight = 180, MaxHeight = 220, Visibility = Visibility.Collapsed };
        var waveformSummary = new TextBlock { Text = mediaLibrary.WaveformSummary, FontSize = 10,
            Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        var waveform = new Canvas { Width = 760, Height = 144, Background = Surface,
            HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(waveform, "Selected audio waveform; click to seek");
        var waveformViewport = new ScrollViewer { Content = waveform, MaxHeight = 150,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        MediaPlayerElement? nativePlayer = null;
        void DrawWaveform()
        {
            waveform.Children.Clear();
            var data = mediaLibrary.Waveform;
            if (data is null || data.PeakLevels.Count == 0)
            {
                waveform.Children.Add(new TextBlock { Text = mediaLibrary.WaveformSparkline, Foreground = Muted,
                    FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                return;
            }
            const double center = 72, margin = 8;
            waveform.Children.Add(new Line { X1 = margin, X2 = waveform.Width - margin, Y1 = center, Y2 = center,
                Stroke = Divider, StrokeThickness = 1 });
            var binWidth = (waveform.Width - margin * 2) / data.PeakLevels.Count;
            for (var index = 0; index < data.PeakLevels.Count; index++)
            {
                var height = Math.Max(2, data.PeakLevels[index] * 58);
                var bar = new Rectangle { Width = Math.Max(1.5, binWidth * .68), Height = height * 2,
                    Fill = Accent, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(bar, margin + index * binWidth + (binWidth - bar.Width) / 2);
                Canvas.SetTop(bar, center - height);
                waveform.Children.Add(bar);
            }
        }
        waveform.PointerPressed += (_, args) =>
        {
            if (mediaLibrary.Waveform is not { DurationSeconds: > 0 } data) return;
            var x = args.GetCurrentPoint(waveform).Position.X;
            var position = TimeSpan.FromSeconds(Math.Clamp(x / waveform.Width, 0, 1) * data.DurationSeconds);
            foreach (var mediaPlayer in new[] { player.MediaPlayer, nativePlayer?.MediaPlayer })
                if (mediaPlayer is not null) mediaPlayer.PlaybackSession.Position = position;
        };
        PropertyChangedEventHandler previewChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(mediaLibrary.PreviewUri))
            {
                if (mediaLibrary.PreviewUri is { } uri)
                {
                    player.Source = MediaSource.CreateFromUri(uri);
                    player.Visibility = Visibility.Visible;
                    if (nativePlayer is not null)
                    {
                        nativePlayer.Source = MediaSource.CreateFromUri(uri);
                        nativePlayer.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    player.Source = null; player.Visibility = Visibility.Collapsed;
                    if (nativePlayer is not null) { nativePlayer.Source = null; nativePlayer.Visibility = Visibility.Collapsed; }
                }
            }
            if (args.PropertyName is nameof(mediaLibrary.WaveformSparkline) or nameof(mediaLibrary.Waveform))
            {
                DrawWaveform();
            }
            if (args.PropertyName == nameof(mediaLibrary.WaveformSummary)) waveformSummary.Text = mediaLibrary.WaveformSummary;
        };
        mediaLibrary.PropertyChanged += previewChanged;
        DrawWaveform();
        detail.Children.Add(waveformSummary);
        detail.Children.Add(waveformViewport);
        detail.Children.Add(player);
        var sampleColumns = new DataBrowserColumn<SampleUseRow>[]
        {
            new("Audio file", "DeclaredPath", row => System.IO.Path.GetFileName(row.DeclaredPath), row => row.DeclaredPath),
            new("Track", "TrackName", row => row.TrackName, row => row.TrackName),
            new("Project", "ProjectName", row => row.ProjectName, row => row.ProjectName),
            new("Format · source", "FileType", row => row.FileType + " · " + row.Source, row => row.FileType),
            new("Folder", "Folder", row => row.Folder, row => row.Folder)
        };
        var sampleBrowser = new DataBrowserControl<SampleUseRow>(sampleColumns, mediaLibrary.Samples,
            ["None", "Project", "Format", "Folder", "Source"], "None",
            (row, mode) => mode switch { "Project" => row.ProjectName, "Format" => row.FileType,
                "Folder" => row.Folder, "Source" => row.Source, _ => "" }, _ => { },
            row => { ShowSample(row); return Task.CompletedTask; },
            row =>
            {
                var menu = new MenuFlyout();
                foreach (var (label, action) in new (string, Action)[]
                         { ("Preview", () => _ = mediaLibrary.PreviewSampleAsync(row)),
                           ("Export copy…", () => _ = mediaLibrary.ExportSampleAsync(row)),
                           ("Collect project media…", () => _ = mediaLibrary.CollectProjectMediaAsync(row)) })
                {
                    var item = new MenuFlyoutItem { Text = label };
                    item.Click += (_, _) => action();
                    menu.Items.Add(item);
                }
                return menu;
            });
        Grid.SetColumn(detail, 1);
        layout.Children.Add(sampleBrowser); layout.Children.Add(detail);
        root.Children.Add(layout);
        var nativeSampleColumns = new DataBrowserColumn<NativeSampleCandidateRow>[]
        {
            new("Audio file", "DeclaredPath", row => System.IO.Path.GetFileName(row.DeclaredPath), row => row.DeclaredPath, 2.2),
            new("Track", "TrackName", row => row.TrackName, row => row.TrackName, 1.1),
            new("Project", "ProjectName", row => row.ProjectName, row => row.ProjectName, 1.1),
            new("Type", "FileType", row => row.FileType, row => row.FileType, .6)
        };
        var nativeSampleBrowser = new DataBrowserControl<NativeSampleCandidateRow>(nativeSampleColumns,
            mediaLibrary.NativeSampleCandidates, ["None", "Project", "File type"], "None",
            (row, mode) => mode switch { "Project" => row.ProjectName, "File type" => row.FileType, _ => "" },
            _ => { }, row => { ShowNativeSampleCandidate(row); return Task.CompletedTask; },
            row =>
            {
                var menu = new MenuFlyout();
                var item = new MenuFlyoutItem { Text = "Preview" };
                item.Click += (_, _) =>
                {
                    ShowNativeSampleCandidate(row);
                    _ = mediaLibrary.PrepareNativeSamplePreviewCommand.ExecuteAsync(row);
                };
                menu.Items.Add(item);
                return menu;
            }, minimumRowsHeight: 260, maximumRowsHeight: 560);
        AutomationProperties.SetName(nativeSampleBrowser, "Audio files found in native project data");
        var nativePathsPanel = new Grid { ColumnSpacing = 12 };
        nativePathsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        nativePathsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nativeDetails = new StackPanel { Background = Surface, Padding = new Thickness(14), Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Selected audio", Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] },
                nativeSelectedText,
                nativePath
            } };
        nativePreview = Button("Preview", () =>
        {
            if (selectedNativeSample is { } candidate)
                _ = mediaLibrary.PrepareNativeSamplePreviewCommand.ExecuteAsync(candidate);
        });
        nativePreview.IsEnabled = false;
        PropertyChangedEventHandler nativePreviewStateChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(mediaLibrary.IsBusy))
                nativePreview!.IsEnabled = selectedNativeSample is not null && !mediaLibrary.IsBusy;
        };
        mediaLibrary.PropertyChanged += nativePreviewStateChanged;
        nativeDetails.Children.Add(nativePreview);
        var nativeEvidence = InfoButton(() =>
        {
            var evidence = new StackPanel { Spacing = 10 };
            if (selectedNativeSample is { } row)
            {
                evidence.Children.Add(Note("The project stores this audio path inside a Cubase audio event. The reader found it in an audio-track record. Track ownership and clip placement have not been confirmed."));
                evidence.Children.Add(DetailCard("SOURCE DETAILS", ("Project", row.ProjectPath), ("Track label", row.TrackName),
                    ("Class", row.EvidenceSource), ("File offset", $"0x{row.FileOffset:X}"),
                    ("Track record offset", row.CandidateTrackOffset is long offset ? $"0x{offset:X}" : "Not available"),
                    ("Reader note", row.Warning)));
            }
            else evidence.Children.Add(Empty("Select an audio file first."));
            ShowInformationWindow("Audio file details", evidence, 900, 620);
        }, "More about this audio reference");
        nativeDetails.Children.Add(nativeEvidence);
        nativePlayer = new MediaPlayerElement { AreTransportControlsEnabled = true, AutoPlay = false,
            MinHeight = 180, MaxHeight = 220, Visibility = Visibility.Collapsed };
        var nativeWaveformSummary = new TextBlock { Text = mediaLibrary.WaveformSummary, FontSize = 10,
            Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        var nativeWaveform = new Canvas { Width = 760, Height = 144, Background = Surface,
            HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(nativeWaveform, "Selected audio waveform; click to seek");
        var nativeWaveformViewport = new ScrollViewer { Content = nativeWaveform, MaxHeight = 150,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        void DrawNativeWaveform()
        {
            nativeWaveform.Children.Clear();
            if (mediaLibrary.Waveform is not { PeakLevels.Count: > 0 } data)
            {
                nativeWaveform.Children.Add(new TextBlock { Text = mediaLibrary.WaveformSparkline, Foreground = Muted,
                    FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                return;
            }
            const double center = 72, margin = 8;
            nativeWaveform.Children.Add(new Line { X1 = margin, X2 = nativeWaveform.Width - margin,
                Y1 = center, Y2 = center, Stroke = Divider, StrokeThickness = 1 });
            var binWidth = (nativeWaveform.Width - margin * 2) / data.PeakLevels.Count;
            for (var index = 0; index < data.PeakLevels.Count; index++)
            {
                var height = Math.Max(2, data.PeakLevels[index] * 58);
                var bar = new Rectangle { Width = Math.Max(1.5, binWidth * .68), Height = height * 2,
                    Fill = Accent, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(bar, margin + index * binWidth + (binWidth - bar.Width) / 2);
                Canvas.SetTop(bar, center - height);
                nativeWaveform.Children.Add(bar);
            }
        }
        nativeWaveform.PointerPressed += (_, args) =>
        {
            if (mediaLibrary.Waveform is not { DurationSeconds: > 0 } data) return;
            var x = args.GetCurrentPoint(nativeWaveform).Position.X;
            if (nativePlayer is { } selectedPlayer)
                selectedPlayer.MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(
                    Math.Clamp(x / nativeWaveform.Width, 0, 1) * data.DurationSeconds);
        };
        mediaLibrary.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(mediaLibrary.Waveform) or nameof(mediaLibrary.WaveformSparkline)) DrawNativeWaveform();
            if (args.PropertyName == nameof(mediaLibrary.WaveformSummary)) nativeWaveformSummary.Text = mediaLibrary.WaveformSummary;
        };
        DrawNativeWaveform();
        nativeDetails.Children.Add(nativeWaveformSummary);
        nativeDetails.Children.Add(nativeWaveformViewport);
        nativeDetails.Children.Add(nativePlayer);
        var nativePathsLeft = new StackPanel { Spacing = 8,
            Children =
            {
                Note("Choose a file to preview it. Click its waveform to move through the recording."),
                nativeSampleBrowser
            } };
        var nativeEmpty = Empty("No additional audio references were found. Select Analyze to re-read the project and refresh its details.");
        nativeEmpty.Visibility = mediaLibrary.NativeSampleCandidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        nativePathsLeft.Children.Insert(1, nativeEmpty);
        nativePathsPanel.Children.Add(nativePathsLeft);
        Grid.SetColumn(nativeDetails, 1);
        nativePathsPanel.Children.Add(nativeDetails);
        NotifyCollectionChangedEventHandler nativeSamplesChanged = (_, _) =>
            nativeEmpty.Visibility = mediaLibrary.NativeSampleCandidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        mediaLibrary.NativeSampleCandidates.CollectionChanged += nativeSamplesChanged;
        var duplicateSection = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        var duplicateList = new ListView { ItemsSource = mediaLibrary.IdenticalSampleGroups,
            SelectionMode = ListViewSelectionMode.Single, MaxHeight = 260 };
        AutomationProperties.SetName(duplicateList, "Byte-identical external audio groups");
        duplicateList.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Padding="8,5" Spacing="2">
                <TextBlock Text="{Binding Sha256}" FontFamily="Consolas" FontSize="10" TextTrimming="CharacterEllipsis"/>
                <TextBlock Text="{Binding SizeSummary}" FontSize="10"/>
                <TextBlock Text="{Binding SourceCount}" FontSize="10"/>
              </StackPanel>
            </DataTemplate>
            """);
        var duplicateDetails = new TextBlock { Text = "Select a group to inspect its source folders and project uses.",
            FontSize = 10, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        duplicateList.SelectionChanged += (_, _) =>
        {
            if (duplicateList.SelectedItem is MediaDuplicateGroup group)
            {
                duplicateDetails.Text = "Byte-identical files · SHA-256 " + group.Sha256 + Environment.NewLine +
                    string.Join(Environment.NewLine, group.Sources.Select(source =>
                        source.ResolvedPath + " · " + string.Join(", ", source.Uses.Select(use => use.ProjectName + " / " + use.TrackName))));
            }
            else duplicateDetails.Text = "Select a group to inspect its source folders and project uses.";
        };
        duplicateSection.Children.Add(duplicateActions);
        duplicateSection.Children.Add(duplicateStatus);
        var duplicateLayout = new Grid { ColumnSpacing = 12 };
        duplicateLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        duplicateLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        duplicateLayout.Children.Add(duplicateList);
        Grid.SetColumn(duplicateDetails, 1);
        duplicateLayout.Children.Add(duplicateDetails);
        duplicateSection.Children.Add(duplicateLayout);
        duplicateSection.Children.Add(Note("Only exact byte-identical files are grouped. This read-only audit does not move, replace, or delete files."));
        var initialTab = mediaLibrary.FocusNativeAudioTab ? 1 : 0;
        mediaLibrary.FocusNativeAudioTab = false;
        root.Children.Add(DashboardTabs(initialTab,
            ("Project media", layout),
            ("Audio paths", nativePathsPanel),
            ("Duplicates", duplicateSection),
            ("Analyze", MediaIndexActivityPanel())));
        root.Unloaded += (_, _) =>
        {
            mediaLibrary.CancelDuplicateScan();
            mediaLibrary.StopPreview();
            mediaLibrary.PropertyChanged -= statusChanged;
            mediaLibrary.PropertyChanged -= actionBusyChanged;
            mediaLibrary.PropertyChanged -= nativePreviewStateChanged;
            mediaLibrary.PropertyChanged -= previewChanged;
            mediaLibrary.NativeSampleCandidates.CollectionChanged -= nativeSamplesChanged;
        };
        root.Loaded += (_, _) =>
        {
            if (activeProject is null)
            {
                mediaLibrary.SetSampleProjectScope(null);
                mediaLibrary.Status = "Open a project to see its audio. Choose Analyze to inspect one project or the whole library.";
            }
            else if (!string.Equals(mediaLibrary.IndexProjectPath, activeProject.FilePath, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(mediaLibrary.IndexStage, "Index ready", StringComparison.Ordinal))
                _ = mediaLibrary.LoadOpenProjectAsync(activeProject);
        };
        return root;
    }

    private UIElement MidiTracksPage()
    {
        var activeProject = shell.Workspace.Project;
        var root = Page("MIDI", activeProject is null
            ? "Open a project to browse its MIDI tracks and notes."
            : $"Browse MIDI tracks and notes in {activeProject.Name}.");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var search = FilterTextBox("Search project, part or pitch…", 300);
        search.Text = mediaLibrary.MidiSearch;
        search.TextChanged += (_, _) => mediaLibrary.MidiSearch = search.Text;
        actions.Children.Add(search); AddPageActions(root, actions);
        var browserPanel = new StackPanel { Spacing = 10 };
        var activityPanel = new StackPanel { Spacing = 10, Children = { MediaIndexActivityPanel() } };
        var summary = new TextBlock { Margin = new Thickness(0, 8, 0, 6), Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] };
        var loadRing = new ProgressRing { Width = 18, Height = 18, IsActive = mediaLibrary.IsBusy,
            Visibility = mediaLibrary.IsBusy ? Visibility.Visible : Visibility.Collapsed };
        void UpdateSummary() => summary.Text = mediaLibrary.MidiSummary;
        UpdateSummary();
        PropertyChangedEventHandler statusChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(mediaLibrary.Status) or nameof(mediaLibrary.MidiSummary)) UpdateSummary();
            if (args.PropertyName == nameof(mediaLibrary.IsBusy))
            {
                UpdateSummary();
                loadRing.IsActive = mediaLibrary.IsBusy;
                loadRing.Visibility = mediaLibrary.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        mediaLibrary.PropertyChanged += statusChanged;
        browserPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { loadRing, summary } });
        var layout = new Grid { ColumnSpacing = 12 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        MidiTrackLibraryRow? selectedTrack = null;
        var detail = new StackPanel { Spacing = 10, Padding = new Thickness(14) };
        detail.Children.Add(new TextBlock { Text = "Selected MIDI track", Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] });
        var info = new TextBlock { Text = "Select a track to inspect its export eligibility.", TextWrapping = TextWrapping.Wrap };
        var rollStatus = new TextBlock { Text = mediaLibrary.MidiPianoRollStatus, FontSize = 10,
            Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        var rollProgress = new ProgressRing { Width = 16, Height = 16, IsActive = mediaLibrary.IsMidiPreviewBusy,
            Visibility = mediaLibrary.IsMidiPreviewBusy ? Visibility.Visible : Visibility.Collapsed };
        var rollCanvas = new Canvas { Width = 760, Height = 180, Background = Surface };
        var rollScroll = new ScrollViewer { Content = rollCanvas, MaxHeight = 320,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void DrawRoll()
        {
            rollCanvas.Children.Clear();
            var preview = mediaLibrary.MidiPianoRoll;
            if (preview is null)
            {
                rollCanvas.Visibility = Visibility.Collapsed;
                return;
            }
            rollCanvas.Visibility = Visibility.Visible;
            const double left = 42, top = 8, bottom = 22, rowHeight = 12;
            var timelineWidth = rollCanvas.Width - left - 8;
            var timeline = Math.Max(0.000001, preview.End - preview.Start);
            var pitchRows = Math.Max(1, preview.HighestKey - preview.LowestKey + 1);
            rollCanvas.Height = top + pitchRows * rowHeight + bottom;
            for (var key = preview.LowestKey; key <= preview.HighestKey; key++)
            {
                var y = top + (preview.HighestKey - key) * rowHeight;
                var line = new Line { X1 = left, X2 = rollCanvas.Width, Y1 = y + rowHeight, Y2 = y + rowHeight,
                    Stroke = Divider, StrokeThickness = 1, Opacity = key % 12 == 0 ? 0.65 : 0.22 };
                rollCanvas.Children.Add(line);
                if (key % 12 == 0)
                {
                    var label = new TextBlock { Text = "C" + (key / 12 - 1), FontSize = 9, Foreground = Muted };
                    Canvas.SetLeft(label, 2); Canvas.SetTop(label, y - 1); rollCanvas.Children.Add(label);
                }
            }
            var tickStep = Math.Max(1d, timeline / 24d);
            for (var tick = Math.Ceiling(preview.Start / tickStep) * tickStep; tick < preview.End; tick += tickStep)
            {
                var x = left + (tick - preview.Start) / timeline * timelineWidth;
                if (x < left || x > rollCanvas.Width) continue;
                var line = new Line { X1 = x, X2 = x, Y1 = top, Y2 = rollCanvas.Height - bottom,
                    Stroke = Divider, StrokeThickness = 1, Opacity = 0.35 };
                rollCanvas.Children.Add(line);
                var label = new TextBlock { Text = tick.ToString("0.##"), FontSize = 9, Foreground = Muted };
                Canvas.SetLeft(label, x + 2); Canvas.SetTop(label, rollCanvas.Height - bottom + 2); rollCanvas.Children.Add(label);
            }
            foreach (var note in preview.VisibleNotes)
            {
                var noteRect = new Rectangle { Width = Math.Max(3, note.Duration / timeline * timelineWidth),
                    Height = rowHeight - 2, Fill = Accent,
                    Opacity = 0.3 + 0.7 * Math.Clamp(note.Velocity ?? 0.7, 0, 1) };
                Canvas.SetLeft(noteRect, left + (note.Start - preview.Start) / timeline * timelineWidth);
                Canvas.SetTop(noteRect, top + (preview.HighestKey - note.Key) * rowHeight + 1);
                rollCanvas.Children.Add(noteRect);
            }
        }
        rollCanvas.Visibility = Visibility.Collapsed;
        var export = Button("Export Standard MIDI…", () =>
        {
            if (selectedTrack is { CanExport: true } row) _ = mediaLibrary.ExportMidiCommand.ExecuteAsync(row);
        });
        export.IsEnabled = false;
        async Task ShowMidiTrackAsync(MidiTrackLibraryRow row)
        {
            selectedTrack = row;
            info.Text = $"{row.Name} · {row.ProjectName}\n{row.Kind} · {row.NoteCount:N0} notes · {row.PitchRange} pitch range · {row.ChannelCount} channels · {row.TimeUnit}\n{(row.CanExport ? "Safe export available from verified track timing." : row.ExportLimitation)}";
            export.IsEnabled = row.CanExport && !mediaLibrary.IsBusy;
            await mediaLibrary.LoadMidiPianoRollAsync(row);
        }
        PropertyChangedEventHandler rollChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(mediaLibrary.IsBusy)) export.IsEnabled = selectedTrack?.CanExport == true && !mediaLibrary.IsBusy;
            if (args.PropertyName == nameof(mediaLibrary.MidiPianoRoll)) DrawRoll();
            if (args.PropertyName == nameof(mediaLibrary.MidiPianoRollStatus)) rollStatus.Text = mediaLibrary.MidiPianoRollStatus;
            if (args.PropertyName == nameof(mediaLibrary.IsMidiPreviewBusy))
            {
                rollProgress.IsActive = mediaLibrary.IsMidiPreviewBusy;
                rollProgress.Visibility = mediaLibrary.IsMidiPreviewBusy ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        mediaLibrary.PropertyChanged += rollChanged;
        detail.Children.Add(info); detail.Children.Add(export);
        detail.Children.Add(new TextBlock { Text = "Read-only piano roll · horizontal axis follows the declared timebase",
            Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] });
        detail.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { rollProgress, rollStatus } });
        detail.Children.Add(rollScroll);
        var midiColumns = new DataBrowserColumn<MidiTrackLibraryRow>[]
        {
            new("Track", "Name", row => row.Name, row => row.Name),
            new("Project", "ProjectName", row => row.ProjectName, row => row.ProjectName),
            new("Kind", "Kind", row => row.Kind, row => row.Kind),
            new("Notes · pitch", "NoteCount", row => $"{row.NoteCount:N0} · {row.PitchRange}", row => row.NoteCount),
            new("Timebase · export", "TimeUnit", row => row.TimeUnit + " · " + (row.CanExport ? "Ready" : "Unavailable"), row => row.TimeUnit)
        };
        var midiBrowser = new DataBrowserControl<MidiTrackLibraryRow>(midiColumns, mediaLibrary.MidiTracks,
            ["None", "Project", "Kind", "Timebase"], "None",
            (row, mode) => mode switch { "Project" => row.ProjectName, "Kind" => row.Kind,
                "Timebase" => row.TimeUnit, _ => "" }, _ => { },
            ShowMidiTrackAsync,
            row =>
            {
                var menu = new MenuFlyout();
                var exportItem = new MenuFlyoutItem { Text = "Export Standard MIDI…", IsEnabled = row.CanExport };
                exportItem.Click += async (_, _) => await mediaLibrary.ExportMidiCommand.ExecuteAsync(row);
                menu.Items.Add(exportItem);
                return menu;
            });
        Grid.SetColumn(detail, 1); layout.Children.Add(midiBrowser); layout.Children.Add(detail);
        var nativePanel = NativeMidiPanel(out var cleanupNative);
        var source = new ComboBox { ItemsSource = new[] { "Native project notes", "Exported MIDI" },
            SelectedIndex = mediaLibrary.NativeMidiGroups.Count == 0 && mediaLibrary.MidiTracks.Count > 0 ? 1 : 0,
            MinWidth = 190, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(source, "MIDI data source");
        var sourceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        sourceRow.Children.Add(new TextBlock { Text = "Source", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        sourceRow.Children.Add(source);
        var exportedEmpty = Empty("No exported MIDI tracks match this view. Choose Native project notes to browse note information found in Cubase files.");
        browserPanel.Children.Add(sourceRow); browserPanel.Children.Add(nativePanel); browserPanel.Children.Add(layout); browserPanel.Children.Add(exportedEmpty);
        var initialSourceChosen = mediaLibrary.NativeMidiGroups.Count + mediaLibrary.MidiTracks.Count > 0;
        void UpdateSource()
        {
            if (!initialSourceChosen && mediaLibrary.NativeMidiGroups.Count + mediaLibrary.MidiTracks.Count > 0)
            {
                initialSourceChosen = true;
                source.SelectedIndex = mediaLibrary.NativeMidiGroups.Count > 0 ? 0 : 1;
            }
            var native = source.SelectedIndex == 0;
            nativePanel.Visibility = native ? Visibility.Visible : Visibility.Collapsed;
            layout.Visibility = !native && mediaLibrary.MidiTracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            exportedEmpty.Visibility = !native && mediaLibrary.MidiTracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!native && mediaLibrary.MidiTracks.Count > 0 && selectedTrack is null)
            {
                var first = mediaLibrary.MidiTracks[0]; midiBrowser.SelectItem(first); _ = ShowMidiTrackAsync(first);
            }
        }
        source.SelectionChanged += (_, _) => UpdateSource();
        PropertyChangedEventHandler sourceChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(mediaLibrary.MidiSummary) or nameof(mediaLibrary.IsBusy)) UpdateSource();
        };
        mediaLibrary.PropertyChanged += sourceChanged;
        root.Children.Add(DashboardTabs(("MIDI browser", browserPanel), ("Analysis", activityPanel)));
        root.Loaded += (_, _) =>
        {
            if (activeProject is null)
                mediaLibrary.Status = "Open a project to see its MIDI. Choose Analyze to inspect one project or the whole library.";
            else if (!string.Equals(mediaLibrary.IndexProjectPath, activeProject.FilePath, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(mediaLibrary.IndexStage, "Index ready", StringComparison.Ordinal))
                _ = mediaLibrary.LoadOpenProjectAsync(activeProject);
        };
        UpdateSource();
        root.Unloaded += (_, _) =>
        {
            mediaLibrary.PropertyChanged -= statusChanged;
            mediaLibrary.PropertyChanged -= rollChanged;
            mediaLibrary.PropertyChanged -= sourceChanged;
            cleanupNative();
            mediaLibrary.ClearMidiPianoRoll();
        };
        return root;
    }
}
