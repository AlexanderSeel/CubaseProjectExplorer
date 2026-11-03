namespace CubaseInspector.Core;

public sealed record IndexedProjectMediaUse(
    int ReferenceIndex, string DeclaredPath, string FileType, string? TrackId, string? TrackName,
    bool? External, int? Channels, int? SampleRate, double? Duration);

public sealed record IndexedMidiTrack(
    string TrackId, string Name, string Kind, int NoteCount, int? LowestKey, int? HighestKey,
    int ChannelCount, string? TimeUnit, bool CanExportStandardMidi, string ExportLimitation);

/// <summary>Native CPR note observations without an asserted track owner or normalized MIDI timebase.</summary>
public sealed record IndexedMidiNoteCandidate(
    string ProjectPath, string ProjectName, int Observation, long FileOffset,
    double PositionTicks, double DurationTicks, int Pitch, int Velocity,
    EvidenceLevel Evidence, string Warning,
    string SourceLayout = "RIF2 MMidiNote/112-byte",
    string? CandidateTrackName = null, string? CandidatePartName = null,
    long? CandidateTrackOffset = null, long? CandidatePartOffset = null, int? Channel = null,
    ushort? RawFlags = null);

/// <summary>Native CPR media path nested under an audio event, without a confirmed path or track link.</summary>
public sealed record IndexedMediaReferenceCandidate(
    string ProjectPath, string ProjectName, int Observation, string DeclaredPath,
    string? CandidateTrackName, long? CandidateTrackOffset, long FileOffset,
    EvidenceLevel Evidence, string EvidenceSource, string Warning);

public sealed record ProjectAssetIndex(
    string ProjectPath, string ProjectName, EvidenceLevel Evidence,
    IReadOnlyList<IndexedProjectMediaUse> MediaUses,
    IReadOnlyList<IndexedMidiTrack> MidiTracks,
    IReadOnlyList<IndexedMidiNoteCandidate>? NativeMidiNoteCandidates = null,
    IReadOnlyList<IndexedMediaReferenceCandidate>? NativeMediaReferenceCandidates = null);

/// <summary>Indexes confirmed export data separately from candidate-only native observations.</summary>
public static class ProjectAssetIndexer
{
    public static ProjectAssetIndex Build(ProjectSnapshot project)
    {
        var nativeCandidates = (project.NativeMidiNoteCandidates ?? []).Select((note, index) =>
            new IndexedMidiNoteCandidate(project.FilePath, project.Name, index + 1, note.FileOffset,
                note.PositionTicks, note.DurationTicks, note.MidiPitch, note.Velocity,
                EvidenceLevel.Candidate,
                note.Evidence + " Possible enclosing track/part labels are structural candidates; ownership, timebase and clip placement remain unverified.",
                note.SourceLayout, note.CandidateTrackName, note.CandidatePartName,
                note.CandidateTrackOffset, note.CandidatePartOffset, note.Channel, note.RawFlags))
            .ToArray();
        var nativeMediaCandidates = (project.NativeAudioMediaCandidates ?? []).Select((media, index) =>
            new IndexedMediaReferenceCandidate(project.FilePath, project.Name, index + 1,
                media.DeclaredPath, media.CandidateTrackName, media.CandidateTrackOffset,
                media.FileOffset, EvidenceLevel.Candidate, media.Evidence, media.Warning))
            .ToArray();
        if (project.Evidence != EvidenceLevel.Confirmed)
            return new(project.FilePath, project.Name, project.Evidence, [], [], nativeCandidates, nativeMediaCandidates);

        var trackById = project.Tracks.GroupBy(track => track.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var media = (project.MediaReferences ?? []).Select((reference, index) =>
        {
            trackById.TryGetValue(reference.TrackId ?? "", out var track);
            return new IndexedProjectMediaUse(index, reference.Path,
                Path.GetExtension(reference.Path).TrimStart('.').ToUpperInvariant(),
                reference.TrackId, track?.Name, reference.External, reference.Channels,
                reference.SampleRate, reference.Duration);
        }).ToArray();

        var noteTracks = (project.MidiNotes ?? []).GroupBy(note => note.TrackId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var uniqueTrackIds = project.Tracks.GroupBy(track => track.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var midiTracks = project.Tracks.Where(track => track.Kind.Contains("MIDI", StringComparison.OrdinalIgnoreCase) ||
                track.Kind.Contains("Instrument", StringComparison.OrdinalIgnoreCase))
            .Select(track => BuildMidiTrack(project, track, noteTracks.GetValueOrDefault(track.Id, []), uniqueTrackIds.Contains(track.Id)))
            .ToArray();
        return new(project.FilePath, project.Name, project.Evidence, media, midiTracks, nativeCandidates);
    }

    private static IndexedMidiTrack BuildMidiTrack(ProjectSnapshot project, TrackInfo track,
        IReadOnlyList<ProjectMidiNote> notes, bool hasUniqueTrackId)
    {
        var units = notes.Select(note => NormalizeUnit(note.TimeUnit)).Distinct(StringComparer.Ordinal).ToArray();
        string? timeUnit = units.Length == 1 ? units[0] : null;
        var reason = !hasUniqueTrackId ? "The project track ID is missing or duplicated; note ownership is ambiguous."
            : notes.Count == 0 ? "No MIDI note events were exported for this track."
            : notes.Any(note => note.IsNestedInClip)
                ? "Notes inside clips/scenes cannot yet be flattened without losing their declared clip placement."
            : units.Length != 1 || timeUnit is null
                ? "Note time units are missing or mixed; MIDI export is unavailable."
            : notes.Any(note => note.Channel is < 0 or > 15 || note.Key is < 0 or > 127 ||
                !double.IsFinite(note.Time) || !double.IsFinite(note.Duration) || note.Time < 0 || note.Duration <= 0 ||
                (note.Velocity is double velocity && (!double.IsFinite(velocity) || velocity is < 0 or > 1)) ||
                (note.ReleaseVelocity is double release && (!double.IsFinite(release) || release is < 0 or > 1)))
                ? "One or more notes contain values outside the supported MIDI ranges."
            : timeUnit == "beats" && project.HasTempoAutomation
                ? "The project declares tempo automation; a single-tempo MIDI export would change playback timing."
            : timeUnit == "beats" && project.Bpm is not ( > 0 and < 1000 )
                ? "A valid exported tempo is required to write a beat-timed MIDI file."
            : notes.Count > MidiFileExporter.MaximumNotes
                ? $"The note count exceeds the export limit of {MidiFileExporter.MaximumNotes:N0}."
                : "";
        return new IndexedMidiTrack(track.Id, track.Name, track.Kind, notes.Count,
            notes.Count == 0 ? null : notes.Min(note => note.Key),
            notes.Count == 0 ? null : notes.Max(note => note.Key),
            notes.Select(note => note.Channel).Distinct().Count(), timeUnit,
            notes.Count > 0 && reason.Length == 0, reason);
    }

    internal static string? NormalizeUnit(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "beats" => "beats",
        "seconds" => "seconds",
        _ => null
    };
}
