using System.Text;

namespace CubaseInspector.Core;

public sealed record StudioLogicalRoute(
    string TrackName, string TrackKind, string ChannelId,
    string? DestinationChannelId, bool DestinationInParsedChannels,
    string Assessment);

public sealed record StudioIoReport(
    string ProjectName, EvidenceLevel Evidence,
    IReadOnlyList<StudioLogicalRoute> LogicalRoutes,
    IReadOnlyList<string> MidiOrInstrumentTracks,
    string PhysicalAudioStatus, string PhysicalMidiStatus,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Conservative studio-transfer audit. DAWproject contains logical channel links,
/// not necessarily physical soundcard/ASIO or hardware MIDI port assignments.
/// </summary>
public static class StudioIoPreflightService
{
    public static StudioIoReport Analyze(ProjectSnapshot project)
    {
        var warnings = project.Warnings.ToList();
        if (project.Evidence != EvidenceLevel.Confirmed)
            warnings.Add("Native CPR candidate records cannot establish physical or logical studio port mappings.");

        var confirmed = project.Evidence == EvidenceLevel.Confirmed;
        var channels = confirmed ? project.Tracks
            .Where(t => t.Evidence == EvidenceLevel.Confirmed && !string.IsNullOrWhiteSpace(t.ChannelId))
            .Select(t => t.ChannelId!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        StudioLogicalRoute[] routes = confirmed ? project.Tracks
            .Where(t => t.Evidence == EvidenceLevel.Confirmed && !string.IsNullOrWhiteSpace(t.ChannelId))
            .Select(t =>
            {
                var exists = t.DestinationChannelId is not null && channels.Contains(t.DestinationChannelId);
                var assessment = t.DestinationChannelId is null
                    ? "Destination not exported / unknown"
                    : exists ? "Linked to parsed project channel"
                    : "Endpoint not in parsed channels; hardware or unsupported destination unknown";
                return new StudioLogicalRoute(t.Name, t.Kind, t.ChannelId!,
                    t.DestinationChannelId, exists, assessment);
            }).OrderBy(t => t.TrackName, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        string[] midi = confirmed ? project.Tracks
            .Where(t => t.Evidence == EvidenceLevel.Confirmed &&
                (t.Kind.Contains("MIDI", StringComparison.OrdinalIgnoreCase) ||
                 t.Kind.Contains("Instrument", StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        warnings.Add("A logical channel endpoint absent from the export is NOT proof of a missing interface or MIDI port.");
        return new StudioIoReport(project.Name, project.Evidence, routes, midi,
            "Unknown: physical audio interface/ASIO device and input/output port assignments are not verified.",
            "Unknown: physical MIDI port identities and hardware mappings are not verified.",
            warnings);
    }

    public static string Markdown(StudioIoReport report)
    {
        static string Md(string? v) => (v ?? "Unknown").Replace("|", "\\|")
            .Replace("\r", " ").Replace("\n", " ");
        var b = new StringBuilder("# Studio I/O transfer preflight").AppendLine().AppendLine()
            .AppendLine("- Project: " + Md(report.ProjectName))
            .AppendLine("- Source evidence: " + report.Evidence)
            .AppendLine("- Audio device status: " + Md(report.PhysicalAudioStatus))
            .AppendLine("- MIDI port status: " + Md(report.PhysicalMidiStatus))
            .AppendLine().AppendLine("## Logical channel destinations (NOT physical port mapping)")
            .AppendLine("| Track | Type | Channel ID | Destination | Status |")
            .AppendLine("|---|---|---|---|---|");
        foreach (var item in report.LogicalRoutes)
            b.AppendLine("| " + Md(item.TrackName) + " | " + Md(item.TrackKind) + " | " +
                Md(item.ChannelId) + " | " + Md(item.DestinationChannelId) + " | " +
                Md(item.Assessment) + " |");
        b.AppendLine().AppendLine("## MIDI / instrument tracks (port identities unknown)");
        foreach (var track in report.MidiOrInstrumentTracks) b.AppendLine("- " + Md(track));
        b.AppendLine().AppendLine("## Limitations");
        foreach (var warning in report.Warnings) b.AppendLine("- " + Md(warning));
        return b.ToString();
    }
}
