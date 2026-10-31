using System.Globalization;

namespace CubaseInspector.Core;

/// <summary>A browsing group of observations, never a normalized or exportable MIDI track.</summary>
public sealed record NativeMidiNoteGroup(string Key, string ProjectName, string ProjectPath,
    string? CandidateTrackName, string? CandidatePartName, string SourceLayout,
    IReadOnlyList<IndexedMidiNoteCandidate> Notes)
{
    public string Name => !string.IsNullOrWhiteSpace(CandidatePartName) ? CandidatePartName
        : !string.IsNullOrWhiteSpace(CandidateTrackName) ? CandidateTrackName : ProjectName;
    public string Context => CandidateTrackName is { Length: > 0 } track
        ? "Possible track: " + track : "Track association unavailable";
    public int NoteCount => Notes.Count;
    public string DisplayName => Name == ProjectName ? $"{Name} · {NoteCount:N0} notes"
        : $"{Name} · {ProjectName} · {NoteCount:N0} notes";
    public string PitchRange => Notes.Count == 0 ? "—" : $"{Notes.Min(note => note.Pitch)}–{Notes.Max(note => note.Pitch)}";
    public string TimingRange => Notes.Count == 0 ? "—" :
        Format(Notes.Min(note => note.PositionTicks)) + "–" + Format(Notes.Max(note => note.PositionTicks + note.DurationTicks));
    public static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);
}

/// <summary>Possible track-level grouping based only on shared enclosing CPR offsets.</summary>
public sealed record NativeMidiTrackGroup(string Key, string ProjectName, string ProjectPath,
    string DisplayName, string Context, long? CandidateTrackOffset,
    IReadOnlyList<NativeMidiNoteGroup> Parts)
{
    public int NoteCount => Parts.Sum(part => part.NoteCount);
    public int PartCount => Parts.Count;
}

public static class NativeMidiBrowser
{
    public static IReadOnlyList<NativeMidiTrackGroup> GroupByPossibleTrack(IEnumerable<NativeMidiNoteGroup> parts) =>
        parts.GroupBy(part => (part.ProjectPath, CandidateTrackOffset: part.Notes.FirstOrDefault()?.CandidateTrackOffset))
            .Select(group =>
            {
                var first = group.First();
                var orderedParts = group.OrderBy(part => part.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                var hasTrackOffset = group.Key.CandidateTrackOffset is long;
                var name = hasTrackOffset
                    ? first.CandidateTrackName ?? $"Possible track at 0x{group.Key.CandidateTrackOffset:X}"
                    : "Track association unavailable";
                return new NativeMidiTrackGroup(
                    $"{first.ProjectPath}\0{group.Key.CandidateTrackOffset?.ToString() ?? "unassigned"}",
                    first.ProjectName, first.ProjectPath, name,
                    hasTrackOffset ? $"Shared enclosing track offset 0x{group.Key.CandidateTrackOffset:X} · structural association" : "No enclosing track offset was available",
                    group.Key.CandidateTrackOffset, Array.AsReadOnly(orderedParts));
            })
            .OrderBy(group => group.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<NativeMidiNoteGroup> Group(IEnumerable<IndexedMidiNoteCandidate> notes) =>
        notes.Where(note => note.Evidence == EvidenceLevel.Candidate)
            .GroupBy(note => (note.ProjectPath, note.CandidatePartOffset, note.CandidateTrackOffset,
                UnassignedLayout: note.CandidatePartOffset is null && note.CandidateTrackOffset is null ? note.SourceLayout : ""))
            .Select(group =>
            {
                var first = group.First();
                return new NativeMidiNoteGroup(
                    $"{first.ProjectPath}\0{group.Key.UnassignedLayout}\0{first.CandidatePartOffset}\0{first.CandidateTrackOffset}",
                    first.ProjectName, first.ProjectPath, first.CandidateTrackName, first.CandidatePartName,
                    string.Join(", ", group.Select(note => note.SourceLayout).Distinct()), Array.AsReadOnly(group.OrderBy(note => note.PositionTicks)
                        .ThenBy(note => note.FileOffset).ToArray()));
            }).OrderBy(group => group.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Plots raw numbers with an explicit unverified axis. Never converts to beats or places clips.</summary>
    public static MidiPianoRollPreview? Preview(NativeMidiNoteGroup group)
    {
        var notes = group.Notes.Where(note => double.IsFinite(note.PositionTicks) &&
            double.IsFinite(note.DurationTicks) && note.DurationTicks >= 0 && note.Pitch is >= 0 and <= 127).ToArray();
        if (notes.Length == 0) return null;
        var visible = notes.Take(2000).Select(note => new MidiPianoRollNote(note.PositionTicks,
            note.DurationTicks, note.Pitch, note.Velocity / 127d)).ToArray();
        var start = notes.Min(note => note.PositionTicks);
        var end = notes.Max(note => note.PositionTicks + note.DurationTicks);
        if (!double.IsFinite(end)) return null;
        return new MidiPianoRollPreview("Raw stored position · units unverified", start,
            Math.Max(end, start + 1), notes.Min(note => note.Pitch), notes.Max(note => note.Pitch),
            notes.Length, visible, visible.Length < notes.Length);
    }
}
