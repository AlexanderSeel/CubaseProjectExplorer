using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace CubaseInspector.App;

/// <summary>Opens non-modal, native desktop windows for substantial read-only information.</summary>
internal static class InfoWindowPresenter
{
    private static readonly HashSet<Window> OpenWindows = [];

    public static void Show(string title, UIElement content, ElementTheme theme,
        int width = 840, int height = 640)
    {
        var window = new Window
        {
            Title = "Cubase Project Explorer — " + title,
            Content = new ScrollViewer
            {
                Content = content,
                Padding = new Thickness(20),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
        if (window.Content is FrameworkElement root) root.RequestedTheme = theme;
        OpenWindows.Add(window);
        window.Closed += (_, _) => OpenWindows.Remove(window);

        window.AppWindow.Resize(new SizeInt32 { Width = width, Height = height });
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.PreferredMinimumWidth = Math.Min(width, 480);
            presenter.PreferredMinimumHeight = Math.Min(height, 320);
        }
        window.Activate();
    }
}
