using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprPluginMetadataTests
{
    private const string Uid = "56535473796C3173796C656E74683100";

    private static byte[] Token(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s + "\0");
        return [(byte)bytes.Length, .. bytes];
    }

    private static byte[] Fixture(string displayed, string? original = null)
    {
        var b = new List<byte>();
        b.AddRange("RIFF__NUND"u8.ToArray());
        b.AddRange("Plugin UID\0"u8.ToArray());
        b.AddRange(new byte[22]);
        b.AddRange(Token(Uid));
        b.AddRange(new byte[3]);
        b.AddRange(Token("Plugin Name"));
        b.AddRange(new byte[5]);
        b.AddRange(Token(displayed));
        if (original is not null)
        {
            b.AddRange(new byte[3]);
            b.AddRange(Token("Original Plugin Name"));
            b.AddRange(new byte[5]);
            b.AddRange(Token(original));
        }
        return b.ToArray();
    }

    [Fact]
    public void ReadsStoredNameAndUidWithoutAssertingInstallation()
    {
        var tokens = CprPluginMetadataReader.Extract(Fixture("Sylenth1"));
        var plugin = Assert.Single(tokens);
        Assert.Equal("Sylenth1", plugin.Name);
        Assert.Equal(Uid, plugin.Id);
        Assert.Equal(EvidenceLevel.Candidate, plugin.Evidence);
        Assert.Contains("CPR", plugin.Format);
        Assert.Null(plugin.TrackId);
        Assert.Contains("0x", plugin.Role);
    }

    [Fact]
    public void RenamedInstrumentUsesOriginalPluginName()
    {
        var plugin = Assert.Single(CprPluginMetadataReader.Extract(
            Fixture("My Bass Instrument", "Sylenth1")));
        Assert.Equal("Sylenth1", plugin.Name);
        Assert.Contains("My Bass Instrument", plugin.Role);
        Assert.Equal(EvidenceLevel.Candidate, plugin.Evidence);
    }

    [Fact]
    public void UidWithoutPluginNameIsPreservedAsAnUnverifiedToken()
    {
        var project = new List<byte>();
        project.AddRange("RIFF__NUND"u8.ToArray());
        project.AddRange("Plugin UID\0"u8.ToArray());
        project.AddRange(new byte[22]);
        project.AddRange(Token(Uid));
        project.AddRange(new byte[3]);
        project.AddRange(Token("Unknown Field"));
        var hit = Assert.Single(CprPluginMetadataReader.Extract(project.ToArray()));
        Assert.Equal(Uid, hit.Id);
        Assert.Equal("Name not decoded", hit.Name);
        Assert.Equal(EvidenceLevel.Candidate, hit.Evidence);
        Assert.Contains("no typed Plugin Name", hit.Role);
    }

    [Fact]
    public void UnsupportedPropertyLayoutRemainsUnresolved()
    {
        var plugin = Assert.Single(CprPluginMetadataReader.Extract(
            "Plugin UID\0Binary data without typed values"u8.ToArray()));
        Assert.StartsWith("offset-", plugin.Id);
        Assert.Contains("Unresolved", plugin.Name);
        Assert.Equal(EvidenceLevel.Candidate, plugin.Evidence);
    }
}
