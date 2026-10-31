using System.Globalization;

namespace CubaseInspector.Core;

/// <summary>Evidence-safe read-only row, with explicit unknown values and no guessed dB unit.</summary>
public sealed record TrackListItem(string Name, string Kind, string Evidence,
    string VolumeText, string DestinationText, string StateText)
{
    public string Id { get; init; } = "";
    public string ParentText { get; init; } = "";
    public string MetadataText { get; init; } = "";
}

public static class TrackListPresentation
{
    public static TrackListItem From(TrackInfo track)
    {
        var confirmed = track.Evidence == EvidenceLevel.Confirmed;
        var volume = !confirmed ? "Unverified" : track.Volume is double v
            ? v.ToString("0.###", CultureInfo.InvariantCulture) : "Not supplied";
        var destination = !confirmed ? "Unverified" :
            string.IsNullOrWhiteSpace(track.DestinationChannelId) ? "Not supplied" : track.DestinationChannelId;
        string State(string label, bool? state) =>
            label + ": " + (!confirmed || state is null ? "Not supplied"
                : state.Value ? "On" : "Off");
        return new TrackListItem(track.Name, track.Kind,
            confirmed ? "Confirmed (exported)" : track.Evidence + " (unverified)",
            volume, destination, State("Mute", track.Mute) + " · " + State("Solo", track.Solo))
        {
            Id = track.Id,
            ParentText = confirmed && track.ParentTrackId is { Length: > 0 } id
                ? "Parent track: " + id : "",
            MetadataText = !confirmed ? "" : string.Join(" · ", new[]
            {
                track.Color is { Length: > 0 } color ? "Color: " + color : null,
                track.Loaded.HasValue ? "Loaded: " + (track.Loaded.Value ? "Yes" : "No") : null,
                track.Comment is { Length: > 0 } comment ? "Note: " + comment : null
            }.Where(text => text is not null))
        };
    }
}
