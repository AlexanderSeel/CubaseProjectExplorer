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
    [ObservableProperty] private string _projectLookupStatus = "Select a project plug-in to check saved catalog data or look it up on KVR.";
    [ObservableProperty] private string _source = "No catalog loaded";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _downloadProductImages;
    [ObservableProperty] private PluginMetadataEntry? _selectedEntry;

    public ResettableObservableCollection<PluginMetadataEntry> Results { get; } = [];

    public PluginCatalogMatchAssessment Assess(PluginReference reference) =>
        PluginCatalogMatchService.Assess(_catalog, reference);

    public async Task CheckProjectReferenceAsync(PluginReference reference)
    {
        ProjectLookupStatus = "Checking the saved plug-in database…";
        if (_catalog is null)
            await LoadLibraryCommand.ExecuteAsync(null);

        var assessment = Assess(reference);
        var products = assessment.Candidates.Count == 0 ? "" : "\n" + string.Join("\n", assessment.Candidates
            .Take(5).Select(item => $"• {item.Name} · {item.Manufacturer} · {item.Source}"));
        ProjectLookupStatus = $"{assessment.Kind}: {assessment.Explanation}{products}\nCatalog source: {Source}. This is advisory product metadata, not proof of installed identity or loadability.";
    }

    public async Task GrabKvrForProjectReferenceAsync(PluginReference reference)
    {
        var name = reference.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == "Name not decoded" ||
            name.Length >= 16 && name.All(Uri.IsHexDigit))
        {
            ProjectLookupStatus = "This project reference has no searchable product name. Open KVR research and search using its stored UID/name evidence.";
            return;
        }

        IsBusy = true;
        ProjectLookupStatus = $"Checking saved KVR entries, then reading KVR's public newest-products listing for “{name}”…";
        try
        {
            if (_catalog is null) await LoadLibraryCommand.ExecuteAsync(null);
            var stored = _catalog?.FindExactNameCandidates(name)
                .Where(item => item.Source.Contains("KVR", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            var match = SelectExactKvrMatch(stored, name, reference.Manufacturer, out var ambiguity);
            if (match is null && ambiguity)
            {
                ProjectLookupStatus = $"Several saved KVR products share the name “{name}”; nothing was imported. Use KVR research to choose the right listing.";
                return;
            }

            if (match is null)
            {
                var fetched = await directories.GrabKvrNewestAsync(CancellationToken.None);
                match = SelectExactKvrMatch(fetched, name, reference.Manufacturer, out ambiguity);
                if (match is null)
                {
                    ProjectLookupStatus = ambiguity
                        ? $"KVR's fetched listings contain multiple exact-name products for “{name}”; nothing was imported. Research on KVR and select a listing manually."
                        : $"No exact listing for “{name}” appeared in the KVR newest-products results. Nothing was imported; use Research on KVR for the full database search.";
                    return;
                }

                var imported = PluginMetadataCatalog.FromEntries([match]);
                var combined = await PersistAndLoadCombinedAsync(imported);
                ProjectLookupStatus = $"Added “{match.Name}” by {match.Manufacturer} from {match.Source} to the saved catalog. {combined.Catalog.Count:N0} products are now available. KVR listing metadata does not verify project identity or installation.";
            }
            else
            {
                ProjectLookupStatus = $"“{match.Name}” by {match.Manufacturer} is already in the saved catalog ({match.Source}). KVR metadata does not verify project identity or installation.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or
                                   UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ProjectLookupStatus = $"KVR lookup failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private static PluginMetadataEntry? SelectExactKvrMatch(IEnumerable<PluginMetadataEntry> entries,
        string projectName, string? projectVendor, out bool ambiguous)
    {
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());

        var exact = entries.Where(item => Normalize(item.Name) == Normalize(projectName)).ToArray();
        if (!string.IsNullOrWhiteSpace(projectVendor) &&
            projectVendor is not ("Unknown" or "Unspecified manufacturer"))
        {
            var vendorMatches = exact.Where(item => Normalize(item.Manufacturer) == Normalize(projectVendor)).ToArray();
            if (vendorMatches.Length == 1) { ambiguous = false; return vendorMatches[0]; }
            if (vendorMatches.Length > 1) { ambiguous = true; return null; }
        }
        ambiguous = exact.Length > 1;
        return exact.Length == 1 ? exact[0] : null;
    }

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
