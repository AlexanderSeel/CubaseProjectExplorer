using System.Globalization;
using CubaseInspector.Core;
using Microsoft.UI.Xaml.Media;

namespace CubaseInspector.App.ViewModels;

public sealed record TrackDisplayRow(string Name, string Type, string Channel, string Destination,
    string SavedValue, string Status, string Evidence, TrackInfo? Source = null)
{
    public static TrackDisplayRow From(TrackInfo track) => new(
        track.Name,
        CprTrackRecordKinds.KindFor(track.SourceRecordClass ?? track.Kind) ?? track.Kind,
        track.ChannelId ?? "Not listed",
        track.DestinationChannelId ?? "Not assigned",
        track.Volume is double volume ? volume.ToString("0.###", CultureInfo.CurrentCulture) : "Not listed",
        $"Mute {Flag(track.Mute)} · Solo {Flag(track.Solo)}",
        EvidencePresentation.TrackLabel(track), track);

    private static string Flag(bool? value) => value switch { true => "On", false => "Off", _ => "Not listed" };
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
            plugin.CandidateOwnerName is { Length: > 0 } candidate ? "Possible channel: " + candidate : "Location not found",
        EvidencePresentation.PluginLabel(plugin), plugin);
}

public sealed record PluginGroupCardRow(ProjectPluginGroup Group, ImageSource? Artwork)
{
    public string Name => Group.Name;
    public string Vendor => Group.Vendor ?? "Vendor not listed";
    public string Format => Group.Format;
    public string Uses => Group.CountLabel;
    public string Locations => Group.LocationSummary;
    public string Reliability => EvidencePresentation.PluginGroupLabel(Group);
}
