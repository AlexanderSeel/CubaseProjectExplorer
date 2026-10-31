using System.Text.Json;
using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprUidLabelBookTests
{
    private const string Uid = "56535473796C3173796C656E74683100";

    [Fact]
    public void LocalManualLabelCanBeAddedExportedReloadedAndRemoved()
    {
        var original = CprUidLabelBook.ParseOrEmpty(null);
        var named = CprUidLabelBook.Assign(original, Uid.ToLowerInvariant(), "  My old synth  ");
        Assert.Empty(original);
        Assert.Equal("My old synth", named[Uid]);
        var json = CprUidLabelBook.ToJson(named);
        var imported = CprUidLabelBook.ParseOrEmpty(json);
        Assert.Equal("My old synth", imported[Uid]);

        var candidate = new PluginReference(Uid, "Name not decoded", null,
            "CPR UID (name not decoded)", null, EvidenceLevel.Candidate);
        Assert.Equal("My old synth", CprUidLabelBook.Find(imported, candidate));
        Assert.Null(CprUidLabelBook.Find(imported,
            candidate with { Evidence = EvidenceLevel.Confirmed }));
        Assert.Null(CprUidLabelBook.Find(imported,
            candidate with { Id = "offset-1" }));

        var removed = CprUidLabelBook.Remove(imported, Uid);
        Assert.Empty(removed);
        Assert.Single(imported);
    }

    [Fact]
    public void InvalidIdentifiersAndUnsafeNamesCannotBeStored()
    {
        var empty = CprUidLabelBook.ParseOrEmpty(null);
        Assert.Throws<ArgumentException>(() => CprUidLabelBook.Assign(empty, "offset-90", "Example"));
        Assert.Throws<ArgumentException>(() => CprUidLabelBook.Assign(empty, Uid, "\r\nExample"));
        Assert.Throws<ArgumentException>(() => CprUidLabelBook.Assign(empty, Uid, new string('X', 121)));
        Assert.Throws<ArgumentException>(() => CprUidLabelBook.Assign(empty, Uid, "   "));
    }

    [Fact]
    public void UnknownSchemaDoesNotOverwriteExistingDataSilently()
    {
        Assert.Throws<JsonException>(() => CprUidLabelBook.ParseOrEmpty(
            "{\"schemaVersion\":2,\"labels\":[]}"));
        Assert.Throws<JsonException>(() => CprUidLabelBook.ParseOrEmpty(
            "{\"schemaVersion\":999999999999999999999,\"labels\":[]}"));
    }

    [Fact]
    public void InvalidRowsAreRejectedSoSettingsCannotBeSilentlyRewritten()
    {
        var json = "{\"schemaVersion\":1,\"labels\":[" +
                   "{\"uid\":\"offset-A\",\"label\":\"Impossible\"}," +
                   "{\"uid\":\"" + Uid + "\",\"label\":\"Official label?\"}," +
                   "{\"uid\":\"" + Uid + "\",\"label\":\"Different duplicate\"}]}";
        Assert.Throws<JsonException>(() => CprUidLabelBook.ParseOrEmpty(json));
    }
}
