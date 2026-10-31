namespace CubaseInspector.Core;

/// <summary>Plain-language wording for source confidence in user-facing views.</summary>
public static class EvidencePresentation
{
    public static string Label(EvidenceLevel level) => level switch
    {
        EvidenceLevel.Confirmed => "Read from project export",
        EvidenceLevel.Candidate => "CPR data read · field interpretation varies",
        _ => "Not available"
    };

    public static string Explanation(EvidenceLevel level) => level switch
    {
        EvidenceLevel.Confirmed => "This value is explicitly present in the project export.",
        EvidenceLevel.Candidate => "The project structure was read. Confidence is field-specific; inspect each value's class, source offset, and relationship before treating it as normalized Cubase state.",
        _ => "This project file does not provide a reliable value. It does not mean the value is zero or absent."
    };

    public static string TrackLabel(TrackInfo track) =>
        track.Evidence == EvidenceLevel.Candidate && track.SourceRecordClass is { } className &&
        CprTrackRecordKinds.KindFor(className) is { } kind
            ? $"Serialized {kind} class recognized · links unverified"
            : Label(track.Evidence);

    public static string TrackExplanation(TrackInfo track) =>
        track.Evidence == EvidenceLevel.Candidate && track.SourceRecordClass is { } className &&
        CprTrackRecordKinds.KindFor(className) is { } kind
            ? $"The bounded CPR class header identifies a serialized {kind} class ({className}); Cubase 15.0.40 ASM confirms the class registration and reader dispatch. The displayed name, visible-track status, parent hierarchy, and channel ownership are separate and remain unverified across Cubase versions."
            : Explanation(track.Evidence);

    public static string PluginLabel(PluginReference plugin)
    {
        if (plugin.Evidence != EvidenceLevel.Candidate || plugin.Role is not { } role)
            return Label(plugin.Evidence);
        if (role.StartsWith("Cubase Plugin UID and typed Plugin Name fields", StringComparison.Ordinal))
            return "Stored CPR plug-in UID/name found · identity and owner unverified";
        if (role.StartsWith("Cubase Plugin UID field", StringComparison.Ordinal))
            return "Stored CPR plug-in UID found · name and owner unavailable";
        if (role.StartsWith("Plugin UID marker found", StringComparison.Ordinal))
            return "CPR plug-in UID marker found · values unresolved";
        return Label(plugin.Evidence);
    }

    public static string PluginExplanation(PluginReference plugin) =>
        PluginLabel(plugin) == Label(plugin.Evidence)
            ? Explanation(plugin.Evidence)
            : PluginLabel(plugin) + ". The analyzed Cubase 15.0.40 APluginSlot load path reads the Plugin child and requests Plugin UID / Original Plugin Name. This reader found the stored fields, but has not tied them to a complete slot object, a track owner, a VST format, or a loadable installed version.";

    public static string PluginGroupLabel(ProjectPluginGroup group)
    {
        var labels = group.Uses.Select(use => PluginLabel(use.Reference)).Distinct(StringComparer.Ordinal).ToArray();
        return labels.Length == 1 ? labels[0] : string.Join(" · ", labels);
    }

    public static string PluginGroupExplanation(ProjectPluginGroup group) =>
        string.Join("\n", group.Uses.Select(use => PluginExplanation(use.Reference))
            .Distinct(StringComparer.Ordinal));
}
