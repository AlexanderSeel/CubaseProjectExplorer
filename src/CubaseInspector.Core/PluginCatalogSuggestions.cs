namespace CubaseInspector.Core;

/// <summary>Advisory third-party catalog suggestions. Never returns verified plugin identities.</summary>
public static class PluginCatalogSuggestions
{
    public static IReadOnlyList<PluginMetadataEntry> Find(
        PluginMetadataCatalog? catalog, string? pluginName, string? vendor, int max = 12)
    {
        if (catalog is null || string.IsNullOrWhiteSpace(pluginName)) return [];
        var name = pluginName.Trim();
        // Unresolved CPR hexadecimal names and generic markers are not searchable product names.
        if (name.Length < 3 || name.Length > 128 ||
            (name.Length >= 16 && name.All(Uri.IsHexDigit))) return [];
        var normalized = Normalize(name);
        if (normalized.Length < 3) return [];
        var candidates = catalog.SearchProductNames(name, take: 100)
            .Where(item => Normalize(item.Name).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                item.Aliases.Any(a => Normalize(a).Contains(normalized, StringComparison.OrdinalIgnoreCase)))
            .Select(item => (Item: item, Rank: Score(item, normalized, vendor))).ToArray();
        return candidates.OrderByDescending(x => x.Rank)
            .ThenBy(x => x.Item.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(max, 1, 25)).Select(x => x.Item).ToArray();
    }

    private static int Score(PluginMetadataEntry item, string name, string? vendor)
    {
        var aliases = item.Aliases.Select(Normalize).ToArray();
        var score = Normalize(item.Name) == name ? 100 :
            aliases.Contains(name) ? 90 : 10;
        if (!string.IsNullOrWhiteSpace(vendor) &&
            item.Manufacturer.Equals(vendor.Trim(), StringComparison.OrdinalIgnoreCase))
            score += 30;
        return score;
    }

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

/// <summary>Explicit external research links, not scraped metadata or an official provider API.</summary>
public static class ExternalPluginDiscovery
{
    public static string? SearchUrl(string provider, string? pluginName, string? vendor)
    {
        var domain = provider switch
        {
            "KVR" => "kvraudio.com",
            "VST3s" => "vst3s.com",
            _ => null
        };
        if (domain is null || string.IsNullOrWhiteSpace(pluginName)) return null;
        var term = pluginName.Trim();
        if (term.Length > 128 || term.Length < 2 ||
            term.Any(char.IsControl)) return null;
        var maker = string.IsNullOrWhiteSpace(vendor) ? "" : vendor.Trim();
        if (maker.Length > 100 || maker.Any(char.IsControl)) maker = "";
        var query = "site:" + domain + " " + term + (maker.Length == 0 ? "" : " " + maker);
        return "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
    }
}
