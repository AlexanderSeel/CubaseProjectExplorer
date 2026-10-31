using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement StudioIoView(ProjectSnapshot project)
    {
        var diagram = StudioIoDiagramBuilder.Build(project);
        var nodes = diagram.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var root = new StackPanel { Spacing = 12 };
        var metrics = new Grid { ColumnSpacing = 12 };
        for (var index = 0; index < 3; index++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
        var values = new[] { ("SAVED PORTS", diagram.SavedPorts.ToString("N0")), ("AUDIO REFERENCES", diagram.AudioPaths.ToString("N0")), ("MIDI NOTES", diagram.MidiNotes.ToString("N0")) };
        for (var index = 0; index < values.Length; index++)
        {
            var metric = Metric(values[index].Item1, values[index].Item2);
            Grid.SetColumn(metric, index); metrics.Children.Add(metric);
        }
        root.Children.Add(metrics);
        var search = FilterTextBox("Find a port, channel, audio file or MIDI stream", 320);
        var picker = new ComboBox { DisplayMemberPath = nameof(StudioIoNode.Name), Width = 300, PlaceholderText = "Select a studio node" };
        AutomationProperties.SetName(picker, "Studio diagram selected node");
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { search, picker } };
        root.Children.Add(toolbar);
        var canvas = new Canvas { Width = 1040, Height = 510 };
        AutomationProperties.SetName(canvas, "Studio patchbay showing selected node and its saved input and output relationships");
        var viewport = new ScrollViewer { Content = canvas, Height = 530, ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = .45f, MaxZoomFactor = 1.5f, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new Border { BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = viewport });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 };
        root.Children.Add(status);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        navigation.Children.Add(Button("Open routing view", () => navigate("Routing")));
        navigation.Children.Add(Button("Open MIDI workspace", async () =>
        {
            var load = mediaLibrary.LoadOpenProjectAsync(project);
            navigate("Midi");
            await load;
        }));
        navigation.Children.Add(Button("Find project audio", async () =>
        {
            var load = mediaLibrary.LoadOpenProjectAsync(project);
            mediaLibrary.SetSampleProjectScope(project.FilePath);
            navigate("Samples");
            await load;
        }));
        root.Children.Add(navigation);
        root.Children.Add(new TextBlock { Text = "Solid lines: declared routes. Dashed lines: saved port assignments or record references. Port and driver descriptions come from the saved file. The diagram includes up to 2,000 media/MIDI groups per section; open their workspace for filtering and detailed inspection.",
            Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        string? selectedId = null;
        var choosing = false;
        void Place(UIElement element, double x, double y)
        { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
        void ShowSource(StudioIoNode node)
        {
            var details = new StackPanel { Spacing = 12 };
            details.Children.Add(DetailCard(node.Kind.ToUpperInvariant(), ("Name", node.Name), ("Details", node.Description),
                ("Source", node.Source), ("Byte offset", node.SourceOffset is long offset ? $"0x{offset:X}" : "Declared source field")));
            if (node.StoredFields is { Count: > 0 }) details.Children.Add(Section("Saved values", node.StoredFields));
            details.Children.Add(Section("Saved relationships", diagram.Links.Where(link => link.From == node.Id || link.To == node.Id)
                .Select(link => $"{nodes.GetValueOrDefault(link.From)?.Name ?? link.From} → {nodes.GetValueOrDefault(link.To)?.Name ?? link.To} · {link.Label}\n{link.Source}" +
                    (link.SourceOffset is long sourceOffset ? $" · 0x{sourceOffset:X}" : ""))));
            ShowInformationWindow(node.Kind + " · " + node.Name, details, 920, 700);
        }
        Button NodeButton(StudioIoNode node)
        {
            var text = new StackPanel { Spacing = 3 };
            text.Children.Add(new TextBlock { Text = "●  " + node.Name, Foreground = Accent, FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = node.Kind + " · " + node.Description, Foreground = Muted, FontSize = 11,
                TextWrapping = TextWrapping.Wrap, MaxLines = 2 });
            var button = new Button { Content = text, Width = 285, Height = 76, Padding = new Thickness(12, 8, 12, 8),
                Background = Surface, BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
            ToolTipService.SetToolTip(button, node.Name + "\n" + node.Description);
            AutomationProperties.SetName(button, node.Kind + " " + node.Name);
            button.Click += (_, _) => Select(node.Id);
            return button;
        }
        void Wire(double x1, double y1, double x2, double y2, StudioIoLink link)
        {
            var figure = new PathFigure { StartPoint = new Point(x1, y1) };
            figure.Segments.Add(new BezierSegment { Point1 = new((x1 + x2) / 2, y1), Point2 = new((x1 + x2) / 2, y2), Point3 = new(x2, y2) });
            var geometry = new PathGeometry(); geometry.Figures.Add(figure);
            var path = new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = Accent, StrokeThickness = 2 };
            if (link.Label != "Output") path.StrokeDashArray = new DoubleCollection { 4, 3 };
            ToolTipService.SetToolTip(path, link.Label + " · " + link.Source);
            canvas.Children.Add(path);
            Place(new Polygon { Fill = Accent, Points = new PointCollection { new(0, 0), new(7, 4), new(0, 8) } }, x2 - 7, y2 - 4);
        }
        void Fit()
        {
            if (viewport.ActualWidth > 0) viewport.ChangeView(null, null, (float)Math.Clamp((viewport.ActualWidth - 20) / canvas.Width, .45, 1), true);
        }
        void Select(string id)
        {
            if (!nodes.TryGetValue(id, out var selected)) return;
            selectedId = id;
            choosing = true; picker.SelectedItem = selected; choosing = false;
            canvas.Children.Clear();
            var incoming = diagram.Links.Where(link => link.To == id && nodes.ContainsKey(link.From)).ToArray();
            var outgoing = diagram.Links.Where(link => link.From == id && nodes.ContainsKey(link.To)).ToArray();
            var rows = Math.Max(Math.Min(5, incoming.Length), Math.Min(5, outgoing.Length));
            canvas.Height = Math.Max(510, rows * 88 + 95);
            var middleY = canvas.Height / 2;
            Place(new TextBlock { Text = "INPUTS / REFERENCES", Foreground = Muted, FontSize = 11 }, 18, 18);
            Place(new TextBlock { Text = "SELECTED STUDIO NODE", Foreground = Muted, FontSize = 11 }, 375, 18);
            Place(new TextBlock { Text = "OUTPUTS / DESTINATIONS", Foreground = Muted, FontSize = 11 }, 737, 18);
            for (var index = 0; index < Math.Min(5, incoming.Length); index++)
            {
                var y = middleY - Math.Min(5, incoming.Length) * 44 + index * 88;
                Wire(303, y + 38, 370, middleY, incoming[index]);
                Place(NodeButton(nodes[incoming[index].From]), 18, y);
            }
            for (var index = 0; index < Math.Min(5, outgoing.Length); index++)
            {
                var y = middleY - Math.Min(5, outgoing.Length) * 44 + index * 88;
                Wire(670, middleY, 737, y + 38, outgoing[index]);
                Place(NodeButton(nodes[outgoing[index].To]), 737, y);
            }
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(new TextBlock { Text = selected.Name, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = selected.Kind, FontSize = 12, Foreground = Accent });
            body.Children.Add(new TextBlock { Text = selected.Description, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, MaxLines = 5 });
            body.Children.Add(Button("Saved values & connections…", () => ShowSource(selected)));
            Place(new Border { Width = 300, MinHeight = 240, Padding = new Thickness(16), Background = Surface,
                BorderBrush = Accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(9), Child = body }, 370, middleY - 120);
            if (incoming.Length == 0) Place(new TextBlock { Text = "No incoming assignment decoded.\nChoose another node to inspect its saved relationships.",
                Width = 280, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Muted }, 18, middleY - 28);
            if (outgoing.Length == 0) Place(new TextBlock { Text = "No outgoing assignment decoded.\nStored values remain available.",
                Width = 280, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Muted }, 737, middleY - 28);
            status.Text = $"{diagram.Nodes.Count:N0} studio nodes · {diagram.Links.Count:N0} saved relationships · selected: {incoming.Length:N0} inputs, {outgoing.Length:N0} outputs" +
                (incoming.Length > 5 || outgoing.Length > 5 ? " · first five per side shown; open all connections to inspect the rest" : "");
            Fit();
        }
        void Filter()
        {
            var query = search.Text.Trim();
            var options = diagram.Nodes.Where(node => query.Length == 0 || (node.Name + " " + node.Kind + " " + node.Description).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            choosing = true; picker.ItemsSource = options; choosing = false;
            var next = options.FirstOrDefault(node => node.Id == selectedId) ?? options.FirstOrDefault(node => node.Kind == "Channel") ?? options.FirstOrDefault();
            if (next is not null) Select(next.Id);
            else { canvas.Children.Clear(); status.Text = "No studio nodes match. Clear the search or choose another project."; }
        }
        search.TextChanged += (_, _) => Filter();
        picker.SelectionChanged += (_, _) => { if (!choosing && picker.SelectedItem is StudioIoNode node) Select(node.Id); };
        viewport.SizeChanged += (_, _) => Fit();
        root.SizeChanged += (_, args) =>
        {
            toolbar.Orientation = navigation.Orientation = args.NewSize.Width < 700 ? Orientation.Vertical : Orientation.Horizontal;
            viewport.Height = args.NewSize.Width < 700 ? 400 : 530;
        };
        Filter();
        return root;
    }
}
