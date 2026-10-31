using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private static readonly Thickness PageInset = new(0, 2, 0, 16);
    private static readonly Thickness SectionInset = new(18);
    private static SolidColorBrush ResourceBrush(string key) => (SolidColorBrush)Application.Current.Resources[key];
    private static readonly SolidColorBrush Surface = ResourceBrush("InspectorSurfaceBrush");
    private static readonly SolidColorBrush Accent = ResourceBrush("InspectorAccentBrush");
    private static readonly SolidColorBrush Muted = ResourceBrush("InspectorTextSecondaryBrush");
    private static readonly SolidColorBrush Divider = ResourceBrush("InspectorDividerBrush");
    private static readonly SolidColorBrush Notice = ResourceBrush("InspectorNoticeBrush");
    private readonly Dictionary<StackPanel, StackPanel> _pageToolbars = [];

    private StackPanel Page(string title, string subtitle)
    {
        var root = new StackPanel { Spacing = 16, Margin = PageInset, MaxWidth = 1680,
            HorizontalAlignment = HorizontalAlignment.Center };
        var heading = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["InspectorPageTitleStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis });
        heading.Children.Add(new TextBlock { Text = subtitle, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
            TextWrapping = TextWrapping.Wrap, MaxWidth = 760 });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var toolbarViewport = new ScrollViewer { Content = toolbar, MaxWidth = 760,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Center };
        var headerGrid = new Grid { ColumnSpacing = 24, MinHeight = 76 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        headerGrid.Children.Add(heading);
        Grid.SetColumn(toolbarViewport, 1);
        headerGrid.Children.Add(toolbarViewport);
        headerGrid.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 760;
            headerGrid.ColumnDefinitions[1].Width = compact
                ? new GridLength(1, GridUnitType.Star)
                : GridLength.Auto;
            Grid.SetColumn(toolbarViewport, compact ? 0 : 1);
            Grid.SetRow(toolbarViewport, compact ? 1 : 0);
            toolbarViewport.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            toolbar.Orientation = args.NewSize.Width < 520 ? Orientation.Vertical : Orientation.Horizontal;
        };
        root.Children.Add(new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 14), Child = headerGrid });
        _pageToolbars[root] = toolbar;
        return root;
    }

    private static Grid DashboardTabs(params (string Title, UIElement Content)[] sections) => DashboardTabs(0, sections);

    private static Grid DashboardTabs(int initialIndex, params (string Title, UIElement Content)[] sections)
    {
        var tabs = new Grid { RowSpacing = 10 };
        tabs.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tabs.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var contentHost = new Grid();
        Grid.SetRow(commands, 0);
        Grid.SetRow(contentHost, 1);
        tabs.Children.Add(commands);
        tabs.Children.Add(contentHost);
        var buttons = new List<Button>();
        var selected = 0;
        void Show(int index)
        {
            selected = index;
            contentHost.Children.Clear();
            var content = sections[index].Content;
            contentHost.Children.Add(content);
            for (var i = 0; i < buttons.Count; i++)
            {
                buttons[i].IsEnabled = true;
                buttons[i].Foreground = i == index ? Accent : Muted;
                buttons[i].BorderBrush = i == index ? Accent : Divider;
                buttons[i].BorderThickness = new Thickness(0, 0, 0, i == index ? 2 : 1);
                AutomationProperties.SetName(buttons[i], $"{sections[i].Title} tab{(i == index ? ", selected" : "")}");
            }
        }
        foreach (var (title, _) in sections)
        {
            var index = buttons.Count;
            var button = new Button { Content = title, Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"],
                IsEnabled = true };
            button.Click += (_, _) => Show(index);
            buttons.Add(button);
            commands.Children.Add(button);
        }
        tabs.SizeChanged += (_, args) => commands.Orientation = args.NewSize.Width < 560 ? Orientation.Vertical : Orientation.Horizontal;
        Show(Math.Clamp(initialIndex, 0, Math.Max(0, sections.Length - 1)));
        return tabs;
    }

    private void AddPageActions(StackPanel page, params UIElement[] actions)
    {
        if (!_pageToolbars.TryGetValue(page, out var toolbar)) return;
        foreach (var action in actions)
        {
            if (action is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(action);
        }
    }

    private static Flyout Popup(UIElement content, double maxWidth = 520, double maxHeight = 560)
    {
        return new Flyout
        {
            Content = new ScrollViewer
            {
                Content = content,
                MaxWidth = maxWidth,
                MaxHeight = maxHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
    }

    private void ShowInformationWindow(string title, UIElement content, int width = 840, int height = 640) =>
        _ = InfoWindowPresenter.ShowAsync(xamlRoot, title, content, width, height);

    private Border Metric(string label, string value) => new()
    {
        Background = Surface, BorderBrush = Divider, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10), Padding = SectionInset,
        Child = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = label, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
            new TextBlock { Text = value, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } } }
    };

    private Border Note(string text) => new() { Background = Notice, BorderBrush = Accent,
        CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12), Margin = new Thickness(0, 2, 0, 2),
        Child = new TextBlock { Text = text, Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextWrapping = TextWrapping.Wrap } };

    private Border Empty(string text) => new() { Background = Surface, BorderBrush = Divider, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Margin = new Thickness(0, 4, 0, 0),
        Child = new TextBlock { Text = text, Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], Foreground = Muted } };

    private StackPanel DetailCard(string heading, params (string Name, string Value)[] rows)
    {
        var panel = new StackPanel { Background = Surface, Padding = SectionInset, Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = heading, Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"], Foreground = Accent });
        foreach (var (name, value) in rows)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = name, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] });
            var val = new TextBlock { Text = value, Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(val, 1); row.Children.Add(val); panel.Children.Add(row);
        }
        return panel;
    }

    private UIElement Section(string heading, IEnumerable<string> rows)
    {
        var panel = new StackPanel { Background = Surface, Padding = SectionInset, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock { Text = heading, Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"], Margin = new Thickness(0, 0, 0, 4) });
        foreach (var text in rows)
            panel.Children.Add(new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 7, 0, 7), Child = new TextBlock { Text = text, Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis } });
        return new ScrollViewer { Content = panel, MaxHeight = 620 };
    }

    private Border TableHeader(params (string Label, double Width)[] columns)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12, 6) };
        foreach (var column in columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Width, GridUnitType.Star) });
        for (var i = 0; i < columns.Length; i++)
        {
            var label = new TextBlock { Text = columns[i].Label, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 171, 181, 194)), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, i); grid.Children.Add(label);
        }
        return new Border { Background = Surface, Child = grid };
    }

    private ComboBox Combo(string placeholder, IReadOnlyList<string> options, double width = 150)
    {
        var control = new ComboBox { PlaceholderText = placeholder, ItemsSource = options, SelectedIndex = 0, Width = width,
            Style = (Style)Application.Current.Resources["InspectorFilterComboBoxStyle"] };
        return control;
    }

    private Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        button.Click += (_, _) => action();
        return button;
    }

    private Button InfoButton(Action action, string accessibleName = "More about this data")
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = "\uE946", FontSize = 15 },
            Width = 36,
            MinWidth = 36,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"]
        };
        ToolTipService.SetToolTip(button, accessibleName);
        AutomationProperties.SetName(button, accessibleName);
        button.Click += (_, _) => action();
        return button;
    }

    private TextBox FilterTextBox(string placeholder, double width = double.NaN) => new()
    {
        PlaceholderText = placeholder,
        Width = width,
        Style = (Style)Application.Current.Resources["InspectorFilterTextBoxStyle"]
    };
}
