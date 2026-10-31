using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class PluginComparisonTests
{
    private static ProjectSnapshot Snapshot(params PluginReference[] plugins) =>
        new("test.dawproject", "test", "DAWproject", "15", 120, EvidenceLevel.Confirmed,
            plugins.Where(plugin => plugin.TrackId is not null && plugin.Evidence == EvidenceLevel.Confirmed).Select(plugin => plugin.TrackId!).Distinct()
                .Select(id => new TrackInfo(id, id, "Audio", null, null, null, null, null)).ToArray(), plugins, [], []);

    private static PluginReference Plugin(string id, string name, int? slot, string? owner = "track",
        string? version = "1") =>
        new(id, name, "ACME", "VST3", owner, EvidenceLevel.Confirmed,
            Version: version, Role: "audioFX", Slot: slot, Enabled: true);

    [Fact]
    public void UniqueClassOnOwnerShowsIndependentSlotAndVersionChanges()
    {
        var a = Snapshot(Plugin("id", "Compressor", 0));
        var b = Snapshot(Plugin("id", "Compressor", 2, version: "2"));
        var pluginChanges = new ProjectDiff().Compare(a, b).Where(c => c.Category == "Plugin").ToArray();
        Assert.Equal(2, pluginChanges.Length);
        Assert.Contains(pluginChanges, c => c.Property == "Version" && c.Before == "1" && c.After == "2");
        Assert.Contains(pluginChanges, c => c.Property == "Insert slot" && c.Before == "0" && c.After == "2");
        Assert.All(pluginChanges, c => Assert.Contains("not a stable instance UID", c.MatchQuality));
        Assert.Contains(ProjectChangePresentation.Filter(pluginChanges, "Plugin", ""),
            c => c.ChangeLabel == "Slot moved");
    }

    [Fact]
    public void UniqueExportedSlotWithDifferentClassReportsChangedContent()
    {
        var changes = new ProjectDiff().Compare(
            Snapshot(Plugin("old", "Delay", 1)),
            Snapshot(Plugin("new", "Reverb", 1)));
        var c = Assert.Single(changes.Where(c => c.Category == "Plugin"));
        Assert.Equal("Slot content", c.Property);
        Assert.Contains("Delay", c.Before);
        Assert.Contains("Reverb", c.After);
        Assert.Contains("identity unknown", c.MatchQuality);
        Assert.Equal("Insert changed", ProjectChangePresentation.Describe(c).ChangeLabel);
    }

    [Fact]
    public void SwapOrderWithinOneOwnerIsNotReportedAsReplacement()
    {
        var old = Snapshot(Plugin("class-a", "A", 0), Plugin("class-b", "B", 1));
        var next = Snapshot(Plugin("class-a", "A", 1), Plugin("class-b", "B", 0));
        var changes = new ProjectDiff().Compare(old, next).Where(c => c.Category == "Plugin").ToArray();
        Assert.Equal(2, changes.Length);
        Assert.All(changes, c => Assert.Equal("Insert slot", c.Property));
    }

    [Fact]
    public void DuplicateClassOrUnknownOwnerNeverGetsSpeculativeInstancePair()
    {
        var old = Snapshot(Plugin("same", "Synth", 0), Plugin("same", "Synth", 1),
            Plugin("unowned", "Other", null, null));
        var next = Snapshot(Plugin("same", "Synth", 1), Plugin("same", "Synth", 2),
            Plugin("unowned", "Other", null, null));
        var changes = new ProjectDiff().Compare(old, next).Where(c => c.Category == "Plugin").ToArray();
        Assert.All(changes, c => Assert.Equal("Instance count", c.Property));
        Assert.DoesNotContain(changes, c => c.Property == "Slot content" || c.Property == "Insert slot");
    }

    [Fact]
    public void MovingToDifferentOwnerDoesNotInventUniqueInstanceIdentity()
    {
        var changes = new ProjectDiff().Compare(
            Snapshot(Plugin("id", "EQ", 0, "old-owner")),
            Snapshot(Plugin("id", "EQ", 0, "new-owner")));
        Assert.All(changes.Where(c => c.Category == "Plugin"),
            c => Assert.Equal("Instance count", c.Property));
    }

    [Fact]
    public void CandidatePluginReferencesAreExcludedEvenInConfirmedSnapshot()
    {
        var candidate = Plugin("unknown", "Maybe", 0) with { Evidence = EvidenceLevel.Candidate };
        Assert.Empty(new ProjectDiff().Compare(Snapshot(candidate), Snapshot()));
    }
}
