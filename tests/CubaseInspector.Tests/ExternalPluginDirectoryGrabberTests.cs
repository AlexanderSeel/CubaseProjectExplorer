using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class ExternalPluginDirectoryGrabberTests
{
    [Fact]
    public void ParsesVst3sPluginArrayButKeepsImagesOptInAndDoesNotCopyUnlicensedDescriptions()
    {
        const string script = "window.VST3S = { plugins: [/* record */ { id: \"eq-one\", name: \"EQ One\", maker: \"Maker\", cat: \"eq\", desc: [\"brace } inside text\"], images: [{src: \"https://img.test/art.png\"}] } ] };";
        var plugin = Assert.Single(ExternalPluginDirectoryGrabber.ParseVst3sScript(script, "VST3s public database"));
        Assert.Equal("EQ One", plugin.Name);
        Assert.Equal("Maker", plugin.Manufacturer);
        Assert.Equal("eq", plugin.Category);
        Assert.Null(plugin.Description);
        Assert.Equal("https://img.test/art.png", plugin.ImageUrl);
        Assert.Equal("VST3s public database", plugin.Source);
    }

    [Fact]
    public void ParsesVst3sCommunityPushData()
    {
        const string script = "window.VST3S.plugins.push({\"id\":\"synth-one\",\"name\":\"Synth One\",\"maker\":\"Maker\",\"cat\":\"synth\"},{\"id\":\"synth-two\",\"name\":\"Synth Two\",\"maker\":\"Maker\",\"cat\":\"synth\"});";
        var plugins = ExternalPluginDirectoryGrabber.ParseVst3sScript(script, "VST3s community additions");
        Assert.Equal(["Synth One", "Synth Two"], plugins.Select(plugin => plugin.Name));
    }
}
