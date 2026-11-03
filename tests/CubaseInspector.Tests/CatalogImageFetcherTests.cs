using System.Net;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CatalogImageFetcherTests
{
    [Fact]
    public async Task DownloadsBoundedSourceDeclaredHttpsImage()
    {
        using var fetcher = new CatalogImageFetcher(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
            }
        }));

        var image = await fetcher.DownloadAsync("https://images.example.test/plugin.png");

        Assert.Equal([1, 2, 3], image.Bytes);
        Assert.Equal("image/png", image.MediaType);
    }

    [Theory]
    [InlineData("http://images.example.test/plugin.png")]
    [InlineData("https://127.0.0.1/plugin.png")]
    public async Task RejectsNonPublicOrNonHttpsImageUrls(string url)
    {
        using var fetcher = new CatalogImageFetcher(new StubHandler(() => throw new InvalidOperationException("Request should not be sent.")));
        await Assert.ThrowsAsync<InvalidDataException>(() => fetcher.DownloadAsync(url));
    }

    [Fact]
    public async Task RejectsRedirectsAndUnsupportedContentTypes()
    {
        using var redirects = new CatalogImageFetcher(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.Redirect)));
        await Assert.ThrowsAsync<InvalidDataException>(() => redirects.DownloadAsync("https://images.example.test/plugin.png"));

        using var unsupported = new CatalogImageFetcher(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not an image", System.Text.Encoding.UTF8, "text/html")
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => unsupported.DownloadAsync("https://images.example.test/plugin.png"));
    }

    private sealed class StubHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response());
    }
}
