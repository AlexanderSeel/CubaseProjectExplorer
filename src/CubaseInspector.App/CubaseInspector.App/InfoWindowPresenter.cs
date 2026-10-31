using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CubaseInspector.App;

/// <summary>Shows inspectable information in a modal overlay inside the app shell.</summary>
internal static class InfoWindowPresenter
{
    private static bool _isOpen;

    public static async Task ShowAsync(XamlRoot xamlRoot, string title, UIElement content,
        int width = 840, int height = 640)
    {
        if (_isOpen) return;
        _isOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = new ScrollViewer
                {
                    Content = content,
                    MaxWidth = Math.Max(480, width - 64),
                    MaxHeight = Math.Max(320, height - 180),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                FullSizeDesired = true
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _isOpen = false;
        }
    }
}
