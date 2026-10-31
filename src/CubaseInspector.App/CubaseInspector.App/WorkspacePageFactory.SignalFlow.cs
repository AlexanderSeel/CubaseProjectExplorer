using System.Collections.ObjectModel;
using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement FocusedRoutingView(ProjectSnapshot project)
    {
        var channels = RoutingFocusBuilder.Channels(project);
        if (channels.Count == 0) return Empty("This export supplied no channel records or routing endpoints.");
        var selectedId = channels.FirstOrDefault(channel => Category(channel) == "Output")?.Id ?? channels[0].Id;
        var root = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var showChannels = new ToggleButton { Content = "Channels", IsChecked = true };
        var picker = new ComboBox { ItemsSource = channels, DisplayMemberPath = nameof(RoutingFocusChannel.Name), Width = 220 };
        AutomationProperties.SetName(picker, "Selected signal-flow channel");
        toolbar.Children.Add(showChannels); toolbar.Children.Add(picker);
        var viewport = new ScrollViewer { Height = 540, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = .5f, MaxZoomFactor = 1.5f };
        var canvas = new Canvas { Width = 1000, Height = 500 };
        AutomationProperties.SetName(canvas, "Feeds in, selected channel with devices, and outgoing destinations");
        viewport.Content = canvas;
        var autoFit = true;
        void Fit()
        {
            if (viewport.ActualWidth <= 0) return;
            var zoom = Math.Min((viewport.ActualWidth - 20) / canvas.Width, (viewport.ActualHeight - 10) / canvas.Height);
            viewport.ChangeView(null, null, (float)Math.Clamp(zoom, .5, 1), true);
        }
        toolbar.Children.Add(Button("Fit", () => { autoFit = true; Fit(); }));
        toolbar.Children.Add(Button("−", () => { autoFit = false; viewport.ChangeView(null, null, Math.Max(.5f, viewport.ZoomFactor - .1f)); }));
        toolbar.Children.Add(Button("+", () => { autoFit = false; viewport.ChangeView(null, null, Math.Min(1.5f, viewport.ZoomFactor + .1f)); }));
        toolbar.Children.Add(new TextBlock { Text = "Solid: output   ·   Dashed: send", FontSize = 11,
            Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(toolbar);
        const int pageSize = 10;
        var incomingPage = 0;
        var outgoingPage = 0;
        var incomingCount = 0;
        var outgoingCount = 0;
        var paging = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
        var pagingStyle = (Style)Application.Current.Resources["InspectorCommandButtonStyle"];
        var previousInputs = new Button { Content = "Previous inputs", Style = pagingStyle };
        var nextInputs = new Button { Content = "More inputs", Style = pagingStyle };
        var previousOutputs = new Button { Content = "Previous destinations", Style = pagingStyle };
        var nextOutputs = new Button { Content = "More destinations", Style = pagingStyle };
        paging.Children.Add(previousInputs); paging.Children.Add(nextInputs); paging.Children.Add(previousOutputs); paging.Children.Add(nextOutputs);
        root.Children.Add(paging);
        var workspace = new Grid { ColumnSpacing = 14 };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sidebar = new StackPanel { Spacing = 8 };
        var search = FilterTextBox("Find a channel", 184);
        var category = Combo("Channel kind", ["All", "Audio", "Instrument/MIDI", "Group", "FX", "Output", "Other", "Unresolved"], 184);
        var channelCount = new TextBlock { FontSize = 11, Foreground = Muted };
        var rows = new ObservableCollection<SignalFlowChannelRow>();
        var list = new ListView { ItemsSource = rows, SelectionMode = ListViewSelectionMode.Single,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Height = 450 };
        AutomationProperties.SetName(list, "Routing channels");
        list.ItemTemplate = new DataTemplate(() =>
        {
            var mark = new TextBlock { Text = "●", VerticalAlignment = VerticalAlignment.Center };
            mark.SetBinding(TextBlock.ForegroundProperty, new Binding { Path = new(nameof(SignalFlowChannelRow.Brush)) });
            var name = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetBinding(TextBlock.TextProperty, new Binding { Path = new(nameof(SignalFlowChannelRow.Name)) });
            var kind = new TextBlock { FontSize = 10, Foreground = Muted };
            kind.SetBinding(TextBlock.TextProperty, new Binding { Path = new(nameof(SignalFlowChannelRow.Kind)) });
            var line = new Grid { ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            var text = new StackPanel { Children = { name, kind } };
            line.Children.Add(mark); Grid.SetColumn(text, 1); line.Children.Add(text);
            line.SetBinding(ToolTipService.ToolTipProperty, new Binding { Path = new(nameof(SignalFlowChannelRow.Name)) });
            return line;
        });
        sidebar.Children.Add(search); sidebar.Children.Add(category); sidebar.Children.Add(channelCount); sidebar.Children.Add(list);
        workspace.Children.Add(sidebar);
        var detail = new StackPanel { Spacing = 8 };
        var path = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        detail.Children.Add(new ScrollViewer { Content = path, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        detail.Children.Add(viewport);
        var status = new TextBlock { Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        detail.Children.Add(status);
        var edgeRows = new ObservableCollection<string>();
        detail.Children.Add(new Expander { Header = "Connections as text", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new ListView { ItemsSource = edgeRows, SelectionMode = ListViewSelectionMode.None, MaxHeight = 250 } });
        Grid.SetColumn(detail, 1); workspace.Children.Add(detail); root.Children.Add(workspace);
        var selecting = false;
        void ToggleSidebar()
        {
            sidebar.Visibility = showChannels.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            workspace.ColumnDefinitions[0].Width = new GridLength(showChannels.IsChecked == true ? 184 : 0);
            if (autoFit) Fit();
        }
        var changingSidebarForSize = false;
        var userChangedSidebar = false;
        void SidebarChanged() { if (!changingSidebarForSize) userChangedSidebar = true; ToggleSidebar(); }
        showChannels.Checked += (_, _) => SidebarChanged(); showChannels.Unchecked += (_, _) => SidebarChanged();
        viewport.Loaded += (_, _) => Fit();
        viewport.SizeChanged += (_, _) => { if (autoFit) Fit(); };
        workspace.SizeChanged += (_, args) =>
        {
            if (!userChangedSidebar && args.NewSize.Width > 0)
            {
                changingSidebarForSize = true;
                showChannels.IsChecked = args.NewSize.Width >= 900;
                changingSidebarForSize = false;
            }
            if (root.XamlRoot is { } xamlRoot) viewport.Height = Math.Clamp(xamlRoot.Size.Height - 380, 280, 540);
        };
        void RefreshChannels()
        {
            var query = search.Text.Trim(); var selectedCategory = category.SelectedItem?.ToString() ?? "All";
            rows.Clear();
            foreach (var channel in channels.Where(channel => (query.Length == 0 || channel.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                (selectedCategory == "All" || Category(channel) == selectedCategory)))
                rows.Add(new(channel, ChannelBrush(channel)));
            channelCount.Text = $"{rows.Count:N0} of {channels.Count:N0} channels";
            list.SelectedItem = rows.FirstOrDefault(row => row.Channel.Id == selectedId);
        }
        void Select(string id)
        {
            if (selecting) return;
            selecting = true;
            try
            {
                if (selectedId != id) { incomingPage = 0; outgoingPage = 0; }
                selectedId = id;
                picker.SelectedItem = channels.FirstOrDefault(channel => channel.Id == id);
                list.SelectedItem = rows.FirstOrDefault(row => row.Channel.Id == id);
                var result = RoutingFocusBuilder.Build(project, id);
                if (!result.IsSuccess || result.Value is null) { status.Text = result.Error?.Message; return; }
                var focus = result.Value;
                canvas.Children.Clear(); path.Children.Clear(); edgeRows.Clear();
                path.Children.Add(new TextBlock { Text = "SIGNAL PATH", FontSize = 10, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
                foreach (var channel in focus.OutputPath)
                {
                    if (path.Children.Count > 1) path.Children.Add(new TextBlock { Text = "→", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
                    var targetId = channel.Id;
                    path.Children.Add(Button(channel.Name, () => Select(targetId)));
                }
                incomingCount = focus.Incoming.Count; outgoingCount = focus.Outgoing.Count;
                var incoming = focus.Incoming.Skip(incomingPage * pageSize).Take(pageSize).ToArray();
                var outgoing = focus.Outgoing.Skip(outgoingPage * pageSize).Take(pageSize).ToArray();
                paging.Visibility = incomingCount > pageSize || outgoingCount > pageSize ? Visibility.Visible : Visibility.Collapsed;
                previousInputs.IsEnabled = incomingPage > 0; nextInputs.IsEnabled = (incomingPage + 1) * pageSize < incomingCount;
                previousOutputs.IsEnabled = outgoingPage > 0; nextOutputs.IsEnabled = (outgoingPage + 1) * pageSize < outgoingCount;
                var rowCount = Math.Max(incoming.Length, outgoing.Length);
                canvas.Height = Math.Max(500, 145 + rowCount * 44);
                var centerY = canvas.Height / 2;
                var centerHeight = Math.Max(230, 150 + Math.Min(focus.Selected.Devices.Count, 8) * 24);
                Panel("FEEDS IN", focus.Incoming.Count, 12, 300, incoming.Length);
                Panel("GOES TO", focus.Outgoing.Count, 704, 284, outgoing.Length);
                if (incoming.Length == 0) Put(new TextBlock { Text = "No incoming connections exported.", Foreground = Muted, FontSize = 12,
                    Width = 268, TextWrapping = TextWrapping.Wrap }, 28, centerY - 8);
                if (outgoing.Length == 0) Put(new TextBlock { Text = Category(focus.Selected) == "Output"
                    ? "Exported output channel.\nNo further destination supplied."
                    : "No outgoing connection exported.", Foreground = Muted, FontSize = 12, Width = 250,
                    TextWrapping = TextWrapping.Wrap }, 720, centerY - 16);
                for (var i = 0; i < incoming.Length; i++)
                {
                    var connection = incoming[i]; var y = centerY - incoming.Length * 22 + i * 44;
                    Wire(302, y + 18, 398, centerY, connection.Edge, ChannelBrush(focus.Selected));
                    Put(ChannelButton(connection), 24, y);
                }
                for (var i = 0; i < outgoing.Length; i++)
                {
                    var connection = outgoing[i]; var y = centerY - outgoing.Length * 22 + i * 44;
                    Wire(638, centerY, 716, y + 18, connection.Edge, ChannelBrush(connection.Channel));
                    Put(ChannelButton(connection, 260), 716, y);
                }
                var body = new StackPanel { Spacing = 8 };
                body.Children.Add(new TextBlock { Text = focus.Selected.Name, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap });
                body.Children.Add(new TextBlock { Text = Category(focus.Selected) + " · " + focus.Selected.Id, Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
                var track = focus.Selected.Track;
                body.Children.Add(new TextBlock { Text = $"Saved volume {track?.Volume?.ToString("0.###") ?? "unknown"}\nPan {track?.Pan?.ToString("0.###") ?? "unknown"} · Mute {Flag(track?.Mute)} · Solo {Flag(track?.Solo)}",
                    Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
                body.Children.Add(new Border { Height = 1, Background = Divider });
                body.Children.Add(new TextBlock { Text = $"DEVICES · {focus.Selected.Devices.Count}", FontSize = 10, Foreground = Muted });
                var devices = new ListView { ItemsSource = focus.Selected.Devices.Select(device =>
                    $"{(device.Slot is int slot ? (slot + 1).ToString() : "?")}  {device.Name}" + (device.Enabled == false ? " · disabled" : "")).ToArray(),
                    SelectionMode = ListViewSelectionMode.None, MaxHeight = Math.Max(48, centerHeight - 150), FontSize = 11 };
                if (focus.Selected.Devices.Count == 0) body.Children.Add(new TextBlock { Text = "No devices exported for this channel.", FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
                else body.Children.Add(devices);
                var selectedCard = new Border { Width = 240, MinHeight = centerHeight, Padding = new Thickness(14),
                    Background = Surface, BorderBrush = ChannelBrush(focus.Selected), BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(9), Child = body };
                AutomationProperties.SetName(selectedCard, "Selected channel " + focus.Selected.Name);
                Put(selectedCard, 398, centerY - centerHeight / 2);
                foreach (var connection in focus.Incoming) edgeRows.Add($"{connection.Channel.Name} → {focus.Selected.Name} · {EdgeLabel(connection.Edge)}");
                foreach (var connection in focus.Outgoing) edgeRows.Add($"{focus.Selected.Name} → {connection.Channel.Name} · {EdgeLabel(connection.Edge)}");
                status.Text = $"{focus.Incoming.Count} incoming · {focus.Outgoing.Count} outgoing · exported connections only" +
                    (incomingCount > pageSize ? $" · inputs {incomingPage * pageSize + 1}–{incomingPage * pageSize + incoming.Length}" : "") +
                    (outgoingCount > pageSize ? $" · destinations {outgoingPage * pageSize + 1}–{outgoingPage * pageSize + outgoing.Length}" : "") +
                    (focus.Warnings.Count > 0 ? "\n" + string.Join("\n", focus.Warnings) : "");
                if (autoFit) Fit();
            }
            finally { selecting = false; }
        }
        void Put(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
        void Panel(string title, int count, double x, double width, int rendered)
        {
            var height = Math.Max(230, rendered * 44 + 64);
            var y = canvas.Height / 2 - height / 2;
            Put(new Border { Width = width, Height = height, BorderBrush = Divider, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Background = Surface }, x, y);
            Put(new TextBlock { Text = $"{title} · {count}", FontSize = 10, Foreground = Muted }, x + 14, y + 14);
        }
        Button ChannelButton(RoutingFocusConnection connection, double width = 278)
        {
            var channel = connection.Channel;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new TextBlock { Text = "●", Foreground = ChannelBrush(channel), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = channel.Name, Width = width - 70, FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Width = width, Height = 36, Padding = new Thickness(10, 0),
                Background = Surface, BorderBrush = ChannelBrush(channel), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18) };
            ToolTipService.SetToolTip(button, channel.Name + " · " + Category(channel) + "\n" + EdgeLabel(connection.Edge));
            AutomationProperties.SetName(button, channel.Name + " · " + EdgeLabel(connection.Edge));
            button.Click += (_, _) => Select(channel.Id);
            return button;
        }
        void Wire(double x1, double y1, double x2, double y2, RoutingEdge edge, SolidColorBrush brush)
        {
            var figure = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = false };
            figure.Segments.Add(new BezierSegment { Point1 = new Point((x1 + x2) / 2, y1),
                Point2 = new Point((x1 + x2) / 2, y2), Point3 = new Point(x2, y2) });
            var geometry = new PathGeometry(); geometry.Figures.Add(figure);
            var wire = new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = brush, StrokeThickness = 1.5, IsHitTestVisible = false };
            if (edge.Kind.Equals("Send", StringComparison.OrdinalIgnoreCase)) wire.StrokeDashArray = new DoubleCollection { 4, 3 };
            canvas.Children.Add(wire);
            Put(new Polygon { Points = new PointCollection { new(0, 0), new(6, 4), new(0, 8) }, Fill = brush,
                IsHitTestVisible = false }, x2 - 6, y2 - 4);
        }
        search.TextChanged += (_, _) => RefreshChannels(); category.SelectionChanged += (_, _) => RefreshChannels();
        previousInputs.Click += (_, _) => { if (incomingPage > 0) { incomingPage--; Select(selectedId); } };
        nextInputs.Click += (_, _) => { if ((incomingPage + 1) * pageSize < incomingCount) { incomingPage++; Select(selectedId); } };
        previousOutputs.Click += (_, _) => { if (outgoingPage > 0) { outgoingPage--; Select(selectedId); } };
        nextOutputs.Click += (_, _) => { if ((outgoingPage + 1) * pageSize < outgoingCount) { outgoingPage++; Select(selectedId); } };
        list.SelectionChanged += (_, args) => { if (args.AddedItems.FirstOrDefault() is SignalFlowChannelRow row) Select(row.Channel.Id); };
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is RoutingFocusChannel channel) Select(channel.Id); };
        RefreshChannels(); Select(selectedId);
        return root;

        static string Flag(bool? flag) => flag switch { true => "On", false => "Off", _ => "unknown" };
    }

    private static string Category(RoutingFocusChannel channel) => (channel.Kind.ToLowerInvariant()) switch
    {
        "master" => "Output", "submix" or "group" => "Group", "effect" or "fx" => "FX",
        "audio" => "Audio", "instrument/midi" => "Instrument/MIDI", "unresolved" => "Unresolved",
        "regular" when channel.Track?.Kind == "Audio" => "Audio",
        "regular" when channel.Track?.Kind == "Instrument/MIDI" => "Instrument/MIDI", _ => "Other"
    };

    private static SolidColorBrush ChannelBrush(RoutingFocusChannel channel) => new(Category(channel) switch
    {
        "Audio" => Color.FromArgb(255, 100, 160, 235), "Instrument/MIDI" => Color.FromArgb(255, 214, 108, 180),
        "Group" => Color.FromArgb(255, 218, 139, 95), "FX" => Color.FromArgb(255, 175, 132, 226),
        "Output" => Color.FromArgb(255, 182, 189, 201), _ => Color.FromArgb(255, 126, 138, 152)
    });

    private static string EdgeLabel(RoutingEdge edge) => edge.Kind +
        (edge.Kind.Equals("Send", StringComparison.OrdinalIgnoreCase)
            ? $" · enabled {edge.Enabled?.ToString() ?? "unknown"} · level {edge.Level?.ToString("0.###") ?? "unknown"} · tap {edge.Tap ?? "unknown"}" : "");

    private sealed record SignalFlowChannelRow(RoutingFocusChannel Channel, SolidColorBrush Brush)
    {
        public string Name => Channel.Name;
        public string Kind => Category(Channel);
    }
}
