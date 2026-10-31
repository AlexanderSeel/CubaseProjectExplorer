using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CubaseInspector.App.ViewModels;

public sealed record InstalledPluginCardRow(
    InstalledPlugin Plugin, ImageSource? Artwork, string Manufacturer, string Version,
    string ClassSummary, string Path)
{
    public string Name => Plugin.Name;
    public string Format => Plugin.Format;
}

public sealed partial class PluginInventoryViewModel(IPluginScannerService scanner, LibraryDocumentStore store) : ObservableObject
{
    private CancellationTokenSource? _scanCancellation;
    private Task? _initialization;
    private IReadOnlyDictionary<string, string> _resourceArtwork = new Dictionary<string, string>();
    [ObservableProperty] private string _status = "Local plug-in declarations are scanned only when requested.";
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasScanned;
    public ObservableCollection<InstalledPlugin> Plugins { get; } = [];
    public IEnumerable<InstalledPlugin> VisiblePlugins => Plugins.Where(p =>
        string.IsNullOrWhiteSpace(Search) || p.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
        p.Path.Contains(Search, StringComparison.OrdinalIgnoreCase));
    partial void OnSearchChanged(string value) => OnPropertyChanged(nameof(VisiblePlugins));

    public InstalledPluginCardRow BuildCardRow(InstalledPlugin plugin)
    {
        var classes = plugin.Classes ?? [];
        var manufacturer = Summary(classes.Select(item => item.Manufacturer), "Manufacturer not supplied");
        var version = Summary(classes.Select(item => item.Version), "Version not supplied");
        var classNames = classes.Select(item => item.Name).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        var classSummary = classNames.Length == 0
            ? $"{classes.Count:N0} class declarations · category not supplied"
            : string.Join(" · ", classNames) + (classes.Count > classNames.Length ? $" · +{classes.Count - classNames.Length:N0} more" : "");
        ImageSource? artwork = null;
        var imagePaths = classes.Select(item => Vst3ClassIdentity.Canonical(item.Cid))
            .Where(cid => cid is not null).Select(cid => _resourceArtwork.GetValueOrDefault(cid!))
            .Where(path => path is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (imagePaths.Length == 1)
        {
            try { artwork = new BitmapImage { UriSource = new Uri(imagePaths[0]!, UriKind.Absolute) }; }
            catch (UriFormatException) { }
        }
        return new InstalledPluginCardRow(plugin, artwork, manufacturer, version, classSummary, plugin.Path);
    }

    private static string Summary(IEnumerable<string?> values, string fallback)
    {
        var labels = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return labels.Length switch { 0 => fallback, 1 => labels[0]!, _ => $"{labels[0]} + {labels.Length - 1:N0} more" };
    }

    public string? FindVerifiedArtwork(ProjectSnapshot project, IEnumerable<PluginReference> references)
    {
        if (!HasScanned) return null;
        var scoped = project with { Plugins = references.ToArray() };
        return VerifiedVst3Artwork.FindLocalSnapshot(scanner.Check(scoped, Plugins.ToArray()));
    }

    public IReadOnlyDictionary<string, string> FindVerifiedArtworkByGroup(ProjectSnapshot project)
    {
        if (!HasScanned) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var checks = scanner.Check(project, Plugins.ToArray());
        return checks.GroupBy(check => ProjectPluginGrouping.Identity(check.Reference), StringComparer.OrdinalIgnoreCase)
            .Select(group => (Identity: group.Key, Path: VerifiedVst3Artwork.FindLocalSnapshot(group) ??
                Vst3SnapshotResources.Find(group.Select(check => check.Reference), _resourceArtwork)))
            .Where(result => result.Path is not null)
            .ToDictionary(result => result.Identity, result => result.Path!, StringComparer.OrdinalIgnoreCase);
    }

    public Task EnsureInitializedAsync() => _initialization ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        try
        {
            var cached = await store.GetPluginsAsync();
            if (!IsBusy && Plugins.Count == 0)
            {
                foreach (var plugin in cached) Plugins.Add(new InstalledPlugin(plugin.Name, plugin.Path, plugin.Format, plugin.Classes));
                var cachedPlugins = Plugins.ToArray();
                _resourceArtwork = await Task.Run(() => Vst3SnapshotResources.Build(cachedPlugins));
                HasScanned = cached.Count > 0;
                OnPropertyChanged(nameof(VisiblePlugins));
                OnPropertyChanged(nameof(ArtworkRevision));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Status = "Cached inventory could not be read: " + ex.Message; }
        if (!IsBusy) await ScanCommand.ExecuteAsync(null);
    }

    public int ArtworkRevision { get; private set; }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        IsBusy = true;
        Status = "Scanning known plug-in locations…";
        try
        {
            var progress = new Progress<ScanProgress>(update =>
                Status = $"Scanning local plug-in folders · {update.Completed:N0} locations checked · {Path.GetFileName(update.CurrentPath)}");
            var result = await scanner.ScanAsync(progress, _scanCancellation.Token);
            if (!result.IsSuccess || result.Value is null)
            {
                Status = result.Error?.Code == "CANCELLED" ? "VST scan cancelled." : result.Error?.Message ?? "Local plug-in scan failed.";
                return;
            }
            Plugins.Clear();
            foreach (var plugin in result.Value) Plugins.Add(plugin);
            _resourceArtwork = await Task.Run(() => Vst3SnapshotResources.Build(result.Value, _scanCancellation.Token));
            HasScanned = true;
            ArtworkRevision++;
            OnPropertyChanged(nameof(ArtworkRevision));
            var persistence = string.Empty;
            try
            {
                Status = $"Found {Plugins.Count:N0} declarations · saving local inventory…";
                await store.SavePluginScanAsync(result.Value, _scanCancellation.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { persistence = " · database save failed: " + ex.Message; }
            Status = $"{Plugins.Count:N0} local VST declarations · Cubase loadability is unknown" + persistence;
            OnPropertyChanged(nameof(VisiblePlugins));
        }
        finally { IsBusy = false; _scanCancellation?.Dispose(); _scanCancellation = null; }
    }

    [RelayCommand]
    private void CancelScan() => _scanCancellation?.Cancel();
}
