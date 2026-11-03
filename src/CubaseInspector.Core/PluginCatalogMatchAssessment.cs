namespace CubaseInspector.Core;

public enum PluginCatalogMatchKind
{
    CatalogNotLoaded,
    NameUnavailable,
    NoMatch,
    NameHint,
    Ambiguous,
    VendorConflict,
    SimilarNameSuggestions
}

public sealed record PluginCatalogMatchAssessment(
    PluginCatalogMatchKind Kind,
    IReadOnlyList<PluginMetadataEntry> Candidates,
    string Explanation);

/// <summary>Explains advisory product-name matches without asserting project identity or installation.</summary>
public static class PluginCatalogMatchService
{
    public static PluginCatalogMatchAssessment Assess(PluginMetadataCatalog? catalog, PluginReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (catalog is null)
            return new(PluginCatalogMatchKind.CatalogNotLoaded, [], "Load the catalog to compare names and aliases.");
        var name = reference.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == "Name not decoded" ||
            name.Length >= 16 && name.All(Uri.IsHexDigit))
            return new(PluginCatalogMatchKind.NameUnavailable, [], "The project reference has no searchable product name.");

        var exact = catalog.FindExactNameCandidates(name);
        if (exact.Count > 0)
        {
            if (IsKnownVendor(reference.Manufacturer))
            {
                var vendor = Normalize(reference.Manufacturer);
                var vendorHits = exact.Where(item => Normalize(item.Manufacturer) == vendor).ToArray();
                if (vendorHits.Length == 1)
                    return new(PluginCatalogMatchKind.NameHint, vendorHits,
                        "One product name and vendor match; advisory metadata only, not verified plug-in identity.");
                if (vendorHits.Length > 1)
                    return new(PluginCatalogMatchKind.Ambiguous, vendorHits,
                        "Several catalog products share this name and vendor; review the candidates.");
                return new(PluginCatalogMatchKind.VendorConflict, exact,
                    "The product name exists in the catalog, but its listed manufacturer conflicts with the project vendor.");
            }
            if (exact.Count == 1)
                return new(PluginCatalogMatchKind.NameHint, exact,
                    "One product name matches; the project vendor is unavailable, so this remains an advisory hint.");
            return new(PluginCatalogMatchKind.Ambiguous, exact,
                "Several catalog products share this name or alias; no automatic choice was made.");
        }

        var similar = PluginCatalogSuggestions.Find(catalog, name, reference.Manufacturer, 5);
        return similar.Count > 0
            ? new(PluginCatalogMatchKind.SimilarNameSuggestions, similar,
                "No exact name or alias match. Similar product names are shown for manual review.")
            : new(PluginCatalogMatchKind.NoMatch, [], "No exact or similar product name was found in the loaded catalog.");
    }

    private static bool IsKnownVendor(string? vendor) => !string.IsNullOrWhiteSpace(vendor) &&
        Normalize(vendor) is not ("unknown" or "vendorunknown" or "unspecifiedmanufacturer");

    private static string Normalize(string? value) => new((value ?? "").Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant).ToArray());
}
