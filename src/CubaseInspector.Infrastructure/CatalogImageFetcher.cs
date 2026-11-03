using System.Net;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Downloads only source-declared HTTPS product images, with strict response bounds.</summary>
public sealed class CatalogImageFetcher : IDisposable
{
    public const int MaximumImageBytes = 512 * 1024;
    private readonly HttpClient _http;

    public CatalogImageFetcher() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

    public CatalogImageFetcher(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<(byte[] Bytes, string MediaType)> DownloadAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 || IPAddress.TryParse(uri.Host, out var address) &&
            (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
             address.GetAddressBytes()[0] == 10 || address.GetAddressBytes()[0] == 127 ||
             address.GetAddressBytes()[0] == 192 && address.GetAddressBytes()[1] == 168 ||
             address.GetAddressBytes()[0] == 172 && address.GetAddressBytes()[1] is >= 16 and <= 31))
            throw new InvalidDataException("Product images must use a public HTTPS URL.");

        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is >= HttpStatusCode.MovedPermanently and <= HttpStatusCode.TemporaryRedirect ||
            response.StatusCode == HttpStatusCode.PermanentRedirect)
            throw new InvalidDataException("Image redirects are not followed. Use the source-declared final HTTPS URL.");
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (mediaType is not ("image/png" or "image/jpeg" or "image/webp" or "image/gif"))
            throw new InvalidDataException("The source URL did not return a supported PNG, JPEG, WebP, or GIF image.");
        if (response.Content.Headers.ContentLength is > MaximumImageBytes)
            throw new InvalidDataException("Product image exceeds the 512 KiB per-image limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + read > MaximumImageBytes)
                throw new InvalidDataException("Product image exceeds the 512 KiB per-image limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (output.Length == 0) throw new InvalidDataException("The source returned an empty image.");
        return (output.ToArray(), mediaType);
    }

    public void Dispose() => _http.Dispose();
}
