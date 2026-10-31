using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class LibrarySearchEngineTests
{
    private static LibrarySearchDocument Doc(string category, string title, string detail = "", string project = "") =>
        new(category, title, detail, project, "", "Confirmed", "");

    [Fact]
    public void SearchesAcrossFieldsRequiresEveryWordAndRanksExactTitleFirst()
    {
        var docs = new[]
        {
            Doc("Plug-in", "Night Room", "Reverb", "Night Mix"),
            Doc("Project", "Night Mix", "DAWproject", ""),
            Doc("Audio", "room tone.wav", "Stereo", "Night Mix")
        };

        var response = LibrarySearchEngine.Search(docs, "Night Room");

        Assert.Equal(new[] { "Night Room", "room tone.wav" }, response.Results.Select(item => item.Title));
        Assert.Equal(2, response.MatchCount);
        Assert.Empty(LibrarySearchEngine.Search(docs, "Night Unknown").Results);
    }

    [Fact]
    public void AppliesCategoryScopeAndReportsBoundedResults()
    {
        var docs = Enumerable.Range(0, 8).Select(index => Doc(index % 2 == 0 ? "Audio" : "MIDI", $"Kick {index}"));

        var response = LibrarySearchEngine.Search(docs, "Kick", "Audio", limit: 2);

        Assert.Equal(4, response.MatchCount);
        Assert.True(response.Limited);
        Assert.Equal(2, response.Results.Count);
        Assert.All(response.Results, item => Assert.Equal("Audio", item.Category));
    }

    [Fact]
    public void EmptyQueryDoesNotReturnEntireDatabase()
    {
        var response = LibrarySearchEngine.Search([Doc("Project", "Project")], "   ");
        Assert.Empty(response.Results);
    }

    [Fact]
    public void MidiScopeIncludesUnassignedNativeCandidates()
    {
        var docs = new[]
        {
            Doc("MIDI", "Piano"),
            Doc("MIDI candidate", "Unassigned note 1", "Pitch 60", "Session")
        };

        var response = LibrarySearchEngine.Search(docs, "Session", "MIDI");

        Assert.Single(response.Results);
        Assert.Equal("MIDI candidate", response.Results[0].Category);
    }
}
