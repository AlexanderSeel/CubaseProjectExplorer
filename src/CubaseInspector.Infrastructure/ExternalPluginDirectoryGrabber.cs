using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Imports a small, attributed metadata set from the public directory pages.</summary>
public sealed class ExternalPluginDirectoryGrabber
{
    private const int MaxResponseBytes = 8_000_000;
    private static readonly Uri Vst3sBase = new("https://vst3s.com/plugins.js");
    private static readonly Uri Vst3sMore = new("https://vst3s.com/plugins-more.js");
    private static readonly Uri KvrNewest = new("https://www.kvraudio.com/plugins/the-newest-plugins");
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    public async Task<IReadOnlyList<PluginMetadataEntry>> GrabVst3sAsync(CancellationToken ct = default)
    {
        var first = await ReadTextAsync(Vst3sBase, ct);
        var records = ParseVst3sScript(first, "VST3s public database").ToList();
        await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
        var more = await ReadTextAsync(Vst3sMore, ct);
        records.AddRange(ParseVst3sScript(more, "VST3s community additions"));
        return records.GroupBy(item => (item.Name, item.Manufacturer), StringTupleComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).Take(10_000).ToArray();
    }

    public async Task<IReadOnlyList<PluginMetadataEntry>> GrabKvrNewestAsync(CancellationToken ct = default)
    {
        var html = await ReadTextAsync(KvrNewest, ct);
        if (html.Contains("Just a moment...", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("KVR returned an anti-bot challenge. The app will not attempt to bypass it.");

        var results = new List<PluginMetadataEntry>();
        foreach (Match match in Regex.Matches(html,
                     "<a\\b[^>]*href=[\\\"'](?<url>(?:https?://www\\.kvraudio\\.com)?/product/[^\\\"']+)[\\\"'][^>]*>(?<name>.*?)</a>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(2)))
        {
            var title = CleanHtml(match.Groups["name"].Value);
            if (title.Length is < 2 or > 180) continue;
            var name = Regex.Replace(title, @"\s+by\s+.+$", "", RegexOptions.IgnoreCase).Trim();
            var maker = Regex.Match(title, @"\s+by\s+(?<maker>.+)$", RegexOptions.IgnoreCase).Groups["maker"].Value.Trim();
            var link = match.Groups["url"].Value;
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) uri = new Uri(KvrNewest, link);
            results.Add(new PluginMetadataEntry(name, maker.Length == 0 ? "Not listed" : maker,
                "Newest on KVR", null, null, uri.AbsoluteUri, null, null, null,
                [], [], [], "KVR Audio newest-products page"));
            if (results.Count >= 100) break;
        }
        if (results.Count == 0)
            throw new InvalidDataException("KVR page loaded, but no product cards matched the known page structure. The import was not saved.");
        return results.GroupBy(item => (item.Name, item.Manufacturer), StringTupleComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
    }

    public static IReadOnlyList<PluginMetadataEntry> ParseVst3sScript(string script, string source)
    {
        if (string.IsNullOrWhiteSpace(script) || script.Length > MaxResponseBytes)
            throw new InvalidDataException("VST3s data file is empty or exceeds the 8 MB limit.");
        var arrayStart = script.IndexOf("plugins:", StringComparison.Ordinal);
        var pushStart = script.IndexOf("window.VST3S.plugins.push(", StringComparison.Ordinal);
        var start = pushStart >= 0 ? pushStart + "window.VST3S.plugins.push(".Length :
            arrayStart >= 0 ? script.IndexOf('[', arrayStart) + 1 : -1;
        if (start < 0) throw new InvalidDataException("VST3s data script did not contain its expected plugin list.");
        var results = new List<PluginMetadataEntry>();
        var objectCount = 0;
        string? firstObject = null;
        foreach (var block in ReadObjectBlocks(script, start))
        {
            objectCount++;
            firstObject ??= block;
            var id = ReadString(block, "id");
            var name = ReadString(block, "name");
            var maker = ReadString(block, "maker");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(maker)) continue;
            var category = ReadString(block, "cat") ?? "Uncategorized";
            // Keep only the declared image URL; image bytes are fetched only after explicit user opt-in.
            var imageUrl = ReadImageUrl(block);
            results.Add(new PluginMetadataEntry(name, maker, category, null, null,
                "https://vst3s.com/plugins/" + Uri.EscapeDataString(id) + "/", imageUrl, null, null,
                [], [], [], source));
            if (results.Count >= 10_000) break;
        }
        if (results.Count == 0) throw new InvalidDataException($"No VST3s products matched the expected script format ({objectCount} object blocks; id={ReadString(firstObject ?? "", "id") ?? "?"}; name={ReadString(firstObject ?? "", "name") ?? "?"}; maker={ReadString(firstObject ?? "", "maker") ?? "?"}).");
        return results;
    }

    private static string? ReadImageUrl(string block)
    {
        var images = block.IndexOf("images", StringComparison.OrdinalIgnoreCase);
        if (images < 0) return null;
        var start = block.IndexOf('[', images);
        if (start < 0) return null;
        foreach (var image in ReadObjectBlocks(block, start + 1))
        {
            var url = ReadString(image, "src") ?? ReadString(image, "url");
            if (PluginMetadataCatalog.SafeUrl(url) is { } safeUrl) return safeUrl;
        }
        return null;
    }

    private async Task<string> ReadTextAsync(Uri uri, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CubaseProjectExplorer/1.0 (+user-requested plugin directory import)");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is >= HttpStatusCode.MovedPermanently and <= HttpStatusCode.TemporaryRedirect ||
            response.StatusCode == HttpStatusCode.PermanentRedirect)
            throw new InvalidDataException($"{uri.Host} redirected the request; redirects are not followed automatically.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            throw new InvalidDataException($"Response from {uri.Host} exceeds the 8 MB import limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (memory.Length + read > MaxResponseBytes)
                throw new InvalidDataException($"Response from {uri.Host} exceeds the 8 MB import limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    private static IEnumerable<string> ReadObjectBlocks(string text, int start)
    {
        var depth = 0;
        var objectStart = -1;
        var quote = '\0';
        var escaped = false;
        var lineComment = false;
        var blockComment = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (lineComment) { if (ch == '\n') lineComment = false; continue; }
            if (blockComment) { if (ch == '*' && next == '/') { blockComment = false; i++; } continue; }
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == quote) quote = '\0';
                continue;
            }
            if (ch == '/' && next == '/') { lineComment = true; i++; continue; }
            if (ch == '/' && next == '*') { blockComment = true; i++; continue; }
            if (ch is '"' or '\'') { quote = ch; continue; }
            if (ch == '{') { if (depth++ == 0) objectStart = i; continue; }
            if (ch == '}' && depth > 0 && --depth == 0 && objectStart >= 0)
            {
                yield return text[objectStart..(i + 1)];
                objectStart = -1;
            }
            if (depth == 0 && ch == ';' && text.AsSpan(start, i - start).Contains("push(", StringComparison.Ordinal)) yield break;
            if (depth == 0 && ch == ']') yield break;
        }
    }

    private static string? ReadString(string block, string key)
    {
        var quotedToken = "\"" + key + "\"";
        for (var start = 0; start < block.Length;)
        {
            var quoted = block.IndexOf(quotedToken, start, StringComparison.Ordinal);
            var bare = block.IndexOf(key, start, StringComparison.Ordinal);
            if (quoted < 0 && bare < 0) return null;
            var tokenStart = quoted < 0 ? bare : bare < 0 ? quoted : Math.Min(quoted, bare);
            var isQuoted = tokenStart == quoted;
            var tokenEnd = isQuoted ? tokenStart + quotedToken.Length : tokenStart + key.Length;
            if (!isQuoted &&
                ((tokenStart > 0 && (char.IsLetterOrDigit(block[tokenStart - 1]) || block[tokenStart - 1] == '_')) ||
                 (tokenEnd < block.Length && (char.IsLetterOrDigit(block[tokenEnd]) || block[tokenEnd] == '_'))))
            { start = tokenEnd; continue; }
            var cursor = tokenEnd;
            while (cursor < block.Length && char.IsWhiteSpace(block[cursor])) cursor++;
            if (cursor >= block.Length || block[cursor++] != ':') { start = tokenEnd; continue; }
            while (cursor < block.Length && char.IsWhiteSpace(block[cursor])) cursor++;
            if (cursor >= block.Length || block[cursor] != '"') { start = tokenEnd; continue; }
            var valueStart = cursor++;
            var escaped = false;
            while (cursor < block.Length)
            {
                var ch = block[cursor++];
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"')
                {
                    try { return System.Text.Json.JsonSerializer.Deserialize<string>(block[valueStart..cursor]); }
                    catch (System.Text.Json.JsonException) { return null; }
                }
            }
            return null;
        }
        return null;
    }

    private static string CleanHtml(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " "))
        .Replace('\u00a0', ' ').Trim();

    private sealed class StringTupleComparer : IEqualityComparer<(string Name, string Manufacturer)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();
        public bool Equals((string Name, string Manufacturer) x, (string Name, string Manufacturer) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name) && StringComparer.OrdinalIgnoreCase.Equals(x.Manufacturer, y.Manufacturer);
        public int GetHashCode((string Name, string Manufacturer) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Manufacturer));
    }
}
