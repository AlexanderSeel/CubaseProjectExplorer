namespace CubaseInspector.Core;

public sealed record MidiPianoRollNote(double Start, double Duration, int Key, double? Velocity);

public sealed record MidiPianoRollPreview(string TimeUnit, double Start, double End,
    int LowestKey, int HighestKey, int TotalNoteCount, IReadOnlyList<MidiPianoRollNote> VisibleNotes,
    bool IsDecimated);

/// <summary>Projects flat exported MIDI notes for a read-only piano-roll display; never reconstructs clip placement.</summary>
public static class MidiPianoRollPreviewer
{
    public const int MaximumVisibleNotes = 5_000;

    public static Result<MidiPianoRollPreview> Build(ProjectSnapshot project, string trackId)
    {
        if (project.Evidence != EvidenceLevel.Confirmed)
            return Result<MidiPianoRollPreview>.Fail("MIDI_ROLL_UNVERIFIED", "A piano roll is available only for confirmed structured project exports.");
        var tracks = project.Tracks.Where(track => string.Equals(track.Id, trackId, StringComparison.Ordinal)).Take(2).ToArray();
        if (tracks.Length != 1)
            return Result<MidiPianoRollPreview>.Fail("MIDI_ROLL_TRACK_AMBIGUOUS", "The selected MIDI track ID is missing or duplicated.");
        var notes = (project.MidiNotes ?? []).Where(note => string.Equals(note.TrackId, trackId, StringComparison.Ordinal)).ToArray();
        if (notes.Length == 0)
            return Result<MidiPianoRollPreview>.Fail("MIDI_ROLL_EMPTY", "No MIDI note events were exported for this track.");
        if (notes.Any(note => note.IsNestedInClip))
            return Result<MidiPianoRollPreview>.Fail("MIDI_ROLL_CLIP_NESTED", "Notes are nested in clips/scenes; their arrangement placement is not flattened in this view.");
        var units = notes.Select(note => ProjectAssetIndexer.NormalizeUnit(note.TimeUnit)).Distinct(StringComparer.Ordinal).ToArray();
        if (units.Length != 1 || units[0] is not ("beats" or "seconds"))
            return Result<MidiPianoRollPreview>.Fail("MIDI_ROLL_TIMEBASE_UNKNOWN", "The note timebase is missing or mixed; the piano roll cannot align this track safely.");
        if (notes.Any(note => note.Key is < 0 or > 127 || !double.IsFinite(note.Time) ||
            !double.IsFinite(note.Duration) || note.Time < 0 || note.Duration <= 0 ||
            (note.Velocity is double velocity && !double.IsFinite(velocity))))
            return Result<MidiPianoRollPreview>.Fail("MIDI_ROLL_NOTE_INVALID", "One or more exported note events contain invalid pitch or timing values.");

        var visible = notes.Length <= MaximumVisibleNotes ? notes : Enumerable.Range(0, MaximumVisibleNotes)
            .Select(index => notes[(int)((long)index * notes.Length / MaximumVisibleNotes)]).ToArray();
        var end = notes.Max(note => note.Time + note.Duration);
        if (!double.IsFinite(end) || end <= 0)
            return Result<MidiPianoRollPreview>.Fail("MIDI_ROLL_NOTE_INVALID", "The exported notes have an invalid timeline range.");
        return Result<MidiPianoRollPreview>.Ok(new MidiPianoRollPreview(units[0]!,
            notes.Min(note => note.Time), end, notes.Min(note => note.Key), notes.Max(note => note.Key),
            notes.Length, visible.Select(note => new MidiPianoRollNote(note.Time, note.Duration,
                note.Key, note.Velocity)).ToArray(), notes.Length > visible.Length));
    }
}
