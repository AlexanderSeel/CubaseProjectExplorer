namespace CubaseInspector.Core;

/// <summary>Formats reader observations for inspection without promoting candidate data to verified I/O.</summary>
public static class CprIoObservationPresentation
{
    public static IReadOnlyList<string> Build(ProjectSnapshot project, int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        limit = Math.Min(limit, 1000);
        var extension = Path.GetExtension(project.FilePath);
        if (limit == 0 || project.Evidence != EvidenceLevel.Candidate ||
            !(extension.Equals(".cpr", StringComparison.OrdinalIgnoreCase) ||
              extension.Equals(".bak", StringComparison.OrdinalIgnoreCase))) return [];

        var audio = (project.NativeAudioMediaCandidates ?? []).Take(limit)
            .Select(media => $"Audio path candidate · {media.DeclaredPath} · containing track label: {media.CandidateTrackName ?? "unknown"} · {media.SourceRecord} · file offset 0x{media.FileOffset:X} · {media.Warning}").ToArray();
        var mixerRows = (project.NativeMixerObservations ?? []).Take(limit).Select(mixer =>
        {
            var values = new List<string>();
            if (mixer.VolumeDb is double volume) values.Add($"volume {volume:0.##} dB");
            if (mixer.Pan is double pan) values.Add($"pan {pan:0.###}");
            if (mixer.Mute is bool mute) values.Add($"mute {mute}");
            if (mixer.Solo is bool solo) values.Add($"solo {solo}");
            values.AddRange((mixer.CandidateFields ?? []).Take(16)
                .Select(field => $"{field.Path} [{field.Type}]={field.Value}"));
            return $"Named-record candidate · {mixer.Name} · {string.Join("; ", values.DefaultIfEmpty("no supported fields decoded"))} · {mixer.SourceStream} · file offset 0x{mixer.ArchiveOffset:X} · {mixer.Evidence}";
        }).ToArray();

        var fields = (project.NativeMetadataCandidates ?? []).Take(limit)
            .Select(field => $"Typed-field candidate · {field.Field}: {field.Value} · {field.SourcePattern} · file offset 0x{field.ArchiveOffset:X} · {field.Status}").ToArray();

        var categories = new[] { audio, mixerRows, fields };
        var observations = new List<string>(Math.Min(limit, audio.Length + mixerRows.Length + fields.Length));
        for (var index = 0; index < limit && observations.Count < limit; index++)
            foreach (var category in categories)
                if (index < category.Length && observations.Count < limit)
                    observations.Add(category[index]);
        return observations;
    }
}
