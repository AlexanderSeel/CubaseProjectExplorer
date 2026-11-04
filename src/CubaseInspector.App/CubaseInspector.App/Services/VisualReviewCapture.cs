using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using Windows.Storage.Streams;

namespace CubaseInspector.App.Services;

/// <summary>Explicit, isolated visual-review sessions can capture the actual Skia visual tree.</summary>
internal static class VisualReviewCapture
{
    public static async Task ApplyActionAsync(UIElement visual, string action)
    {
        var elements = Descendants(visual).ToArray();
        string? expectedPage = null;
        if (action.StartsWith("Nav:", StringComparison.Ordinal))
        {
            var navigation = elements.OfType<Microsoft.UI.Xaml.Controls.NavigationView>().Single();
            var item = navigation.MenuItems.Concat(navigation.FooterMenuItems)
                .OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().Single(item => item.Tag as string == action[4..]);
            navigation.SelectedItem = item;
            expectedPage = action[4..];
        }
        else if (action == "Scroll:Bottom")
        {
            var viewport = elements.OfType<Microsoft.UI.Xaml.Controls.ScrollViewer>().Single(view => view.Name == "PageContentViewport");
            viewport.ChangeView(null, viewport.ScrollableHeight, null, true);
        }
        else if (action.StartsWith("Project:", StringComparison.Ordinal))
        {
            var browser = elements.OfType<DataBrowserControl<ViewModels.LibraryProjectRow>>().Single();
            if (!await browser.ActivateFirstAsync(row => row.Name == action[8..]))
                throw new InvalidOperationException("Review project was not found: " + action);
            expectedPage = "Overview";
            if (visual is MainPage projectPage && projectPage.ReviewProjectName != action[8..])
                throw new InvalidOperationException("Project activation did not update the open session.");
        }
        else if (action.StartsWith("Track:", StringComparison.Ordinal))
        {
            var browser = elements.OfType<DataBrowserControl<ViewModels.TrackDisplayRow>>().FirstOrDefault();
            if (browser is null || !await browser.ActivateFirstAsync(row => row.Name == action[6..]))
                throw new InvalidOperationException("Review track selection was not found: " + action);
        }
        else
        {
            var tool = action.StartsWith("Tool:", StringComparison.Ordinal) ? action[5..] : null;
            if (tool is not null && tool is not ("Search everything" or "Project calendar" or "Insights" or
                "Compare projects" or "Plug-in inventory" or "Plug-in catalog" or "Dependency preflight" or "Reports" or "Library database"))
                throw new InvalidOperationException("Unsupported review tool navigation.");
            expectedPage = tool switch
            {
                "Search everything" => "Search", "Project calendar" => "Calendar", "Insights" => "Statistics",
                "Compare projects" => "Compare", "Plug-in inventory" => "Inventory", "Plug-in catalog" => "Catalog",
                "Dependency preflight" => "Preflight", "Reports" => "Tools", "Library database" => "Database", _ => null
            };
            var name = tool ?? (action.StartsWith("Tab:", StringComparison.Ordinal) ? action[4..] : action switch
            {
                "View:Cards" => "Show cards",
                "View:Details" => "Show sortable details grid",
                _ => throw new InvalidOperationException("Unsupported read-only visual review action.")
            });
            var button = elements.OfType<Microsoft.UI.Xaml.Controls.Button>().FirstOrDefault(button =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button).StartsWith(name, StringComparison.Ordinal) &&
                (tool is not null || action.StartsWith("View:", StringComparison.Ordinal) ||
                 Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button).Contains(" tab", StringComparison.Ordinal)));
            if (button is null) throw new InvalidOperationException("Review button was not found: " + action);
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
            if (peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke) is not
                Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke)
                throw new InvalidOperationException("Review button does not expose invocation: " + action);
            invoke.Invoke();
        }
        await Task.Delay(400);
        if (expectedPage is not null && visual is MainPage page && page.ReviewPage != expectedPage)
            throw new InvalidOperationException($"Review navigation expected {expectedPage}, got {page.ReviewPage}.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        var visited = 0;
        while (pending.TryPop(out var current) && visited++ < 20_000)
        {
            yield return current;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(current); i++)
                pending.Push(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(current, i));
        }
    }

    public static async Task SaveAsync(UIElement visual, string path)
    {
        path = Path.GetFullPath(path);
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Visual review output must be a new PNG file.", nameof(path));
        var rendered = new RenderTargetBitmap();
        await rendered.RenderAsync(visual);
        var buffer = await rendered.GetPixelsAsync();
        if (rendered.PixelWidth <= 0 || rendered.PixelHeight <= 0 || buffer.Length == 0)
            throw new InvalidOperationException("The preview visual has no rendered pixels.");
        var pixels = new byte[checked((int)buffer.Length)];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
        using var bitmap = new SKBitmap(rendered.PixelWidth, rendered.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (pixels.Length != bitmap.ByteCount) throw new InvalidOperationException("Unexpected preview pixel stride.");
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoded.SaveTo(output);
    }
}
