using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubaseInspector.App.Services;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;

namespace CubaseInspector.App.ViewModels;

public sealed partial class PluginCatalogViewModel(PluginCatalogStore store, LibraryDocumentStore documents,
    ExternalPluginDirectoryGrabber directories, CatalogImageFetcher imageFetcher) : ObservableObject
{
    private PluginMetadataCatalog? _catalog;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _manufacturer = "";
    [ObservableProperty] private string _apiBaseAddress = "http://localhost:8000";
    [ObservableProperty] private string _status = "Load the public catalog to search product metadata.";
    [ObservableProperty] private string _source = "No catalog loaded";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _downloadProductImages;
    [ObservableProperty] private PluginMetadataEntry? _selectedEntry;

    public ResettableObservableCollection<PluginMetadataEntry> Results { get; } = [];

    public PluginCatalogMatchAssessment Assess(PluginReference reference) =>
        PluginCatalogMatchService.Assess(_catalog, reference);

    public void SearchFor(string pluginName)
    {
        Search = pluginName;
        Manufacturer = "";
        if (_catalog is null)
        {
            Status = "Loading the saved offline catalog to search this plug-in…";
            if (!IsBusy && store.HasCachedCatalog) _ = LoadOfflineCommand.ExecuteAsync(null);
            else if (!IsBusy) Status = "No saved catalog is available. Load the catalog to search product information.";
        }
        if (Results.FirstOrDefault(entry => entry.Name.Equals(pluginName, StringComparison.OrdinalIgnoreCase)) is { } exact)
            SelectedEntry = exact;
    }

    partial void OnSearchChanged(string value) => RefreshResults();
    partial void OnManufacturerChanged(string value) => RefreshResults();

    [RelayCommand]
    private async Task LoadOfflineAsync()
    {
        IsBusy = true;
        try
        {
            var result = await store.LoadCachedAsync();
            SetCatalog(result.Catalog, result.Source);
            var combined = await PersistAndLoadCombinedAsync(result.Catalog);
            Status = $"Loaded {result.Catalog.Count:N0} products; the database now contains {combined.Catalog.Count:N0} combined products from all sources.{combined.ImageStatus}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        IsBusy = true;
        Status = "Downloading the public catalog…";
        try
        {
            var result = await store.DownloadAsync();
            SetCatalog(result.Catalog, result.Source);
            var combined = await PersistAndLoadCombinedAsync(result.Catalog);
            Status = $"Loaded {result.Catalog.Count:N0} products; the database now contains {combined.Catalog.Count:N0} combined products from all sources.{combined.ImageStatus}";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            Status = $"Catalog unavailable: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SearchApiAsync()
    {
        IsBusy = true;
        Status = "Searching the configured PluginDB API…";
        try
        {
            var result = await store.SearchApiAsync(ApiBaseAddress, Search);
            SetCatalog(result.Catalog, result.Source);
            var combined = await PersistAndLoadCombinedAsync(result.Catalog);
            Status = $"Imported {result.Catalog.Count:N0} API results; the database now contains {combined.Catalog.Count:N0} combined products from all sources.{combined.ImageStatus} Advisory metadata only; no installation or project identity is verified.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or
                                   System.Text.Json.JsonException or ArgumentException)
        {
            Status = "PluginDB API search failed: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void SetCatalog(PluginMetadataCatalog catalog, string source)
    {
        _catalog = catalog;
        Source = source;
        RefreshResults();
    }

    private async Task<(PluginMetadataCatalog Catalog, string ImageStatus)> PersistAndLoadCombinedAsync(PluginMetadataCatalog imported)
    {
        var products = imported.Browse().ToArray();
        var imagesSaved = 0;
        var imagesSkipped = 0;
        long imageBytes = 0;
        if (DownloadProductImages)
        {
            var downloadedByUrl = new Dictionary<string, (byte[] Bytes, string MediaType)>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < products.Length && imagesSaved < 200 && imageBytes < 64L * 1024 * 1024; index++)
            {
                var product = products[index];
                if (string.IsNullOrWhiteSpace(product.ImageUrl)) continue;
                try
                {
                    if (!downloadedByUrl.TryGetValue(product.ImageUrl, out var image))
                    {
                        image = await imageFetcher.DownloadAsync(product.ImageUrl);
                        if (imageBytes + image.Bytes.Length > 64L * 1024 * 1024)
                        {
                            imagesSkipped++;
                            continue;
                        }
                        downloadedByUrl.Add(product.ImageUrl, image);
                        imageBytes += image.Bytes.Length;
                    }
                    products[index] = product with { ImageBytes = image.Bytes, ImageMediaType = image.MediaType };
                    imagesSaved++;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException)
                {
                    imagesSkipped++;
                }
            }
            var eligibleCount = products.Count(product => !string.IsNullOrWhiteSpace(product.ImageUrl));
            imagesSkipped += Math.Max(0, eligibleCount - imagesSaved - imagesSkipped);
        }

        await documents.SaveCatalogProductsAsync(products);
        var saved = await documents.GetCatalogProductsAsync();
        var entries = saved.Select(item => new PluginMetadataEntry(item.Name, item.Manufacturer,
            item.Category, item.Subcategory, item.Description, item.Website, item.ImageUrl,
            item.ManualUrl, item.VideoUrl, item.Formats, item.Tags, item.Aliases, item.Source,
            item.ImageBytes, item.ImageMediaType)).ToArray();
        var combined = PluginMetadataCatalog.FromEntries(entries);
        SetCatalog(combined, $"Library database · {saved.Select(item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} sources combined");
        var imageStatus = DownloadProductImages
            ? $" Saved {imagesSaved:N0} supplied product images ({imageBytes / 1024:N0} KiB); skipped {imagesSkipped:N0}."
            : string.Empty;
        return (combined, imageStatus);
    }

    [RelayCommand]
    private async Task LoadLibraryAsync()
    {
        IsBusy = true;
        Status = "Loading saved catalog products from the library database…";
        try
        {
            var saved = await documents.GetCatalogProductsAsync();
            var entries = saved.Select(item => new PluginMetadataEntry(item.Name, item.Manufacturer,
                item.Category, item.Subcategory, item.Description, item.Website, item.ImageUrl,
                item.ManualUrl, item.VideoUrl, item.Formats, item.Tags, item.Aliases, item.Source,
                item.ImageBytes, item.ImageMediaType)).ToArray();
            SetCatalog(PluginMetadataCatalog.FromEntries(entries), "Configured library database");
            Status = entries.Length == 0
                ? "No catalog products are saved in this database yet. Load PluginDB CC0 data or grab an external directory."
                : $"Loaded {entries.Length:N0} saved catalog products from the configured library database.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = "Saved catalog could not be loaded: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task GrabVst3sAsync() => await GrabDirectoryAsync(
        directories.GrabVst3sAsync, "VST3s public catalog");

    [RelayCommand]
    private async Task GrabKvrAsync() => await GrabDirectoryAsync(
        directories.GrabKvrNewestAsync, "KVR newest plug-ins");

    private async Task GrabDirectoryAsync(Func<CancellationToken, Task<IReadOnlyList<PluginMetadataEntry>>> grab,
        string label)
    {
        IsBusy = true;
        Status = $"Reading {label}…";
        try
        {
            var products = await grab(CancellationToken.None);
            var catalog = PluginMetadataCatalog.FromEntries(products);
            var combined = await PersistAndLoadCombinedAsync(catalog);
            Status = $"Imported {products.Count:N0} products from {label}; database browse now combines {combined.Catalog.Count:N0} products across all sources.{combined.ImageStatus}";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ArgumentException)
        {
            Status = $"{label} import failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private void RefreshResults()
    {
        if (_catalog is null) { Results.ReplaceAll([]); return; }
        var matches = string.IsNullOrWhiteSpace(Search)
            ? _catalog.Browse(Manufacturer)
            : _catalog.Search(Search, Manufacturer, 10_000);
        Results.ReplaceAll(matches);
        if (Results.Count == 0) Status = "No catalog products match this search.";
        else if (string.IsNullOrWhiteSpace(Search)) Status = $"Browsing all {Results.Count:N0} catalog products · advisory metadata only";
        else Status = $"{Results.Count:N0} matching catalog products · advisory metadata only";
        if (SelectedEntry is not null && !Results.Contains(SelectedEntry)) SelectedEntry = null;
    }
}
