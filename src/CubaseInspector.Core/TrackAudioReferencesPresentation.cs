namespace CubaseInspector.Core;

public sealed record TrackAudioReference(string Name, string Path, string Kind,
    string Association, EvidenceLevel Evidence, string Source, long? SourceOffset, string Details);

public sealed record TrackAudioReferences(IReadOnlyList<TrackAudioReference> Associated,
    IReadOnlyList<TrackAudioReference> Unassigned);

/// <summary>Joins saved media references only by declared IDs or exact source offsets; names are a fallback only when no offset exists.</summary>
public static class TrackAudioReferencesPresentation
{
    public static TrackAudioReferences Build(ProjectSnapshot project, TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(track);
        var associated = new List<TrackAudioReference>();
        var unassigned = new List<TrackAudioReference>();
        var tracksById = project.Tracks.GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var tracksByName = project.Tracks.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

        foreach (var (reference, index) in (project.MediaReferences ?? []).Select((item, index) => (item, index)))
        {
            var row = new TrackAudioReference(Path.GetFileName(reference.Path), reference.Path, "Declared media reference",
                reference.TrackId == track.Id ? "Declared TrackId match" : "No exact track ID",
                EvidenceLevel.Confirmed, "Project media reference", null,
                $"{reference.External switch { true => "External", false => "Embedded", _ => "Location unspecified" }} · {reference.Channels?.ToString() ?? "?"} channels · {reference.SampleRate?.ToString("N0") ?? "?"} Hz · reference {index + 1}");
            if (reference.TrackId == track.Id) associated.Add(row);
            else if (reference.TrackId is null || !tracksById.ContainsKey(reference.TrackId)) unassigned.Add(row);
        }

        foreach (var (reference, index) in (project.NativeAudioMediaCandidates ?? []).Select((item, index) => (item, index)))
        {
            var exactOffset = track.SourceOffset is long trackOffset && reference.CandidateTrackOffset == trackOffset;
            var nameOnly = reference.CandidateTrackOffset is null && reference.CandidateTrackName is { } hintName &&
                tracksByName.TryGetValue(hintName, out var namedTrack) && namedTrack.Id == track.Id;
            var row = new TrackAudioReference(Path.GetFileName(reference.DeclaredPath), reference.DeclaredPath,
                "CPR audio path observation", exactOffset ? "Exact enclosing track byte offset" :
                    nameOnly ? "Track-name hint only · ownership unverified" : "Track association unavailable",
                EvidenceLevel.Candidate, reference.SourceRecord, reference.FileOffset,
                $"Candidate {index + 1} · track hint: {reference.CandidateTrackName ?? "not decoded"} · track offset: " +
                (reference.CandidateTrackOffset is long offset ? $"0x{offset:X}" : "not decoded") + $"\n{reference.Warning}");
            if (exactOffset || nameOnly) associated.Add(row);
            else if (reference.CandidateTrackOffset is null && (string.IsNullOrWhiteSpace(reference.CandidateTrackName) ||
                     !tracksByName.ContainsKey(reference.CandidateTrackName)) ||
                     reference.CandidateTrackOffset is not null && !project.Tracks.Any(item => item.SourceOffset == reference.CandidateTrackOffset))
                unassigned.Add(row);
        }

        return new(associated, unassigned);
    }
}
