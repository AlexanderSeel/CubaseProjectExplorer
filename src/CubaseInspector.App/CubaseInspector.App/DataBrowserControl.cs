using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed record DataBrowserColumn<T>(string Header, string SortKey,
    Func<T, string?> Display, Func<T, IComparable?> SortValue, double? WidthWeight = null);

/// <summary>Shared, virtualized table/card browser for indexed project and media records.</summary>
internal sealed class DataBrowserControl<T> : Grid where T : class
{
    private readonly IReadOnlyList<DataBrowserColumn<T>> _columns;
    private readonly Func<T, string, string> _groupKey;
    private readonly Action<string> _groupChanged;
    private readonly Func<T, Task> _activate;
    private readonly Func<T, MenuFlyout?> _contextMenu;
    private readonly ObservableCollection<BrowserRow<T>> _rows = [];
    private readonly Dictionary<string, bool> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly DataGrid _grid;
    private readonly GridView _cards;
    private readonly ObservableCollection<BrowserRow<T>> _cardPage = [];
    private readonly StackPanel _cardPager;
    private readonly Button _previousCardPage;
    private readonly Button _nextCardPage;
    private readonly TextBlock _cardPageLabel;
    private readonly ComboBox _groupPicker;
    private readonly Button _tableButton;
    private readonly Button _cardsButton;
    private readonly IReadOnlyList<string> _groupModes;
    private readonly IReadOnlyList<T> _source;
    private string _groupMode;
    private string? _sortKey;
    private bool _sortAscending = true;
    private bool _updating;
    private T? _selectedItem;
    private int _cardPageStart;

    // Bound realized cards so changing views does not construct an entire large library at once.
    private const int CardPageSize = 32;

    public DataBrowserControl(IReadOnlyList<DataBrowserColumn<T>> columns, IReadOnlyList<T> source,
        IReadOnlyList<string> groupModes, string initialGroupMode, Func<T, string, string> groupKey,
        Action<string> groupChanged, Func<T, Task> activate, Func<T, MenuFlyout?> contextMenu,
        DataTemplate? cardTemplate = null, bool cardsInitiallyVisible = false,
        double minimumRowsHeight = 320, double maximumRowsHeight = 620, bool showToolbar = true)
    {
        if (columns.Count is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(columns), "The shared browser supports 1–8 columns.");
        _columns = columns;
        _source = source;
        _groupModes = groupModes;
        _groupMode = initialGroupMode;
        _groupKey = groupKey;
        _groupChanged = groupChanged;
        _activate = activate;
        _contextMenu = contextMenu;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid { ColumnSpacing = 10, Padding = new Thickness(10, 6),
            Background = (Brush)Application.Current.Resources["InspectorSurfaceBrush"] };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var groupLabel = new TextBlock { Text = "Group", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["InspectorTextSecondaryBrush"] };
        AutomationProperties.SetName(groupLabel, "Group results");
        toolbar.Children.Add(groupLabel);
        _groupPicker = new ComboBox { ItemsSource = groupModes, SelectedItem = initialGroupMode,
            MinWidth = 140, MaxWidth = 210, VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["InspectorFilterComboBoxStyle"] };
        _groupPicker.SelectionChanged += (_, _) =>
        {
            if (_groupPicker.SelectedItem is string mode && mode != _groupMode)
            {
                _groupMode = mode;
                _groupChanged(mode);
                RefreshRows();
            }
        };
        Grid.SetColumn(_groupPicker, 1);
        toolbar.Children.Add(_groupPicker);
        var viewButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _tableButton = new Button { Content = "Details", MinWidth = 76, IsEnabled = false,
            Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        _cardsButton = new Button { Content = "Cards", MinWidth = 76,
            Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
        AutomationProperties.SetName(_tableButton, "Show sortable details grid");
        AutomationProperties.SetName(_cardsButton, "Show cards");
        AutomationProperties.SetName(_groupPicker, "Group results by");
        _tableButton.Click += (_, _) => SetView(false);
        _cardsButton.Click += (_, _) => SetView(true);
        viewButtons.Children.Add(_tableButton);
        viewButtons.Children.Add(_cardsButton);
        Grid.SetColumn(viewButtons, 2);
        toolbar.Children.Add(viewButtons);
        _cardPager = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _previousCardPage = new Button { Content = "Previous", MinWidth = 72, IsEnabled = false };
        _nextCardPage = new Button { Content = "Next", MinWidth = 64, IsEnabled = false };
        _cardPageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        AutomationProperties.SetName(_previousCardPage, "Previous cards page");
        AutomationProperties.SetName(_nextCardPage, "Next cards page");
        _previousCardPage.Click += (_, _) => ChangeCardPage(-CardPageSize);
        _nextCardPage.Click += (_, _) => ChangeCardPage(CardPageSize);
        _cardPager.Children.Add(_cardPageLabel);
        _cardPager.Children.Add(_previousCardPage);
        _cardPager.Children.Add(_nextCardPage);
        Grid.SetColumn(_cardPager, 3);
        toolbar.Children.Add(_cardPager);
        Children.Add(new Border { BorderBrush = (Brush)Application.Current.Resources["InspectorDividerBrush"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Visibility = showToolbar ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 8), Child = toolbar });

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserSortColumns = true,
            CanUserResizeColumns = true,
            CanUserReorderColumns = true,
            IsReadOnly = true,
            RowHeight = 34,
            ColumnHeaderHeight = 36,
            Background = (Brush)Application.Current.Resources["InspectorSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["InspectorDividerBrush"],
            BorderThickness = new Thickness(1),
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            ItemsSource = _rows,
            MinHeight = minimumRowsHeight,
            MaxHeight = maximumRowsHeight
        };
        var headerStyle = new Style(typeof(CommunityToolkit.WinUI.UI.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.Resources["InspectorSurfaceBrush"]));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.Resources["InspectorTextSecondaryBrush"]));
        headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        _grid.ColumnHeaderStyle = headerStyle;
        AutomationProperties.SetName(_grid, "Sortable results grid. Select a group heading to collapse or expand it.");
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding { Path = new PropertyPath($"Value{index}") },
                Tag = index,
                Width = new DataGridLength(column.WidthWeight ?? (index == 0 ? 2 : 1), DataGridLengthUnitType.Star)
            });
        }
        _grid.Sorting += GridSorting;
        _grid.SelectionChanged += async (_, _) => await HandleSelectionAsync(_grid.SelectedItem);
        _grid.LoadingRow += (_, args) =>
        {
            if (args.Row.DataContext is BrowserRow<T> row && !row.IsGroupHeader && row.Item is not null)
                args.Row.ContextFlyout = _contextMenu(row.Item);
            else args.Row.ContextFlyout = null;
        };
        Grid.SetRow(_grid, 1);
        Children.Add(_grid);

        _cards = new GridView
        {
            ItemsSource = _cardPage,
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = minimumRowsHeight,
            MaxHeight = maximumRowsHeight,
            ItemTemplate = cardTemplate ?? BuildCardTemplate(columns.Count)
        };
        AutomationProperties.SetName(_cards, "Results cards. Select a group heading to collapse or expand it.");
        _cards.SelectionChanged += async (_, _) => await HandleSelectionAsync(_cards.SelectedItem);
        _cards.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemContainer is not GridViewItem container) return;
            if (args.InRecycleQueue || args.Item is not BrowserRow<T> { IsGroupHeader: false, Item: not null } row)
                container.ContextFlyout = null;
            else container.ContextFlyout = _contextMenu(row.Item);
        };
        _cards.Visibility = Visibility.Collapsed;
        Grid.SetRow(_cards, 1);
        Children.Add(_cards);

        if (_source is INotifyCollectionChanged collection)
            collection.CollectionChanged += SourceCollectionChanged;
        Unloaded += (_, _) =>
        {
            if (_source is INotifyCollectionChanged notify) notify.CollectionChanged -= SourceCollectionChanged;
        };
        if (cardsInitiallyVisible) SetView(cards: true);
        RefreshRows();
    }

    private static DataTemplate BuildCardTemplate(int columnCount) => new(() =>
    {
        var border = new Border
        {
            Width = 260,
            MinHeight = 112,
            Margin = new Thickness(5),
            Padding = new Thickness(14),
            Background = (Brush)Application.Current.Resources["InspectorSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["InspectorDividerBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        var stack = new StackPanel { Spacing = 6 };
        for (var index = 0; index < Math.Min(columnCount, 4); index++)
        {
            var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = index == 0 ? TextWrapping.NoWrap : TextWrapping.Wrap,
                FontWeight = index == 0 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                FontSize = index == 0 ? 14 : 11 };
            text.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath($"Value{index}") });
            stack.Children.Add(text);
        }
        border.Child = stack;
        return border;
    });

    private void SourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshRows();

    private void GridSorting(object? sender, DataGridColumnEventArgs args)
    {
        if (args.Column.Tag is not int index || index < 0 || index >= _columns.Count) return;
        _sortKey = _columns[index].SortKey;
        _sortAscending = args.Column.SortDirection != DataGridSortDirection.Ascending;
        foreach (var column in _grid.Columns) column.SortDirection = null;
        args.Column.SortDirection = _sortAscending ? DataGridSortDirection.Ascending : DataGridSortDirection.Descending;
        RefreshRows();
    }

    private async Task HandleSelectionAsync(object? selected)
    {
        if (_updating || selected is not BrowserRow<T> row) return;
        if (row.IsGroupHeader)
        {
            var key = _groupMode + "\0" + row.GroupKey;
            _collapsedGroups[key] = !_collapsedGroups.GetValueOrDefault(key);
            RefreshRows();
            return;
        }
        if (row.Item is null) return;
        _selectedItem = row.Item;
        await _activate(row.Item);
    }

    private void SetView(bool cards)
    {
        var row = _selectedItem is null ? null : _rows.FirstOrDefault(item => Equals(item.Item, _selectedItem));
        var selectedIndex = row is null ? -1 : _rows.IndexOf(row);
        if (cards && selectedIndex >= 0)
            _cardPageStart = selectedIndex / CardPageSize * CardPageSize;

        _updating = true;
        _grid.Visibility = cards ? Visibility.Collapsed : Visibility.Visible;
        _cards.Visibility = cards ? Visibility.Visible : Visibility.Collapsed;
        _cardPager.Visibility = cards ? Visibility.Visible : Visibility.Collapsed;
        _tableButton.IsEnabled = cards;
        _cardsButton.IsEnabled = !cards;
        _grid.SelectedItem = cards ? null : row;
        RefreshCardPage(row);
        _updating = false;
    }

    private void ChangeCardPage(int offset)
    {
        var lastPageStart = _rows.Count == 0 ? 0 : (_rows.Count - 1) / CardPageSize * CardPageSize;
        _cardPageStart = Math.Clamp(_cardPageStart + offset, 0, lastPageStart);
        _updating = true;
        RefreshCardPage();
        _updating = false;
    }

    private void RefreshCardPage(BrowserRow<T>? selectedRow = null)
    {
        var wasUpdating = _updating;
        _updating = true;
        _cardPage.Clear();
        var end = Math.Min(_cardPageStart + CardPageSize, _rows.Count);
        for (var index = _cardPageStart; index < end; index++)
            _cardPage.Add(_rows[index]);

        _cards.SelectedItem = selectedRow is not null && _cardPage.Contains(selectedRow) ? selectedRow : null;
        var pageCount = Math.Max(1, (_rows.Count + CardPageSize - 1) / CardPageSize);
        var pageNumber = _rows.Count == 0 ? 0 : _cardPageStart / CardPageSize + 1;
        _cardPageLabel.Text = _rows.Count == 0
            ? "No cards"
            : $"Page {pageNumber} of {pageCount} · {_rows.Count:N0} rows";
        _previousCardPage.IsEnabled = _cardPageStart > 0;
        _nextCardPage.IsEnabled = end < _rows.Count;
        _updating = wasUpdating;
    }

    public void SelectItem(T? item)
    {
        _selectedItem = item;
        var row = item is null ? null : _rows.FirstOrDefault(candidate => Equals(candidate.Item, item));
        _updating = true;
        _grid.SelectedItem = _grid.Visibility == Visibility.Visible ? row : null;
        if (_cards.Visibility == Visibility.Visible && row is not null)
        {
            var rowIndex = _rows.IndexOf(row);
            if (rowIndex >= 0 && (rowIndex < _cardPageStart || rowIndex >= _cardPageStart + CardPageSize))
                _cardPageStart = rowIndex / CardPageSize * CardPageSize;
            RefreshCardPage(row);
        }
        else _cards.SelectedItem = null;
        _updating = false;
    }

    private void RefreshRows()
    {
        var oldSelection = _selectedItem;
        var ordered = _source.ToList();
        if (_sortKey is not null)
        {
            var columnIndex = _columns.ToList().FindIndex(column => column.SortKey == _sortKey);
            if (columnIndex >= 0)
            {
                var sortValue = _columns[columnIndex].SortValue;
                ordered.Sort((left, right) =>
                {
                    var a = sortValue(left); var b = sortValue(right);
                    var comparison = a is null ? (b is null ? 0 : -1) : b is null ? 1 :
                        a is string textA && b is string textB ? StringComparer.OrdinalIgnoreCase.Compare(textA, textB) :
                        a.GetType() == b.GetType() ? a.CompareTo(b) : StringComparer.OrdinalIgnoreCase.Compare(a.ToString(), b.ToString());
                    return _sortAscending ? comparison : -comparison;
                });
            }
        }

        _updating = true;
        _rows.Clear();
        if (_groupMode == "None")
        {
            foreach (var item in ordered) _rows.Add(BrowserRow<T>.Data(item, _columns));
        }
        else
        {
            var groups = ordered.GroupBy(item => _groupKey(item, _groupMode), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var key = _groupMode + "\0" + group.Key;
                var collapsed = _collapsedGroups.GetValueOrDefault(key);
                _rows.Add(BrowserRow<T>.Heading(group.Key, group.Count(), collapsed));
                if (!collapsed)
                    foreach (var item in group) _rows.Add(BrowserRow<T>.Data(item, _columns));
            }
        }
        var selectedRow = oldSelection is null ? null : _rows.FirstOrDefault(row => Equals(row.Item, oldSelection));
        var selectedIndex = selectedRow is null ? -1 : _rows.IndexOf(selectedRow);
        if (selectedIndex >= 0)
            _cardPageStart = selectedIndex / CardPageSize * CardPageSize;
        else if (_cardPageStart >= _rows.Count)
            _cardPageStart = _rows.Count == 0 ? 0 : (_rows.Count - 1) / CardPageSize * CardPageSize;
        _grid.SelectedItem = _grid.Visibility == Visibility.Visible ? selectedRow : null;
        RefreshCardPage(selectedRow);
        _selectedItem = oldSelection;
        _updating = false;
    }

    private sealed class BrowserRow<TItem> where TItem : class
    {
        public TItem? Item { get; private init; }
        public bool IsGroupHeader { get; private init; }
        public string GroupKey { get; private init; } = "";
        public string Value0 { get; private init; } = "";
        public string Value1 { get; private init; } = "";
        public string Value2 { get; private init; } = "";
        public string Value3 { get; private init; } = "";
        public string Value4 { get; private init; } = "";
        public string Value5 { get; private init; } = "";
        public string Value6 { get; private init; } = "";
        public string Value7 { get; private init; } = "";

        public static BrowserRow<TItem> Data(TItem item, IReadOnlyList<DataBrowserColumn<TItem>> columns)
        {
            var values = columns.Select(column => column.Display(item) ?? "").ToArray();
            return new BrowserRow<TItem> { Item = item, Value0 = At(values, 0), Value1 = At(values, 1),
                Value2 = At(values, 2), Value3 = At(values, 3), Value4 = At(values, 4), Value5 = At(values, 5),
                Value6 = At(values, 6), Value7 = At(values, 7) };
        }

        public static BrowserRow<TItem> Heading(string key, int count, bool collapsed) =>
            new() { IsGroupHeader = true, GroupKey = key, Value0 = $"{(collapsed ? "▸" : "▾")}  {key} · {count:N0}" };

        private static string At(string[] values, int index) => index < values.Length ? values[index] : "";
    }
}
