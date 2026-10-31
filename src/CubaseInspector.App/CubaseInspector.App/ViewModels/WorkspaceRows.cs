using System.Globalization;
using CubaseInspector.Core;
using Microsoft.UI.Xaml.Media;

namespace CubaseInspector.App.ViewModels;

public sealed record TrackDisplayRow(string Name, string Type, string Channel, string Destination,
    string SavedValue, string Status, string Evidence, TrackInfo? Source = null)
{
    public static TrackDisplayRow From(TrackInfo track) => new(
        track.Name,
        track.Kind,
        track.ChannelId ?? "Unknown",
        track.DestinationChannelId ?? "Unknown",
        track.Volume is double volume ? volume.ToString("0.###", CultureInfo.CurrentCulture) : "Unknown",
        $"Mute {Flag(track.Mute)} · Solo {Flag(track.Solo)}",
        EvidencePresentation.Label(track.Evidence), track);

    private static string Flag(bool? value) => value switch { true => "On", false => "Off", _ => "Unknown" };
}

public sealed record PluginReferenceDisplayRow(string Name, string Manufacturer, string Format,
    string Version, string Role, string Owner, string Evidence, PluginReference? Source = null)
{
    public static PluginReferenceDisplayRow From(PluginReference plugin, IReadOnlyDictionary<string, string> owners) => new(
        plugin.Name,
        plugin.Manufacturer ?? "Unknown vendor",
        plugin.Format,
        plugin.Version ?? "Unknown",
        plugin.Role ?? "Role not supplied",
        plugin.TrackId is not null && owners.TryGetValue(plugin.TrackId, out var owner) ? owner :
            plugin.CandidateOwnerName is { Length: > 0 } candidate ? "Nearby CPR record: " + candidate + " (owner unverified)" : "Location not found",
        EvidencePresentation.Label(plugin.Evidence), plugin);
}

public sealed record PluginGroupCardRow(ProjectPluginGroup Group, ImageSource? Artwork)
{
    public string Name => Group.Name;
    public string Vendor => Group.Vendor ?? "Maker not listed";
    public string Format => Group.Format;
    public string Uses => Group.CountLabel;
    public string Locations => Group.LocationSummary;
    public string Reliability => EvidencePresentation.Label(Group.Evidence);
}
