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
        NativeMidiNoteGroup? selected = null;
        var compactPicker = new ComboBox { ItemsSource = mediaLibrary.NativeMidiGroups,
            DisplayMemberPath = nameof(NativeMidiNoteGroup.DisplayName),
            HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed,
            PlaceholderText = "Choose a project or part", Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(compactPicker, "Select native MIDI project or part");
        var title = new TextBlock { Text = "Native CPR notes", FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var context = new TextBlock { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var plot = new MidiNotePlot();
        var timing = new TextBlock { Text = "Raw stored positions · timing units and clip placement unverified",
            Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var evidence = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var evidencePanel = new StackPanel { Spacing = 8 };
        evidencePanel.Children.Add(evidence);
        evidencePanel.Children.Add(new TextBlock { Text = "Values in the table are rounded for readability. Original precision and byte offsets remain in JSON inspection output. Native observations cannot be exported as Standard MIDI.",
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var evidenceButton = new Button { Content = "Evidence & source…" };
        evidenceButton.Click += (_, _) => ShowInformationWindow("Native MIDI evidence & source", evidencePanel);
        var selectedNote = new TextBlock { Foreground = Muted, FontSize = 11,
            TextWrapping = TextWrapping.Wrap, Text = "Select a note to inspect its source offset." };
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
                selectedNote.Text = $"Note {note.Observation} · byte offset 0x{note.FileOffset:X}" +
                    (note.RawFlags is ushort flags ? $" · raw flags 0x{flags:X4}" : "") + $" · {note.SourceLayout}";
                return Task.CompletedTask;
            }, _ => null, minimumRowsHeight: 160, maximumRowsHeight: 280, showToolbar: false);
        AutomationProperties.SetName(notes, "Candidate MIDI note values. Timing units and ownership are unverified.");
        var detail = new StackPanel { Spacing = 10 };
        detail.Children.Add(title); detail.Children.Add(context);
        detail.Children.Add(new TextBlock { Text = "Candidate evidence · Standard MIDI export unavailable", FontSize = 11, Foreground = Muted });
        detail.Children.Add(new Border { CornerRadius = new CornerRadius(6), BorderBrush = Divider,
            BorderThickness = new Thickness(1), Child = plot });
        detail.Children.Add(timing); detail.Children.Add(notes); detail.Children.Add(selectedNote);
        detail.Children.Add(evidenceButton);
        DataBrowserControl<NativeMidiNoteGroup>? groups = null;
        Task ShowGroup(NativeMidiNoteGroup group)
        {
            selected = group;
            compactPicker.SelectedItem = group;
            title.Text = group.Name;
            var projectContext = group.Name == group.ProjectName ? group.Context : group.ProjectName + " · " + group.Context;
            context.Text = $"{projectContext}\n{group.NoteCount:N0} candidate notes · MIDI pitches {group.PitchRange}";
            noteRows.ReplaceAll(group.Notes);
            plot.SetPreview(NativeMidiBrowser.Preview(group));
            evidence.Text = group.SourceLayout + "\n\n" + (group.Notes.FirstOrDefault()?.Warning ?? "No source evidence available.");
            timing.Text = "Raw stored positions · timing units and clip placement unverified" +
                (group.NoteCount > 2000 ? " · plot shows the first 2,000 notes" : "");
            selectedNote.Text = "Select a note to inspect its source offset.";
            return Task.CompletedTask;
        }
        var groupColumns = new DataBrowserColumn<NativeMidiNoteGroup>[]
        {
            new("Project / part", "Name", group => group.Name, group => group.Name, 2.8),
            new("Possible track", "Context", group => group.CandidateTrackName ?? "Unassigned", group => group.Context, 1),
            new("Notes", "Count", group => group.NoteCount.ToString("N0"), group => group.NoteCount, 0.45)
        };
        groups = new DataBrowserControl<NativeMidiNoteGroup>(groupColumns, mediaLibrary.NativeMidiGroups,
            ["None", "Project", "Layout"], "None", (group, mode) => mode switch
            { "Project" => group.ProjectName, "Layout" => group.SourceLayout, _ => "" }, _ => { },
            ShowGroup, _ => null, minimumRowsHeight: 240, maximumRowsHeight: 600);
        layout.Children.Add(groups); Grid.SetColumn(detail, 1); layout.Children.Add(detail);
        Grid.SetColumnSpan(compactPicker, 2); layout.Children.Add(compactPicker);
        compactPicker.SelectionChanged += (_, _) =>
        {
            if (compactPicker.SelectedItem is NativeMidiNoteGroup group && !ReferenceEquals(group, selected))
            { groups.SelectItem(group); _ = ShowGroup(group); }
        };
        var empty = Empty("No native MIDI notes are indexed. Re-scan CPR/BAK projects in Project Library, then refresh this index.");
        host.Children.Add(empty); host.Children.Add(layout);
        void Refresh()
        {
            var hasRows = mediaLibrary.NativeMidiGroups.Count > 0;
            layout.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
            empty.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
            if (empty.Child is TextBlock text) text.Text = mediaLibrary.IsBusy ? "Loading native MIDI observations…"
                : mediaLibrary.MidiSearch.Length > 0
                ? "No native notes match this search. Try a project, possible track or part name, or MIDI pitch."
                : "No native MIDI notes are indexed. Re-scan CPR/BAK projects in Project Library, then refresh this index.";
            var next = mediaLibrary.NativeMidiGroups.FirstOrDefault(group => group.Key == selected?.Key)
                ?? mediaLibrary.NativeMidiGroups.FirstOrDefault();
            if (next is not null) { groups.SelectItem(next); _ = ShowGroup(next); }
            else { selected = null; noteRows.Clear(); plot.SetPreview(null); }
        }
        NotifyCollectionChangedEventHandler changed = (_, _) => Refresh();
        mediaLibrary.NativeMidiGroups.CollectionChanged += changed;
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
        cleanup = () => { mediaLibrary.NativeMidiGroups.CollectionChanged -= changed;
            mediaLibrary.PropertyChanged -= busyChanged; layout.SizeChanged -= resized; };
        return host;
    }
}
