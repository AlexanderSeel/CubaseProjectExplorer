using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace CubaseInspector.App;

/// <summary>Read-only pitch/position plot with a tabular alternative in the MIDI browser.</summary>
internal sealed class MidiNotePlot : Grid
{
    private readonly Canvas _canvas = new() { Height = 204 };
    private MidiPianoRollPreview? _preview;
    private Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public MidiNotePlot()
    {
        MinHeight = 204;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = Brush("InspectorSurfaceBrush");
        Children.Add(_canvas);
        SizeChanged += (_, _) => Draw();
        AutomationProperties.SetName(this, "Read-only MIDI pitch and stored-position plot. Exact note values are available in the notes table.");
    }

    public void SetPreview(MidiPianoRollPreview? preview)
    {
        _preview = preview;
        Draw();
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        if (_preview is not { } preview || ActualWidth < 100) return;
        var width = Math.Max(100, ActualWidth);
        _canvas.Width = width;
        const double left = 38, top = 8, bottom = 28, height = 204;
        var lowest = Math.Max(0, preview.LowestKey - 2);
        var highest = Math.Min(127, Math.Max(preview.HighestKey + 2, lowest + 11));
        var rowHeight = (height - top - bottom) / (highest - lowest + 1);
        var span = Math.Max(1, preview.End - preview.Start);
        var timelineWidth = width - left - 12;
        for (var pitch = lowest; pitch <= highest; pitch++)
        {
            var y = top + (highest - pitch) * rowHeight;
            var key = new Rectangle { Width = timelineWidth, Height = rowHeight,
                Fill = Brush("InspectorDividerBrush"), Opacity = pitch % 12 is 1 or 3 or 6 or 8 or 10 ? 0.12 : 0.035 };
            Canvas.SetLeft(key, left); Canvas.SetTop(key, y); _canvas.Children.Add(key);
            if (pitch == lowest || pitch == highest || pitch % 12 == 0)
            {
                var label = new TextBlock { Text = pitch.ToString(), FontSize = 10,
                    Foreground = Brush("InspectorTextSecondaryBrush") };
                Canvas.SetLeft(label, 6); Canvas.SetTop(label, y); _canvas.Children.Add(label);
            }
        }
        for (var index = 0; index <= 4; index++)
        {
            var value = preview.Start + index * span / 4;
            var x = left + index * timelineWidth / 4;
            _canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = height - bottom,
                Stroke = Brush("InspectorDividerBrush"), StrokeThickness = 1, Opacity = 0.5 });
            var label = new TextBlock { Text = NativeMidiNoteGroup.Format(value), FontSize = 10,
                Foreground = Brush("InspectorTextSecondaryBrush") };
            Canvas.SetLeft(label, Math.Min(x, width - 50)); Canvas.SetTop(label, height - bottom + 6);
            _canvas.Children.Add(label);
        }
        foreach (var note in preview.VisibleNotes)
        {
            var rectangle = new Rectangle { Width = Math.Min(timelineWidth,
                    Math.Max(3, note.Duration / span * timelineWidth)),
                Height = Math.Max(2, rowHeight - 2), RadiusX = 2, RadiusY = 2,
                Fill = Brush("InspectorAccentBrush"), Opacity = 0.45 + 0.55 * Math.Clamp(note.Velocity ?? 0.7, 0, 1) };
            Canvas.SetLeft(rectangle, left + (note.Start - preview.Start) / span * timelineWidth);
            Canvas.SetTop(rectangle, top + (highest - note.Key) * rowHeight + 1);
            ToolTipService.SetToolTip(rectangle, $"Pitch {note.Key} · position {NativeMidiNoteGroup.Format(note.Start)} · length {NativeMidiNoteGroup.Format(note.Duration)}");
            _canvas.Children.Add(rectangle);
        }
    }
}
