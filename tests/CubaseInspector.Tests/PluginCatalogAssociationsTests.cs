using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;
public class PluginCatalogAssociationsTests
{
    [Fact]
    public void MalformedOrUnknownVersionSettingsCannotBeSilentlyOverwritten()
    {
        var folder = Directory.CreateTempSubdirectory("catalog-decisions-");
        var path = Path.Combine(folder.FullName, "review.json");
        try
        {
            foreach (var content in new[] { "{not json", "{\"version\":2,\"decisions\":[]}" })
            {
                File.WriteAllText(path, content);
                var decisions = PluginCatalogAssociations.Load(path);
                Assert.False(decisions.IsWritable);
                Assert.NotNull(decisions.LoadWarning);
                Assert.Throws<InvalidOperationException>(() => decisions.Reject("Candidate|CPR|UID:A"));
                Assert.Throws<InvalidOperationException>(() => decisions.Save(path));
                Assert.Equal(content, File.ReadAllText(path));
            }
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void ManualReviewPersistsAndRejectionOverridesPreviousMatch()
    {
        const string json = """
        {"manufacturers":[{"slug":"maker","name":"Maker"}],"plugins":[{"name":"Example","manufacturer_slug":"maker","category":"Effect"}]}
        """;
        var catalog = PluginMetadataCatalog.FromSeedJson(json);
        var entry = Assert.Single(catalog.Search("Example"));
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var decisions = new PluginCatalogAssociations();
            decisions.Confirm("Confirmed|VST3|UID:abc", entry);
            decisions.Save(file);
            var loaded = PluginCatalogAssociations.Load(file);
            Assert.Equal(entry.Name, loaded.Resolve("Confirmed|VST3|UID:abc", catalog)?.Name);
            loaded.Reject("Confirmed|VST3|UID:abc");
            Assert.Null(loaded.Resolve("Confirmed|VST3|UID:abc", catalog));
            Assert.True(loaded.Find("Confirmed|VST3|UID:abc")!.Rejected);
            var staleCatalog = PluginMetadataCatalog.FromSeedJson("""
                {"manufacturers":[{"slug":"other","name":"Other"}],"plugins":[{"name":"Another","manufacturer_slug":"other","category":"Instrument"}]}
                """);
            loaded.Confirm("Confirmed|VST3|UID:abc", entry);
            Assert.Null(loaded.Resolve("Confirmed|VST3|UID:abc", staleCatalog));
            loaded.Clear("Confirmed|VST3|UID:abc");
            Assert.Null(loaded.Find("Confirmed|VST3|UID:abc"));
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
