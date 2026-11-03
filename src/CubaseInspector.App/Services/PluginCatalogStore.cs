using System.Net;
using System.Net.Http;
using CubaseInspector.Core;

namespace CubaseInspector.App.Services;

/// <summary>Loads the public catalog only on user request and keeps a bounded offline copy.</summary>
public sealed class PluginCatalogStore
{
    private const int MaximumBytes = 6_000_000;
    private static readonly Uri SeedUri = new(PluginMetadataCatalog.SeedUrl);
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CubaseProjectExplorer", "catalog", "plugindb-seed.json");

    public bool HasCachedCatalog => File.Exists(_cachePath);

    public async Task<(PluginMetadataCatalog Catalog, string Source)> LoadCachedAsync(CancellationToken cancellationToken = default)
    {
        var json = await ReadBoundedFileAsync(_cachePath, cancellationToken);
        var catalog = await Task.Run(() => PluginMetadataCatalog.FromSeedJson(json), cancellationToken);
        return (catalog, "Offline cache · PluginDB CC0 data");
    }

    public async Task<(PluginMetadataCatalog Catalog, string Source)> DownloadAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SeedUri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is >= HttpStatusCode.MovedPermanently and <= HttpStatusCode.TemporaryRedirect ||
            response.StatusCode == HttpStatusCode.PermanentRedirect)
            throw new InvalidDataException("Catalog redirects are not followed. Use the published PluginDB seed URL directly.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumBytes)
            throw new InvalidDataException("Catalog download exceeds the 6 MB limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (memory.Length + read > MaximumBytes)
                throw new InvalidDataException("Catalog download exceeds the 6 MB limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var json = System.Text.Encoding.UTF8.GetString(memory.ToArray());
        var catalog = await Task.Run(() => PluginMetadataCatalog.FromSeedJson(json), cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var temporaryPath = _cachePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _cachePath, overwrite: true);
        return (catalog, "Downloaded just now · PluginDB CC0 data");
    }

    public async Task<(PluginMetadataCatalog Catalog, string Source)> SearchApiAsync(
        string baseAddress, string query, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var baseUri) ||
            baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0 ||
            !(baseUri.Scheme == Uri.UriSchemeHttps ||
              baseUri.Scheme == Uri.UriSchemeHttp &&
              (baseUri.IsLoopback || baseUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Use an HTTPS PluginDB base URL, or HTTP localhost for a local instance.", nameof(baseAddress));
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 160)
            throw new ArgumentException("Enter a product, manufacturer, or alias to search (up to 160 characters).", nameof(query));

        var endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/api/v1/search?q=" + Uri.EscapeDataString(query.Trim()));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is >= HttpStatusCode.MovedPermanently and <= HttpStatusCode.TemporaryRedirect ||
            response.StatusCode == HttpStatusCode.PermanentRedirect)
            throw new InvalidDataException("API redirects are not followed. Use the final HTTPS address directly.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 4_000_000)
            throw new InvalidDataException("PluginDB API response exceeds the 4 MB safety limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (memory.Length + read > 4_000_000)
                throw new InvalidDataException("PluginDB API response exceeds the 4 MB safety limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var catalog = await Task.Run(() => PluginMetadataCatalog.FromApiSearchJson(
            System.Text.Encoding.UTF8.GetString(memory.ToArray())), cancellationToken);
        return (catalog, "PluginDB REST API · user-hosted instance · advisory metadata");
    }

    private static async Task<string> ReadBoundedFileAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("No offline catalog is cached yet.", path);
        if (info.Length > MaximumBytes) throw new InvalidDataException("Cached catalog exceeds the 6 MB limit.");
        return await File.ReadAllTextAsync(path, cancellationToken);
    }
}
