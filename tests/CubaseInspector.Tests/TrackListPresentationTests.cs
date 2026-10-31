using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class TrackListPresentationTests
{
    [Fact]
    public void MissingSavedChannelValuesAreNotSilentlyDisplayedAsZero()
    {
        var item = TrackListPresentation.From(
            new TrackInfo("t1", "Lead", "Audio", "ch1", null, null, null, null));
        Assert.Equal("Not supplied", item.VolumeText);
        Assert.Equal("Not supplied", item.DestinationText);
        Assert.Contains("Mute: Not supplied", item.StateText);
    }

    [Fact]
    public void ConfirmedZeroValuesRemainVisibleAndAreNotTreatedAsMissing()
    {
        var item = TrackListPresentation.From(
            new TrackInfo("t2", "Bass", "Audio", "ch2", "bus", 0, false, true));
        Assert.Equal("0", item.VolumeText);
        Assert.Equal("bus", item.DestinationText);
        Assert.Contains("Mute: Off", item.StateText);
        Assert.Contains("Solo: On", item.StateText);
    }

    [Fact]
    public void OnlyConfirmedStructuredTracksExposeDeclaredParent()
    {
        var track = new TrackInfo("child", "Voice", "Audio", null, null, null, null, null,
            ParentTrackId: "parent");
        Assert.Equal("Parent track: parent", TrackListPresentation.From(track).ParentText);
        var described = TrackListPresentation.From(track with
        {
            Color = "#AABBCC", Comment = "Filtered", Loaded = false
        });
        Assert.Contains("#AABBCC", described.MetadataText);
        Assert.Contains("Loaded: No", described.MetadataText);
        Assert.Contains("Filtered", described.MetadataText);
        var candidate = TrackListPresentation.From(track with
        {
            Evidence = EvidenceLevel.Candidate,
            Color = "#AABBCC",
            Loaded = false
        });
        Assert.Empty(candidate.ParentText);
        Assert.Empty(candidate.MetadataText);
    }

    [Fact]
    public void UnverifiedCandidateRowsNeverClaimSavedMixerValues()
    {
        var item = TrackListPresentation.From(
            new TrackInfo("c", "Crash", "Audio", null, "x", 0.8, true, true,
                EvidenceLevel.Candidate));
        Assert.Equal("Unverified", item.VolumeText);
        Assert.Equal("Unverified", item.DestinationText);
        Assert.Contains("Candidate", item.Evidence);
        Assert.Contains("Not supplied", item.StateText);
    }
}
