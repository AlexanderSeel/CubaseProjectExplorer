using System.Globalization;
using CubaseInspector.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace CubaseInspector.App;

internal sealed partial class WorkspacePageFactory
{
    private UIElement CalendarPage()
    {
        var entries = shell.Library.AllEntries;
        var root = Page("Project Calendar", "Project file modification dates only · they do not represent song creation or recording dates.");
        var dateCounts = ProjectCalendarBrowser.CountsByLocalFileModifiedDate(entries);
        if (entries.Count == 0)
        {
            root.Children.Add(Empty("No projects are currently indexed. Open a project or scan a folder from Project Library."));
            return root;
        }

        Button ProjectButton(ProjectLibraryEntry entry)
        {
            var button = new Button { Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"] };
            button.Click += async (_, _) =>
            {
                await shell.Library.OpenProjectAsync(entry.Project.FilePath);
                if (shell.HasProject) { shell.NavigateTo("Overview"); navigate("Overview"); }
            };
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = entry.Project.Name, Style = (Style)Application.Current.Resources["InspectorBodyTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
            var path = new TextBlock { Text = entry.Project.FilePath, Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(path, 1); row.Children.Add(path);
            var type = new TextBlock { Text = Path.GetExtension(entry.Project.FilePath).TrimStart('.').ToUpperInvariant() + " · " + EvidencePresentation.Label(entry.Project.Evidence),
                Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(type, 2); row.Children.Add(type);
            button.Content = row;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            ToolTipService.SetToolTip(button, entry.Project.FilePath);
            return button;
        }

        var unknownDateEntries = ProjectCalendarBrowser.WithoutKnownFileModifiedDate(entries);
        if (dateCounts.Count == 0)
            root.Children.Add(Note("No readable file modification dates are available yet. The year/month calendar remains available, and undated projects stay listed below."));

        var years = dateCounts.Keys.Select(date => date.Year).Distinct().OrderDescending().ToArray();
        var latestDate = dateCounts.Count == 0
            ? DateOnly.FromDateTime(DateTime.Now)
            : dateCounts.Keys.Max();
        if (years.Length == 0) years = [latestDate.Year];
        var selectedYear = latestDate.Year;
        var selectedMonth = latestDate.Month;
        var yearPicker = Combo("Year", years.Select(year => year.ToString(CultureInfo.CurrentCulture)).ToArray(), 120);
        var yearControl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { new TextBlock { Text = "Year", Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"],
                VerticalAlignment = VerticalAlignment.Center }, yearPicker } };
        AddPageActions(root, yearControl);
        var monthsGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8, Margin = new Thickness(0, 8, 0, 12) };
        for (var column = 0; column < 4; column++) monthsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < 3; row++) monthsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var monthHeading = new TextBlock { Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] };
        var monthSummary = new TextBlock { Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"], Margin = new Thickness(0, 0, 0, 8) };
        var datesPanel = new StackPanel { Spacing = 10 };

        void RefreshMonthCards()
        {
            monthsGrid.Children.Clear();
            var culture = CultureInfo.CurrentCulture;
            for (var month = 1; month <= 12; month++)
            {
                var monthNumber = month;
                var count = dateCounts.Where(pair => pair.Key.Year == selectedYear && pair.Key.Month == monthNumber)
                    .Sum(pair => pair.Value);
                var activeDays = dateCounts.Count(pair => pair.Key.Year == selectedYear && pair.Key.Month == monthNumber);
                var content = new StackPanel { Spacing = 3, Children =
                {
                    new TextBlock { Text = culture.DateTimeFormat.GetMonthName(monthNumber), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = $"{count:N0} files · {activeDays:N0} active dates",
                        Style = (Style)Application.Current.Resources["InspectorMetadataTextStyle"] }
                } };
                var button = new Button
                {
                    Content = content,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = monthNumber == selectedMonth ? Accent : Surface,
                    BorderBrush = monthNumber == selectedMonth ? Accent : Divider,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12),
                    MinHeight = 68,
                    Style = (Style)Application.Current.Resources["InspectorCommandButtonStyle"]
                };
                button.Click += (_, _) => { selectedMonth = monthNumber; RefreshCalendar(); };
                Grid.SetColumn(button, (monthNumber - 1) % 4);
                Grid.SetRow(button, (monthNumber - 1) / 4);
                monthsGrid.Children.Add(button);
            }
        }

        void RefreshCalendar()
        {
            RefreshMonthCards();
            var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(selectedMonth);
            monthHeading.Text = $"{monthName} {selectedYear}";
            var monthEntries = entries
                .Where(entry => entry.LastModifiedUtc is { } modified &&
                    modified.ToLocalTime().Year == selectedYear && modified.ToLocalTime().Month == selectedMonth)
                .OrderByDescending(entry => entry.LastModifiedUtc)
                .ThenBy(entry => entry.Project.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var activeDates = dateCounts.Count(pair => pair.Key.Year == selectedYear && pair.Key.Month == selectedMonth);
            monthSummary.Text = monthEntries.Length == 0
                ? "No project files were modified during this month."
                : $"{monthEntries.Length:N0} project files · {activeDates:N0} active dates · local file time";
            datesPanel.Children.Clear();
            foreach (var group in monthEntries.GroupBy(entry => DateOnly.FromDateTime(entry.LastModifiedUtc!.Value.ToLocalTime().DateTime))
                         .OrderByDescending(group => group.Key))
            {
                var day = new StackPanel { Spacing = 4 };
                day.Children.Add(new TextBlock
                {
                    Text = $"{group.Key.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM", CultureInfo.CurrentCulture)} · {group.Count():N0} project files",
                    Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"]
                });
                foreach (var entry in group.OrderBy(item => item.Project.Name, StringComparer.OrdinalIgnoreCase))
                {
                    day.Children.Add(ProjectButton(entry));
                }
                datesPanel.Children.Add(new Border { Background = Surface, Padding = new Thickness(12), Child = day });
            }
        }

        yearPicker.SelectedItem = selectedYear.ToString(CultureInfo.CurrentCulture);
        yearPicker.SelectionChanged += (_, _) =>
        {
            if (int.TryParse(yearPicker.SelectedItem?.ToString(), CultureInfo.CurrentCulture, out var year))
            {
                selectedYear = year;
                RefreshCalendar();
            }
        };
        root.Children.Add(monthsGrid);
        root.Children.Add(monthHeading);
        root.Children.Add(monthSummary);
        root.Children.Add(datesPanel);
        if (unknownDateEntries.Count > 0)
        {
            root.Children.Add(new TextBlock { Text = $"Date unknown · {unknownDateEntries.Count:N0} projects",
                Margin = new Thickness(0, 16, 0, 4), Style = (Style)Application.Current.Resources["InspectorSectionTitleStyle"] });
            foreach (var entry in unknownDateEntries) root.Children.Add(ProjectButton(entry));
        }
        RefreshCalendar();
        return root;
    }
}
