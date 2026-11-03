namespace CubaseInspector.Core;

/// <summary>
/// Read-only report of fields a normalized project currently exposes. It distinguishes
/// "not decoded" native CPR fields from nullable fields and does not infer physical DAW state.
/// </summary>
public static class ProjectFieldCoverage
{
    public static IReadOnlyDictionary<string, ProjectFieldState> Evaluate(ProjectSnapshot project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var nativeCandidate = project.Evidence != EvidenceLevel.Confirmed;
        return new Dictionary<string, ProjectFieldState>(StringComparer.Ordinal)
        {
            ["Saved-by version"] = project.CreatorVersion is null
                ? ProjectFieldState.Unknown : nativeCandidate
                    ? ProjectFieldState.Candidate : ProjectFieldState.Present,
            ["Tempo"] = nativeCandidate
                ? (project.Bpm.HasValue || HasCandidate(project, "Tempo event BPM", "Tempo track BPM", "Musical tempo BPM")
                    ? ProjectFieldState.Candidate : ProjectFieldState.Unknown)
                : project.Bpm.HasValue ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Time signature"] = nativeCandidate
                ? (project.TimeSignatureNumerator.HasValue && project.TimeSignatureDenominator.HasValue || HasCandidate(project, "Time signature event")
                    ? ProjectFieldState.Candidate : ProjectFieldState.Unknown)
                : project.TimeSignatureNumerator.HasValue && project.TimeSignatureDenominator.HasValue
                    ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Sample rate"] = nativeCandidate
                ? (HasCandidate(project, "Sample rate") ? ProjectFieldState.Candidate : ProjectFieldState.Unknown)
                : project.MediaReferences?.Any(media => media.SampleRate.HasValue) == true
                    ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Sample size"] = nativeCandidate
                ? (HasCandidate(project, "Sample size") ? ProjectFieldState.Candidate : ProjectFieldState.Unknown)
                : ProjectFieldState.NotExported,
            ["Track identity"] = nativeCandidate
                ? (project.Tracks.Count > 0 ? ProjectFieldState.Candidate : ProjectFieldState.Unknown)
                : project.Tracks.Count > 0 ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Plugin references"] = nativeCandidate
                ? (project.Plugins.Count > 0 ? ProjectFieldState.Candidate : ProjectFieldState.Unknown)
                : project.Plugins.Count > 0 ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Static arrangement clips / markers"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.TimelineItems is { Count: > 0 }
                    ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Audio media references"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.MediaReferences is { Count: > 0 }
                    ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Routing"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.Routing.Count > 0 ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Mixer volume"] = nativeCandidate
                ? HasCandidate(project, "Mixer volume") || project.NativeMixerObservations?.Any(item => item.VolumeDb.HasValue) == true
                    ? ProjectFieldState.Candidate : ProjectFieldState.Unknown
                : project.Tracks.Any(t => t.Volume.HasValue) ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Mixer pan"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.Tracks.Any(t => t.Pan.HasValue) ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Mute / solo"] = nativeCandidate
                ? HasCandidate(project, "Mute state", "Solo state") || project.NativeMixerObservations?.Any(item => item.Mute.HasValue || item.Solo.HasValue) == true
                    ? ProjectFieldState.Candidate : ProjectFieldState.Unknown
                : project.Tracks.Any(t => t.Mute.HasValue || t.Solo.HasValue)
                    ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Insert slot"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.Plugins.Any(p => p.Slot.HasValue) ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Plug-in state references"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.Plugins.Any(p => p.StateReference is not null) ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["MIDI note events"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.MidiNotes is { Count: > 0 } ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Channel EQ bands"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.EqBands is { Count: > 0 } ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Audio warp points"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.WarpPoints is { Count: > 0 } ? ProjectFieldState.Present : ProjectFieldState.NotExported,
            ["Song metadata"] = nativeCandidate ? ProjectFieldState.Unknown
                : project.Metadata is { } metadata && new[] { metadata.Title, metadata.Artist, metadata.Album,
                    metadata.OriginalArtist, metadata.Composer, metadata.Songwriter, metadata.Producer,
                    metadata.Arranger, metadata.Year, metadata.Genre, metadata.Copyright, metadata.Website,
                    metadata.Comment }.Any(value => !string.IsNullOrWhiteSpace(value))
                    ? ProjectFieldState.Present : ProjectFieldState.NotExported
        };
    }

    private static bool HasCandidate(ProjectSnapshot project, params string[] fields) =>
        project.NativeMetadataCandidates?.Any(candidate => fields.Contains(candidate.Field, StringComparer.Ordinal)) == true;
}
