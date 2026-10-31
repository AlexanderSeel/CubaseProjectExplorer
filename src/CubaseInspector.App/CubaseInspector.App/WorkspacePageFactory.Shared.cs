using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private static readonly Thickness PageInset = new(0);
    private static readonly Thickness SectionInset = new(16);
    private static SolidColorBrush ResourceBrush(string key) => (SolidColorBrush)Application.Current.Resources[key];
    private static readonly SolidColorBrush Surface = ResourceBrush("InspectorSurfaceBrush");
    private static readonly SolidColorBrush Accent = ResourceBrush("InspectorAccentBrush");
    private static readonly SolidColorBrush Muted = ResourceBrush("InspectorTextSecondaryBrush");
    private static readonly SolidColorBrush Divider = ResourceBrush("InspectorDividerBrush");
    private static readonly SolidColorBrush Notice = ResourceBrush("InspectorNoticeBrush");
    private readonly Dictionary<StackPanel, StackPanel> _pageToolbars = [];

    private StackPanel Page(string title, string subtitle)
    {
        var root = new StackPanel { Spacing = 10, Margin = PageInset };
        var heading = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["InspectorPageTitleStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis });
        heading.Children.Add(new TextBlock { Text = subtitle, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
            TextWrapping = TextWrapping.Wrap, MaxWidth = 760 });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var toolbarViewport = new ScrollViewer { Content = toolbar, MaxWidth = 760,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Center };
        var headerGrid = new Grid { ColumnSpacing = 16, MinHeight = 64 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(heading);
        Grid.SetColumn(toolbarViewport, 1);
        headerGrid.Children.Add(toolbarViewport);
        root.Children.Add(new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 12), Child = headerGrid });
        _pageToolbars[root] = toolbar;
        return root;
    }

    private static Grid DashboardTabs(params (string Title, UIElement Content)[] sections)
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
                buttons[i].IsEnabled = i != index;
                AutomationProperties.SetName(buttons[i], $"{sections[i].Title} tab{(i == index ? ", selected" : "")}");
            }
        }
        foreach (var (title, _) in sections)
        {
            var index = buttons.Count;
            var button = new Button { Content = title, Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"],
                IsEnabled = index != selected };
            button.Click += (_, _) => Show(index);
            buttons.Add(button);
            commands.Children.Add(button);
        }
        tabs.SizeChanged += (_, args) => commands.Orientation = args.NewSize.Width < 560 ? Orientation.Vertical : Orientation.Horizontal;
        Show(0);
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

    private void ShowInformationWindow(string title, UIElement content, int width = 840, int height = 640)
    {
        var theme = shell.Theme switch
        {
            "Light" => ElementTheme.Light,
            "System" => ElementTheme.Default,
            _ => ElementTheme.Dark
        };
        InfoWindowPresenter.Show(title, content, theme, width, height);
    }

    private Border Metric(string label, string value) => new()
    {
        Background = Surface, CornerRadius = new CornerRadius(6), Padding = SectionInset,
        Child = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = label, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
            new TextBlock { Text = value, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } } }
    };

    private Border Note(string text) => new() { Background = Notice, BorderBrush = Accent,
        BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(12, 10), Margin = new Thickness(0, 5, 0, 8),
        Child = new TextBlock { Text = text, Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextWrapping = TextWrapping.Wrap } };

    private Border Empty(string text) => new() { Background = Surface, Padding = new Thickness(18), Margin = new Thickness(0, 8, 0, 0),
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

    private TextBox FilterTextBox(string placeholder, double width = double.NaN) => new()
    {
        PlaceholderText = placeholder,
        Width = width,
        Style = (Style)Application.Current.Resources["InspectorFilterTextBoxStyle"]
    };
}
