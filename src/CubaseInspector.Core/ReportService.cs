using System.Text;
namespace CubaseInspector.Core;

public sealed class ReportService : IReportService
{
    public string ShortSummary(ProjectSnapshot p) =>
        $"{p.Name.Replace('\r', ' ').Replace('\n', ' ')} — {p.Format}, evidence: {p.Evidence}; BPM: {(p.Bpm?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}; " +
        (p.Evidence == EvidenceLevel.Candidate
            ? $"candidate track records: {p.Tracks.Count}; candidate plugin tokens: {p.Plugins.Count}; routing: not decoded."
            : $"exported tracks: {p.Tracks.Count}; exported plugin refs: {p.Plugins.Count}; exported routes: {p.Routing.Count}.");

    public string ToMarkdown(ProjectSnapshot p)
    {
        var b = new StringBuilder();
        b.AppendLine($"# {EscapeMd(p.Name)}").AppendLine();
        b.AppendLine($"- Source: `{EscapeMd(p.FilePath)}`");
        b.AppendLine($"- Format: {EscapeMd(p.Format)}");
        b.AppendLine($"- Evidence level: {p.Evidence}");
        b.AppendLine($"- BPM: {(p.Bpm is null ? "Unknown" : p.Bpm.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
        b.AppendLine("- Time signature: " +
            (p.TimeSignatureNumerator.HasValue && p.TimeSignatureDenominator.HasValue
                ? p.TimeSignatureNumerator + "/" + p.TimeSignatureDenominator
                : p.Evidence == EvidenceLevel.Candidate ? "Not decoded" : "Not exported"));
        b.AppendLine($"- Track records: {p.Tracks.Count} (confirmed: {p.Tracks.Count(t => t.Evidence == EvidenceLevel.Confirmed)}, candidate: {p.Tracks.Count(t => t.Evidence == EvidenceLevel.Candidate)}); plugin markers/references: {p.Plugins.Count}; routing edges: {p.Routing.Count}");
        b.AppendLine().AppendLine("## Field availability");
        b.AppendLine(p.Evidence == EvidenceLevel.Candidate
            ? "- Native CPR: track names and plugin UIDs are unverified candidates; confirmed track hierarchy, BPM, inserts, mixer settings, and routing are **not decoded**."
            : "- Structured export: values describe fields present in the export; omissions are not numeric zero and do not prove values absent in the source DAW.");
        foreach (var field in ProjectFieldCoverage.Evaluate(p))
            b.AppendLine("- " + EscapeMd(field.Key) + ": " + field.Value);
        if (p.Metadata is { } metadata)
        {
            b.AppendLine().AppendLine("## Song metadata");
            foreach (var (name, value) in new[]
                     {
                         ("Title", metadata.Title), ("Artist", metadata.Artist), ("Album", metadata.Album),
                         ("Original artist", metadata.OriginalArtist), ("Composer", metadata.Composer),
                         ("Songwriter", metadata.Songwriter), ("Producer", metadata.Producer),
                         ("Arranger", metadata.Arranger), ("Year", metadata.Year), ("Genre", metadata.Genre),
                         ("Copyright", metadata.Copyright), ("Website", metadata.Website), ("Comment", metadata.Comment)
                     })
                if (!string.IsNullOrWhiteSpace(value)) b.AppendLine("- " + name + ": " + EscapeMd(value));
        }
        b.AppendLine().AppendLine("## Tracks");
        foreach (var t in p.Tracks) b.AppendLine($"- {EscapeMd(t.Name)} ({EscapeMd(t.Kind)}; {t.Evidence}; role={EscapeMd(t.ChannelRole ?? "unknown")}; volume={t.Volume?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}; pan={t.Pan?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}; mute={t.Mute?.ToString() ?? "unknown"}; solo={t.Solo?.ToString() ?? "unknown"}; parent={EscapeMd(t.ParentTrackId ?? "root/unknown")}; color={EscapeMd(t.Color ?? "not supplied")}; comment={EscapeMd(t.Comment ?? "not supplied")}; loaded={t.Loaded?.ToString() ?? "unknown"}; class={EscapeMd(t.SourceRecordClass ?? "unknown")}; ARCH offset={t.SourceOffset?.ToString() ?? "unknown"})");
        b.AppendLine().AppendLine("## MIDI note events");
        if (p.MidiNotes is { Count: > 0 } notes)
            foreach (var note in notes)
                b.AppendLine("- Track=" + EscapeMd(note.TrackId) + "; time=" + note.Time.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "; duration=" + note.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; channel=" + note.Channel +
                    "; key=" + note.Key + "; velocity=" + (note.Velocity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown") +
                    "; release velocity=" + (note.ReleaseVelocity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"));
        else b.AppendLine(p.Evidence == EvidenceLevel.Candidate ? "Native CPR MIDI note events not decoded." : "No MIDI note events exported or extracted.");
        b.AppendLine().AppendLine("## Native MIDI note candidates");
        if (p.NativeMidiNoteCandidates is { Count: > 0 } nativeNotes)
        {
            b.AppendLine($"- {nativeNotes.Count:N0} version-scoped RIF2 note-layout candidate(s); track/clip ownership, tick resolution, tempo, and arrangement semantics are unverified.");
            foreach (var note in nativeNotes.Take(256))
                b.AppendLine("- " + note.Evidence + "; position=" + note.PositionTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " ticks; duration=" + note.DurationTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " ticks; pitch=" + note.MidiPitch + "; velocity=" + note.Velocity + "; offset=0x" + note.FileOffset.ToString("X"));
            if (nativeNotes.Count > 256)
                b.AppendLine($"- {nativeNotes.Count - 256:N0} additional candidates omitted here; JSON output retains the bounded full candidate list.");
        }
        else b.AppendLine("No supported version-scoped native MIDI note layout matched.");
        b.AppendLine().AppendLine("## Declared clips and markers");
        if (p.TimelineItems is { Count: > 0 } timeline)
            foreach (var item in timeline)
                b.AppendLine("- " + EscapeMd(item.Kind) + ": " + EscapeMd(item.Name ?? "(unnamed)") +
                    "; time=" + item.Time.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "; duration=" + (item.Duration?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown") +
                    "; reference=" + EscapeMd(item.Reference ?? "none supplied") +
                    "; enabled=" + (item.Enabled?.ToString() ?? "unknown"));
        else
            b.AppendLine(p.Evidence == EvidenceLevel.Candidate
                ? "Native CPR clips and markers not decoded."
                : "No static clip/marker records exported or extracted.");
        b.AppendLine().AppendLine("## Audio media references");
        if (p.MediaReferences is { Count: > 0 } media)
            foreach (var item in media)
                b.AppendLine("- " + EscapeMd(item.Path) +
                    " (external=" + (item.External?.ToString() ?? "unknown") +
                    "; channels=" + (item.Channels?.ToString() ?? "unknown") +
                    "; sample rate=" + (item.SampleRate?.ToString() ?? "unknown") +
                    "; duration=" + (item.Duration?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown") +
                    "; referenced only, file existence not checked)");
        else
            b.AppendLine(p.Evidence == EvidenceLevel.Candidate
                ? "Native CPR media references not decoded."
                : "No audio media references exported or extracted.");
        b.AppendLine().AppendLine("## Routing");
        foreach (var edge in p.Routing)
            b.AppendLine($"- {EscapeMd(edge.FromChannelId)} -> {EscapeMd(edge.ToChannelId)} ({EscapeMd(edge.Kind)}; level={edge.Level?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}; enabled={edge.Enabled?.ToString() ?? "unknown"}; tap={EscapeMd(edge.Tap ?? "unknown")})");
        b.AppendLine().AppendLine("## Plugin references");
        if (p.Evidence == EvidenceLevel.Candidate)
        {
            b.AppendLine("CPR plugin UID/name tokens are candidates; their frequency does not prove insert-instance counts.");
            var named = p.Plugins.Where(plug => plug.Evidence == EvidenceLevel.Candidate &&
                !plug.Id.StartsWith("offset-", StringComparison.Ordinal) &&
                plug.Name != "Name not decoded").ToArray();
            var uidOnly = p.Plugins.Count(plug => plug.Name == "Name not decoded");
            b.AppendLine($"- Distinct named UID/name pairs: {named.Select(v => (v.Id, v.Name)).Distinct().Count()}.");
            b.AppendLine($"- UID-only metadata tokens: {uidOnly}.");
            b.AppendLine($"- Unresolved offset markers: {p.Plugins.Count - named.Length - uidOnly}.");
        }
        foreach (var plugin in p.Plugins) b.AppendLine($"- {EscapeMd(plugin.Name)} (declared reference ID={EscapeMd(plugin.Id)}; instance ID={EscapeMd(plugin.InstanceId ?? "unknown")}; opaque state reference={EscapeMd(plugin.StateReference ?? "not supplied")}; {EscapeMd(plugin.Format)}; {plugin.Evidence}; vendor={EscapeMd(plugin.Manufacturer ?? "unknown")}; version={EscapeMd(plugin.Version ?? "unknown")}; source={EscapeMd(plugin.Role ?? "unknown")}; slot={plugin.Slot?.ToString() ?? "unknown"})");
        b.AppendLine().AppendLine("## Exported channel EQ bands");
        foreach (var band in p.EqBands ?? [])
            b.AppendLine($"- Track={EscapeMd(band.TrackId)}; device={EscapeMd(band.DeviceInstanceId ?? "unknown")}; band={band.BandIndex + 1}; type={EscapeMd(band.Type ?? "unknown")}; filter order={band.Order?.ToString() ?? "unknown"}; frequency={band.Frequency?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} {EscapeMd(band.FrequencyUnit ?? "unit unknown")}; gain={band.Gain?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} {EscapeMd(band.GainUnit ?? "unit unknown")}; Q={band.Q?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} {EscapeMd(band.QUnit ?? "unit unknown")}; enabled={band.Enabled?.ToString() ?? "unknown"}; source={EscapeMd(band.SourcePath)}");
        b.AppendLine().AppendLine("## Exported audio warp points");
        foreach (var point in p.WarpPoints ?? [])
            b.AppendLine($"- Track={EscapeMd(point.TrackId ?? "unknown")}; time={point.Time.ToString(System.Globalization.CultureInfo.InvariantCulture)} {EscapeMd(point.TimeUnit ?? "unit unknown")}; content time={point.ContentTime.ToString(System.Globalization.CultureInfo.InvariantCulture)} {EscapeMd(point.ContentTimeUnit ?? "unit unknown")}; source={EscapeMd(point.SourcePath)}");
        b.AppendLine().AppendLine("## Limitations");
        foreach (var warning in p.Warnings) b.AppendLine($"- {EscapeMd(warning)}");
        return b.ToString();
    }
    public string TracksCsv(ProjectSnapshot p)
    {
        var lines = new List<string> { "Id,Name,Kind,ChannelId,Destination,Volume,Pan,Mute,Solo,Evidence,SourceRecordClass,SourceOffset,ParentTrackId,Color,Comment,Loaded,ChannelRole" };
        lines.AddRange(p.Tracks.Select(t => Csv(t.Id, t.Name, t.Kind, t.ChannelId, t.DestinationChannelId,
            t.Volume?.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Pan?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            t.Mute?.ToString(), t.Solo?.ToString(), t.Evidence.ToString(), t.SourceRecordClass, t.SourceOffset?.ToString(),
            t.ParentTrackId, t.Color, t.Comment, t.Loaded?.ToString(), t.ChannelRole)));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
    public string PluginsCsv(ProjectSnapshot p)
    {
        var lines = new List<string> { "Id,Name,Manufacturer,Format,TrackId,Evidence,Version,Role,Slot,Enabled,InstanceId,StateReference" };
        lines.AddRange(p.Plugins.Select(v => Csv(v.Id, v.Name, v.Manufacturer, v.Format, v.TrackId, v.Evidence.ToString(), v.Version, v.Role, v.Slot?.ToString(), v.Enabled?.ToString(), v.InstanceId, v.StateReference)));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
    private static string Csv(params string?[] values) => string.Join(",", values.Select(v =>
    {
        var value = v ?? "";
        // Prevent spreadsheet formula injection when someone opens CSV exports.
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }));
    private static string EscapeMd(string s) => s.Replace("\\", "\\\\").Replace("`", "\\`").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
