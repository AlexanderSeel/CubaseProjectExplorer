using System.Diagnostics.CodeAnalysis;
using Uno.Resizetizer;
using CubaseInspector.App.Services;
using CubaseInspector.App.ViewModels;
using CubaseInspector.Infrastructure;
using System.IO;
using System.Diagnostics;

namespace CubaseInspector.App;

public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    [SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Uno.Extensions APIs are used in a way that is safe for trimming in this template context.")]
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var preferences = new AppPreferencesService();
        preferences.Load();
        var builder = this.CreateBuilder(args)
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(preferences);
                    services.AddCubaseInspector(preferences.DatabasePath);
                    services.AddSingleton<IProjectPathPicker, ProjectPathPicker>();
                    services.AddSingleton<ProjectSessionState>();
                    services.AddSingleton<ProjectLibraryViewModel>();
                    services.AddSingleton<ProjectWorkspaceViewModel>();
                    services.AddSingleton<ProjectComparisonViewModel>();
                    services.AddSingleton<PluginInventoryViewModel>();
                    services.AddSingleton<ProjectToolsViewModel>();
                    services.AddSingleton<ProjectStatisticsViewModel>();
                    services.AddSingleton<LibraryAdminViewModel>();
                    services.AddSingleton<MediaLibraryViewModel>();
                    services.AddSingleton<LibrarySearchViewModel>();
                    services.AddSingleton<PluginCatalogStore>();
                    services.AddSingleton<ExternalPluginDirectoryGrabber>();
                    services.AddSingleton<CatalogImageFetcher>();
                    services.AddSingleton<PluginCatalogViewModel>();
                    services.AddSingleton<ProjectPreflightViewModel>();
                    services.AddSingleton<AppShellViewModel>();
                })
            );
        MainWindow = builder.Window;

        #if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath)) MainWindow.AppWindow.SetIcon(iconPath);
            else Trace.TraceWarning("CPE window icon was not found at {0}", iconPath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or
            UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Trace.TraceWarning("Could not apply the CPE window and taskbar icon: {0}", exception.Message);
        }

        Host = builder.Build();
        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        if (MainWindow.Content is not Frame rootFrame)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = new Frame();

            // Place the frame in the current Window
            MainWindow.Content = rootFrame;
        }

        if (rootFrame.Content == null)
        {
            // When the navigation stack isn't restored navigate to the first page,
            // configuring the new page by passing required information as a navigation
            // parameter
            rootFrame.Navigate(typeof(MainPage), Host.Services.GetRequiredService<AppShellViewModel>());
        }
        // Ensure the current window is active
        MainWindow.Activate();
    }
}
