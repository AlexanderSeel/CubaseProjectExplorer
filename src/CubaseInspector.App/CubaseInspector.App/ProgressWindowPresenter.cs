using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Windows.Graphics;

namespace CubaseInspector.App;

/// <summary>Non-modal progress window for long work that should not occupy the main page.</summary>
internal static class ProgressWindowPresenter
{
    private static Window? _window;
    private static TextBlock? _status;
    private static ProgressBar? _progress;
    private static Button? _cancel;
    private static Action? _cancelAction;
    private static bool _dismissed;
    private static bool _closingForCompletion;

    public static void ShowOrUpdate(string title, string status, double? progress,
        Action? cancel, ElementTheme theme)
    {
        if (_dismissed) return;
        if (_window is null) CreateWindow(title, theme);

        _window!.Title = "Cubase Project Explorer — " + title;
        _status!.Text = status;
        _progress!.IsIndeterminate = progress is null;
        if (progress is double value) _progress.Value = Math.Clamp(value, 0, 100);
        _cancelAction = cancel;
        _cancel!.Visibility = cancel is null ? Visibility.Collapsed : Visibility.Visible;
        _cancel.IsEnabled = cancel is not null;
    }

    public static void Close()
    {
        _dismissed = false;
        if (_window is null) return;
        _closingForCompletion = true;
        _window.Close();
        _closingForCompletion = false;
    }

    private static void CreateWindow(string title, ElementTheme theme)
    {
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        AutomationProperties.SetName(_status, "Background task status");
        _progress = new ProgressBar { Minimum = 0, Maximum = 100, IsIndeterminate = true,
            Height = 6, Margin = new Thickness(0, 4, 0, 0) };
        _cancel = new Button { Content = "Cancel scan", HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = Visibility.Collapsed };
        _cancel.Click += (_, _) =>
        {
            _cancel!.IsEnabled = false;
            _cancel.Content = "Canceling…";
            _cancelAction?.Invoke();
        };

        var content = new StackPanel { Spacing = 16, Padding = new Thickness(24), MinWidth = 420 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_status);
        content.Children.Add(_progress);
        content.Children.Add(new TextBlock
        {
            Text = "This is a separate, non-modal window. You can move or resize it, return to the main app, or close this window while work continues.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.8
        });
        content.Children.Add(_cancel);

        _window = new Window { Title = "Cubase Project Explorer — " + title, Content = content };
        content.RequestedTheme = theme;
        _window.Closed += (_, _) =>
        {
            if (!_closingForCompletion) _dismissed = true;
            _window = null;
            _status = null;
            _progress = null;
            _cancel = null;
            _cancelAction = null;
        };
        _window.AppWindow.Resize(new SizeInt32 { Width = 960, Height = 560 });
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 440;
        }
        _window.Activate();
    }
}
