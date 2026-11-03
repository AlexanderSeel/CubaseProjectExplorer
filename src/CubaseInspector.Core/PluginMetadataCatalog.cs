using System.Text.Json;

namespace CubaseInspector.Core;

/// <summary>
/// Optional THIRD-PARTY DISCOVERY metadata, never project evidence or a verified VST3 class ID.
/// The open PluginDB seed is published as CC0; external artwork has separate ownership.
/// </summary>
public sealed record PluginMetadataEntry(
    string Name, string Manufacturer, string Category, string? Subcategory,
    string? Description, string? Website, string? ImageUrl, string? ManualUrl,
    string? VideoUrl, IReadOnlyList<string> Formats, IReadOnlyList<string> Tags,
    IReadOnlyList<string> Aliases, string Source, byte[]? ImageBytes = null,
    string? ImageMediaType = null)
{
    public string Caption => Name + " · " + Manufacturer + " · " + Category;
    public string FormatLabel => Formats.Count == 0 ? "Not listed" : string.Join(", ", Formats);
    public string TagLabel => Tags.Count == 0 ? "None listed" : string.Join(", ", Tags);
}

public sealed class PluginMetadataCatalog
{
    public const string SeedUrl =
        "https://raw.githubusercontent.com/blakebratcher/plugindb/main/data/seed.json";
    private readonly IReadOnlyList<PluginMetadataEntry> _items;
    private readonly IReadOnlyDictionary<string, PluginMetadataEntry[]> _normalizedNames;
    public int Count => _items.Count;

    public static PluginMetadataCatalog FromEntries(IEnumerable<PluginMetadataEntry> entries) =>
        new(entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)).Take(10_000).ToArray());

    /// <summary>Browse the whole loaded catalog when the user has not entered a filter.</summary>
    public IReadOnlyList<PluginMetadataEntry> Browse(string? manufacturer = null) => _items
        .Where(item => string.IsNullOrWhiteSpace(manufacturer) ||
            item.Manufacturer.Contains(manufacturer.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    private PluginMetadataCatalog(IReadOnlyList<PluginMetadataEntry> entries)
    {
        _items = entries;
        _normalizedNames = entries
            .SelectMany(entry => new[] { entry.Name }.Concat(entry.Aliases)
                .Select(name => (Key: NormalizeCatalogName(name), Entry: entry)))
            .Where(pair => pair.Key.Length >= 3)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(pair => pair.Entry).Distinct().ToArray(),
                StringComparer.Ordinal);
    }

    /// <summary>Import the public CC0 JSON catalog without running an HTTP server.</summary>
    public static PluginMetadataCatalog FromSeedJson(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 6_000_000)
            throw new ArgumentException("Catalog file empty or exceeds 6 MB.", nameof(json));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("plugins", out var list) || list.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("manufacturers", out var makers) || makers.ValueKind != JsonValueKind.Array)
            throw new JsonException("PluginDB seed.json expected: manufacturers[] and plugins[].");

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in makers.EnumerateArray())
        {
            var slug = Str(m, "slug");
            var name = Str(m, "name");
            if (!string.IsNullOrWhiteSpace(slug) && !string.IsNullOrWhiteSpace(name))
                names[slug] = name;
        }

        var entries = new List<PluginMetadataEntry>();
        foreach (var p in list.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            var name = Str(p, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var makerSlug = Str(p, "manufacturer_slug");
            entries.Add(new PluginMetadataEntry(
                name, makerSlug is not null && names.TryGetValue(makerSlug, out var vendor)
                    ? vendor : "Unspecified manufacturer",
                Str(p, "category") ?? "Unspecified",
                Str(p, "subcategory"), Str(p, "description"),
                SafeUrl(Str(p, "url")), SafeUrl(Str(p, "image_url")),
                SafeUrl(Str(p, "manual_url")), SafeUrl(Str(p, "video_url")),
                Strings(p, "formats"), Strings(p, "tags"), Strings(p, "aliases"), "PluginDB CC0 catalog"));
            if (entries.Count >= 10_000) break;
        }
        return new PluginMetadataCatalog(entries);
    }

    /// <summary>Supports a user-hosted instance of PluginDB's documented GET /api/v1/search?q= endpoint.</summary>
    public static PluginMetadataCatalog FromApiSearchJson(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 4_000_000)
            throw new ArgumentException("API result empty or exceeds 4 MB.", nameof(json));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected PluginDB API search response data[].");
        var entries = new List<PluginMetadataEntry>();
        foreach (var p in list.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(Str(p, "name"))) continue;
            var vendor = p.TryGetProperty("manufacturer", out var m) &&
                         m.ValueKind == JsonValueKind.Object ? Str(m, "name") : null;
            entries.Add(new PluginMetadataEntry(Str(p, "name")!, vendor ?? "Unspecified manufacturer",
                Str(p, "category") ?? "Unspecified", Str(p, "subcategory"),
                Str(p, "description"), SafeUrl(Str(p, "website")), SafeUrl(Str(p, "image_url")),
                SafeUrl(Str(p, "manual_url")), SafeUrl(Str(p, "video_url")),
                Strings(p, "formats"), Strings(p, "tags"), Strings(p, "aliases"), "PluginDB REST API"));
            if (entries.Count >= 100) break;
        }
        return new PluginMetadataCatalog(entries);
    }

    /// <summary>Search name, aliases, vendor, tags; never auto-assert an ambiguous match.</summary>
    public IReadOnlyList<PluginMetadataEntry> Search(string? term, string? manufacturer = null, int take = 60)
    {
        var query = (term ?? "").Trim();
        if (query.Length == 0) return [];
        var vendor = (manufacturer ?? "").Trim();
        return _items.Where(x =>
            (vendor.Length == 0 || x.Manufacturer.Contains(vendor, StringComparison.OrdinalIgnoreCase)) &&
            (x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             x.Aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
             x.Manufacturer.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             x.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(x => x.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(take, 1, 100)).ToArray();
    }

    /// <summary>Advisory product-name/alias search for punctuation variants.
    /// Unlike full catalog search, tags and vendor text never constitute a product hit.</summary>
    public IReadOnlyList<PluginMetadataEntry> SearchProductNames(string? term, int take = 100)
    {
        var key = NormalizeCatalogName(term);
        if (key.Length < 3 || key.Length > 128) return [];
        return _items.Where(p => NormalizeCatalogName(p.Name).Contains(key, StringComparison.Ordinal) ||
            p.Aliases.Any(alias => NormalizeCatalogName(alias).Contains(key, StringComparison.Ordinal)))
            .OrderByDescending(p => NormalizeCatalogName(p.Name) == key ||
                p.Aliases.Any(alias => NormalizeCatalogName(alias) == key))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(take, 1, 100)).ToArray();
    }

    /// <summary>Number of distinct catalog products sharing this normalized name or alias.
    /// This is a discovery diagnostic, not an installation or identity assertion.</summary>
    public int CountExactNameCandidates(string? name)
    {
        var key = NormalizeCatalogName(name);
        return key.Length >= 3 && _normalizedNames.TryGetValue(key, out var matches)
            ? matches.Length : 0;
    }

    public IReadOnlyList<PluginMetadataEntry> FindExactNameCandidates(string? name)
    {
        var key = NormalizeCatalogName(name);
        return key.Length >= 3 && key.Length <= 128 && _normalizedNames.TryGetValue(key, out var matches)
            ? matches.ToArray() : [];
    }

    /// <summary>
    /// Conservative, punctuation-insensitive catalog lookup. A unique product-name match is
    /// advisory metadata only; it never proves a VST instance, installation or class ID.
    /// </summary>
    public PluginMetadataEntry? FindUnambiguousCandidate(string? name, string? manufacturer)
    {
        var key = NormalizeCatalogName(name);
        if (key.Length < 3 || key.Length > 128) return null;
        // Raw CPR UID tokens are not product names.
        if (name is { Length: >= 16 } && name.All(Uri.IsHexDigit)) return null;

        if (!_normalizedNames.TryGetValue(key, out var exact)) return null;

        var vendorKey = NormalizeCatalogName(manufacturer);
        if (vendorKey.Length > 0 && vendorKey != "unknown" &&
            vendorKey != "vendorunknown" && vendorKey != "unspecifiedmanufacturer")
        {
            var byVendor = exact.Where(p => NormalizeCatalogName(p.Manufacturer) == vendorKey).ToArray();
            if (byVendor.Length == 1) return byVendor[0];
            // A conflicting known vendor must not be silently treated as the same product.
            return null;
        }
        // Vendor fields in old CPR files are often missing, generic, or different from
        // the commercial brand. Only fall back when the product identity is unique.
        return exact.Length == 1 ? exact[0] : null;
    }

    private static string NormalizeCatalogName(string? value) =>
        new((value ?? "").Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());

    public PluginMetadataEntry? FindExactCatalogEntry(string? name, string? manufacturer, string? source)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(manufacturer) ||
            string.IsNullOrWhiteSpace(source)) return null;
        var matches = _items.Where(x =>
            x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            x.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase) &&
            x.Source.Equals(source, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static string? SafeUrl(string? value) =>
        value is { Length: <= 2048 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var url) &&
        url.Scheme == Uri.UriSchemeHttps ? url.AbsoluteUri : null;

    private static string? Str(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() : null;

    private static IReadOnlyList<string> Strings(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Array
            ? field.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!).Where(v => !string.IsNullOrWhiteSpace(v))
                .Take(60).ToArray()
            : [];
}
