using System.Globalization;
using CubaseInspector.Core;
using Microsoft.UI.Xaml.Media;

namespace CubaseInspector.App.ViewModels;

public sealed record TrackDisplayRow(string Name, string Type, string Channel, string Destination,
    string SavedValue, string Status, string Evidence, TrackInfo? Source = null)
{
    public string MixSummary
    {
        get
        {
            var values = new List<string>();
            if (Source?.Volume is double volume) values.Add("Level " + volume.ToString("0.##", CultureInfo.CurrentCulture));
            if (Source?.Mute is bool mute) values.Add(mute ? "Muted" : "Unmuted");
            if (Source?.Solo is bool solo && solo) values.Add("Solo");
            return values.Count == 0 ? "—" : string.Join(" · ", values);
        }
    }

    public static string TypeFor(TrackInfo track)
    {
        var kind = CprTrackRecordKinds.KindFor(track.SourceRecordClass ?? track.Kind) ?? track.Kind;
        if (IsMissing(kind)) return "Track";
        var value = kind.Trim();
        if (value.Contains("audio", StringComparison.OrdinalIgnoreCase)) return "Audio";
        if (value.Contains("midi", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("instrument", StringComparison.OrdinalIgnoreCase)) return "Instrument / MIDI";
        if (value.Contains("group", StringComparison.OrdinalIgnoreCase)) return "Group";
        if (value.Contains("fx", StringComparison.OrdinalIgnoreCase) || value.Contains("effect", StringComparison.OrdinalIgnoreCase)) return "FX";
        if (value.Contains("output", StringComparison.OrdinalIgnoreCase)) return "Output";
        return value;
    }

    public static TrackDisplayRow From(TrackInfo track, int ordinal = 1, string? destinationName = null)
    {
        var type = TypeFor(track);
        var name = IsMissing(track.Name) ? $"{type} track {Math.Max(1, ordinal):00}" : track.Name.Trim();
        var output = destinationName ?? (IsMissing(track.DestinationChannelId) ? "—" : "Output not found");
        return new(name, type, "", output,
            track.Volume is double volume ? volume.ToString("0.###", CultureInfo.CurrentCulture) : "",
            string.Join(" · ", new[]
            {
                track.Mute is bool mute ? (mute ? "Muted" : "Unmuted") : null,
                track.Solo == true ? "Solo" : track.Solo == false ? "Not solo" : null
            }.Where(value => value is not null)),
            EvidencePresentation.TrackLabel(track), track);
    }

    public static bool HasSourceValue(string? value) => !IsMissing(value);

    private static bool IsMissing(string? value) => string.IsNullOrWhiteSpace(value) ||
        value.Trim().Trim('<', '>', '(', ')', '[', ']').ToLowerInvariant() is
            "unknown" or "undefined" or "unnamed" or "null" or "none" or "not listed" or "not supplied" or "n/a";
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
    public string Name => IsUnavailable(Group.Name) ||
        Group.Name.Contains("unresolved", StringComparison.OrdinalIgnoreCase) ||
        Group.Name.Contains("not decoded", StringComparison.OrdinalIgnoreCase)
            ? "Plug-in reference" : Group.Name;
    public string Vendor => IsUnavailable(Group.Vendor) ||
        Group.Vendor!.Contains("not listed", StringComparison.OrdinalIgnoreCase) ? "" : Group.Vendor!;
    public string Format => IsUnavailable(Group.Format) ||
        Group.Format.Contains("CPR UID", StringComparison.OrdinalIgnoreCase) ||
        Group.Format.Contains("unverified", StringComparison.OrdinalIgnoreCase) ? "" : Group.Format;
    public string Uses => Group.CountLabel;
    public string Locations => Group.Evidence == EvidenceLevel.Confirmed
        ? Group.LocationSummary
        : Group.Uses.Count == 1 ? "Project location not identified" : "Project locations not identified";
    public string Reliability => EvidencePresentation.PluginGroupLabel(Group);

    private static bool IsUnavailable(string? value) => string.IsNullOrWhiteSpace(value) ||
        value.Trim().ToLowerInvariant() is "unknown" or "undefined" or "unnamed" or "null" or "none" or
            "not listed" or "not supplied" or "n/a";
}
