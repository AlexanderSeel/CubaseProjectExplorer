using System.Collections.Specialized;
using System.ComponentModel;
using CubaseInspector.App.ViewModels;
using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement NativeMidiPanel(out Action cleanup)
    {
        var host = new StackPanel { Spacing = 12 };
        var layout = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var noteRows = new ResettableObservableCollection<IndexedMidiNoteCandidate>();
        NativeMidiTrackGroup? selected = null;
        var compactPicker = new ComboBox { ItemsSource = mediaLibrary.NativeMidiTrackGroups,
            DisplayMemberPath = nameof(NativeMidiTrackGroup.DisplayName),
            HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed,
            PlaceholderText = "Choose a project or part", Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(compactPicker, "Select native MIDI project or part");
        var title = new TextBlock { Text = "Notes found in native projects", FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var context = new TextBlock { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var plot = new MidiNotePlot();
        var timing = new TextBlock { Text = "Stored note positions · source details explain timing and part context",
            Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var evidence = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var partPicker = new ComboBox { DisplayMemberPath = nameof(NativeMidiNoteGroup.DisplayName),
            PlaceholderText = "Choose a part", MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(partPicker, "MIDI parts in this track grouping");
        var evidencePanel = new StackPanel { Spacing = 8 };
        evidencePanel.Children.Add(evidence);
        var selectedNoteEvidence = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        evidencePanel.Children.Add(selectedNoteEvidence);
        evidencePanel.Children.Add(new TextBlock { Text = "Values in the table are rounded for readability. Original precision and byte offsets remain in the saved project evidence. Native observations are not yet available for Standard MIDI export.",
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var evidenceButton = InfoButton(() => ShowInformationWindow("Native MIDI evidence & source", evidencePanel),
            "Technical details · MIDI source records");
        var selectedNote = new TextBlock { Foreground = Muted, FontSize = 11,
            TextWrapping = TextWrapping.Wrap, Text = "Select a note to see its musical details." };
        var columns = new DataBrowserColumn<IndexedMidiNoteCandidate>[]
        {
            new("Pitch", "Pitch", note => note.Pitch.ToString(), note => note.Pitch, 0.7),
            new("Position", "Position", note => NativeMidiNoteGroup.Format(note.PositionTicks), note => note.PositionTicks, 1.25),
            new("Length", "Length", note => NativeMidiNoteGroup.Format(note.DurationTicks), note => note.DurationTicks, 1.25),
            new("Velocity", "Velocity", note => note.Velocity.ToString(), note => note.Velocity, 0.8),
            new("Channel", "Channel", note => note.Channel is int channel ? (channel + 1).ToString() : "Unknown", note => note.Channel, 0.8)
        };
        var notes = new DataBrowserControl<IndexedMidiNoteCandidate>(columns, noteRows, ["None"], "None",
            (_, _) => "", _ => { }, note =>
            {
                selectedNote.Text = $"Pitch {note.Pitch} · position {NativeMidiNoteGroup.Format(note.PositionTicks)} · length {NativeMidiNoteGroup.Format(note.DurationTicks)} · velocity {note.Velocity}" +
                    (note.Channel is int channel ? $" · channel {channel + 1}" : "");
                selectedNoteEvidence.Text = $"Saved note #{note.Observation} · byte offset 0x{note.FileOffset:X}" +
                    (note.RawFlags is ushort flags ? $" · raw flags 0x{flags:X4}" : "") + $" · {note.SourceLayout}";
                return Task.CompletedTask;
            }, _ => null, minimumRowsHeight: 160, maximumRowsHeight: 280, showToolbar: false);
        AutomationProperties.SetName(notes, "Notes found in Cubase project files. Select a note to see pitch, position, length and velocity.");
        var detail = new StackPanel { Spacing = 10 };
        detail.Children.Add(title); detail.Children.Add(context);
        detail.Children.Add(new Border { CornerRadius = new CornerRadius(6), BorderBrush = Divider,
            BorderThickness = new Thickness(1), Child = plot });
        detail.Children.Add(timing); detail.Children.Add(notes); detail.Children.Add(selectedNote);
        detail.Children.Add(evidenceButton);
        DataBrowserControl<NativeMidiTrackGroup>? groups = null;
        Task ShowPart(NativeMidiNoteGroup group)
        {
            title.Text = group.Name;
            context.Text = $"{group.ProjectName} · {group.Context}\n{group.NoteCount:N0} notes read · MIDI pitches {group.PitchRange}";
            noteRows.ReplaceAll(group.Notes.Take(2000));
            plot.SetPreview(NativeMidiBrowser.Preview(group));
            evidence.Text = group.SourceLayout + "\n\n" + (group.Notes.FirstOrDefault()?.Warning ?? "No source evidence available.");
            timing.Text = "Stored note positions · source details explain timing and part context" +
                (group.NoteCount > 2000 ? " · plot and table show the first 2,000 notes; narrow the project/part search to inspect another group" : "");
            selectedNote.Text = "Select a note to see its musical details.";
            selectedNoteEvidence.Text = string.Empty;
            return Task.CompletedTask;
        }
        Task ShowTrack(NativeMidiTrackGroup group)
        {
            selected = group;
            compactPicker.SelectedItem = group;
            title.Text = group.DisplayName;
            context.Text = $"{group.ProjectName} · {group.Context}\n{group.PartCount:N0} parts · {group.NoteCount:N0} notes";
            partPicker.ItemsSource = group.Parts;
            partPicker.SelectedItem = group.Parts.FirstOrDefault();
            return group.Parts.Count > 0 ? ShowPart(group.Parts[0]) : Task.CompletedTask;
        }
        partPicker.SelectionChanged += (_, _) =>
        {
            if (partPicker.SelectedItem is NativeMidiNoteGroup part) _ = ShowPart(part);
        };
        detail.Children.Insert(2, partPicker);
        var groupColumns = new DataBrowserColumn<NativeMidiTrackGroup>[]
        {
            new("Project / track", "DisplayName", group => group.DisplayName, group => group.DisplayName, 2.8),
            new("Parts", "PartCount", group => group.PartCount.ToString("N0"), group => group.PartCount, 0.6),
            new("Notes", "NoteCount", group => group.NoteCount.ToString("N0"), group => group.NoteCount, 0.7)
        };
        groups = new DataBrowserControl<NativeMidiTrackGroup>(groupColumns, mediaLibrary.NativeMidiTrackGroups,
            ["None", "Project", "Layout"], "None", (group, mode) => mode switch
            { "Project" => group.ProjectName, "Layout" => string.Join(", ", group.Parts.Select(part => part.SourceLayout).Distinct()), _ => "" }, _ => { },
            ShowTrack, _ => null, minimumRowsHeight: 240, maximumRowsHeight: 600);
        layout.Children.Add(groups); Grid.SetColumn(detail, 1); layout.Children.Add(detail);
        Grid.SetColumnSpan(compactPicker, 2); layout.Children.Add(compactPicker);
        compactPicker.SelectionChanged += (_, _) =>
        {
            if (compactPicker.SelectedItem is NativeMidiTrackGroup group && !ReferenceEquals(group, selected))
            { groups.SelectItem(group); _ = ShowTrack(group); }
        };
        var empty = Empty("No MIDI details are loaded yet. Select Analyze to read the project again and refresh its MIDI view.");
        host.Children.Add(empty); host.Children.Add(layout);
        void Refresh()
        {
            var hasRows = mediaLibrary.NativeMidiTrackGroups.Count > 0;
            layout.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
            empty.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
            if (empty.Child is TextBlock text) text.Text = mediaLibrary.IsIndexLoading ? "Follow the index activity above. Cancel to browse records read so far."
                : mediaLibrary.MidiSearch.Length > 0
                ? "No notes match this search. Try a project, track or part name, or MIDI pitch."
                : "No native note groups in this view. Load a saved scope, use the open project, or rebuild its index above.";
            var next = mediaLibrary.NativeMidiTrackGroups.FirstOrDefault(group => group.Key == selected?.Key)
                ?? mediaLibrary.NativeMidiTrackGroups.FirstOrDefault();
            if (next is not null) { groups.SelectItem(next); _ = ShowTrack(next); }
            else { selected = null; noteRows.Clear(); plot.SetPreview(null); }
        }
        NotifyCollectionChangedEventHandler changed = (_, _) => Refresh();
        mediaLibrary.NativeMidiTrackGroups.CollectionChanged += changed;
        PropertyChangedEventHandler busyChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(mediaLibrary.IsBusy)) Refresh();
        };
        mediaLibrary.PropertyChanged += busyChanged;
        SizeChangedEventHandler resized = (_, _) =>
        {
            var stacked = layout.ActualWidth < 1050;
            groups.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
            compactPicker.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(detail, stacked ? 0 : 1); Grid.SetRow(detail, stacked ? 1 : 0);
            Grid.SetColumnSpan(groups, stacked ? 2 : 1); Grid.SetColumnSpan(detail, stacked ? 2 : 1);
        };
        layout.SizeChanged += resized;
        Refresh();
        cleanup = () => { mediaLibrary.NativeMidiTrackGroups.CollectionChanged -= changed;
            mediaLibrary.PropertyChanged -= busyChanged; layout.SizeChanged -= resized; };
        return host;
    }
}
