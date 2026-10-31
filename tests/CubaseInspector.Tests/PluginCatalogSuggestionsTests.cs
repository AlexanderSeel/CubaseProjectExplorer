using CubaseInspector.Core;
using Xunit;
namespace CubaseInspector.Tests;
public class PluginCatalogSuggestionsTests
{
    private const string Catalog = """
        {
          "manufacturers":[{"slug":"fab","name":"Fab"},{"slug":"other","name":"Other"}],
          "plugins":[
            {"name":"Pro-Q 3","manufacturer_slug":"fab","category":"Effect","aliases":["ProQ3"]},
            {"name":"Pro-Q 3","manufacturer_slug":"other","category":"Effect"},
            {"name":"Pro-Q 4","manufacturer_slug":"fab","category":"Effect"},
            {"name":"Noise Gate","manufacturer_slug":"fab","category":"Effect","tags":["Pro-Q 3"]}
          ]
        }
        """;
    [Fact]
    public void ExactNameAndManufacturerRankFirstButSuggestionsRemainMultiple()
    {
        var catalog = PluginMetadataCatalog.FromSeedJson(Catalog);
        var results = PluginCatalogSuggestions.Find(catalog, "Pro-Q 3", "Fab");
        Assert.Equal("Fab", results[0].Manufacturer);
        Assert.Equal("Pro-Q 3", results[0].Name);
        Assert.Contains(results, x => x.Manufacturer == "Other");
        Assert.DoesNotContain(results, x => x.Name == "Noise Gate");
        var compact = PluginCatalogSuggestions.Find(catalog, "ProQ3", "Fab");
        Assert.Contains(compact, x => x.Name == "Pro-Q 3" && x.Manufacturer == "Fab");
        Assert.DoesNotContain(compact, x => x.Name == "Noise Gate");
    }
    [Fact]
    public void DoesNotUseUidAsCatalogSearchOrArbitrarilyMatchTags()
    {
        var catalog = PluginMetadataCatalog.FromSeedJson(Catalog);
        Assert.Empty(PluginCatalogSuggestions.Find(catalog, "0123456789ABCDEF0123456789ABCDEF", null));
        Assert.Empty(PluginCatalogSuggestions.Find(catalog, "", "Fab"));
    }
    [Fact]
    public void AutoEnrichmentMatchesPunctuationAndMissingOrGenericVendor()
    {
        var catalog = PluginMetadataCatalog.FromSeedJson("""
            {
              "manufacturers":[{"slug":"fab","name":"FabFilter"}],
              "plugins":[{"name":"Pro-Q 3","manufacturer_slug":"fab","category":"Effect","aliases":["FabFilter Pro Q3"]}]
            }
            """);
        Assert.Equal("Pro-Q 3", catalog.FindUnambiguousCandidate("ProQ3", null)?.Name);
        Assert.Equal("Pro-Q 3", catalog.FindUnambiguousCandidate("Pro Q 3", "Vendor unknown")?.Name);
        Assert.Equal("Pro-Q 3", catalog.FindUnambiguousCandidate("FabFilter Pro-Q3", "FabFilter")?.Name);
        Assert.Null(catalog.FindUnambiguousCandidate("0123456789ABCDEF0123456789ABCDEF", null));
    }

    [Fact]
    public void IndexedAliasAndCanonicalNameDoNotCountSameProductTwice()
    {
        var catalog = PluginMetadataCatalog.FromSeedJson("""
            {
              "manufacturers":[{"slug":"fab","name":"FabFilter"}],
              "plugins":[{"name":"Pro-Q 3","manufacturer_slug":"fab","category":"Effect",
                          "aliases":["Pro Q3","ProQ3","Pro-Q 3"]}]
            }
            """);
        Assert.Equal("Pro-Q 3", catalog.FindUnambiguousCandidate("ProQ3", null)?.Name);
        Assert.Equal("Pro-Q 3", catalog.FindUnambiguousCandidate("Pro-Q 3", "FabFilter")?.Name);
    }

    [Fact]
    public void AmbiguousCatalogProductIsNeverAutoEnrichedWithoutManufacturer()
    {
        var catalog = PluginMetadataCatalog.FromSeedJson(Catalog);
        Assert.Null(catalog.FindUnambiguousCandidate("ProQ3", null));
        Assert.Equal(2, catalog.CountExactNameCandidates("ProQ3"));
        Assert.Equal(0, catalog.CountExactNameCandidates("Unlisted instrument"));
        Assert.Equal("Fab", catalog.FindUnambiguousCandidate("Pro Q 3", "Fab")?.Manufacturer);
        Assert.Null(catalog.FindUnambiguousCandidate("Pro Q 3", "Different vendor"));
    }

    [Fact]
    public void CatalogAssessmentSeparatesMissingAmbiguousAndVendorConflict()
    {
        var catalog = PluginMetadataCatalog.FromSeedJson(Catalog);
        PluginReference Ref(string name, string? vendor) =>
            new("candidate", name, vendor, "unknown", null, EvidenceLevel.Candidate);

        var ambiguous = PluginCatalogMatchService.Assess(catalog, Ref("Pro-Q 3", null));
        var conflict = PluginCatalogMatchService.Assess(catalog, Ref("Pro-Q 3", "Different vendor"));
        var missing = PluginCatalogMatchService.Assess(catalog, Ref("Unlisted compressor", null));
        var unique = PluginCatalogMatchService.Assess(catalog, Ref("Pro-Q 3", "Fab"));

        Assert.Equal(PluginCatalogMatchKind.Ambiguous, ambiguous.Kind);
        Assert.Equal(PluginCatalogMatchKind.VendorConflict, conflict.Kind);
        Assert.Equal(PluginCatalogMatchKind.NoMatch, missing.Kind);
        Assert.Equal(PluginCatalogMatchKind.NameHint, unique.Kind);
        Assert.All(new[] { ambiguous, conflict, unique }, assessment => Assert.All(assessment.Candidates,
            candidate => Assert.Equal("PluginDB CC0 catalog", candidate.Source)));
    }

    [Fact]
    public void SimilarNameResultsAreLabelledAsSuggestionsNotExactMatches()
    {
        var catalog = PluginMetadataCatalog.FromSeedJson(Catalog);
        var assessment = PluginCatalogMatchService.Assess(catalog,
            new PluginReference("candidate", "Pro-Q", null, "unknown", null, EvidenceLevel.Candidate));

        Assert.Equal(PluginCatalogMatchKind.SimilarNameSuggestions, assessment.Kind);
        Assert.NotEmpty(assessment.Candidates);
        Assert.Contains("No exact", assessment.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("KVR", "kvraudio.com")]
    [InlineData("VST3s", "vst3s.com")]
    public void WebDiscoveryLinkIsScopedAndEscaped(string provider, string domain)
    {
        var url = ExternalPluginDiscovery.SearchUrl(provider, "Pro-Q 3 & Echo", "Fab");
        Assert.NotNull(url);
        Assert.StartsWith("https://www.google.com/search?q=", url);
        Assert.Contains("site%3A" + domain, url);
        Assert.Contains("%26", url);
        Assert.Null(ExternalPluginDiscovery.SearchUrl("unknown", "Synth", null));
    }
}
