using System.Globalization;
using System.Text;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Conservative bounded evidence reader, NOT a Cubase .cpr decoder. It neither interprets
/// plugin state nor claims to extract a track hierarchy, tempo or correct routing.
/// </summary>
public sealed class CprEvidenceReader : IProjectReader
{
    private const int MaxPrefixBytes = 256 * 1024;
    private static readonly byte[] AppVersionToken = "PAppVersion\0"u8.ToArray();

    public bool CanRead(string path) => CprFileIdentity.IsCprOrValidBackup(path);

    public async Task<Result<ProjectSnapshot>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Path.GetExtension(path).Equals(".bak", StringComparison.OrdinalIgnoreCase) &&
            !CprFileIdentity.HasCprHeader(path))
            return Result<ProjectSnapshot>.Fail("NOT_CPR_BACKUP",
                "The .bak file does not contain a Cubase RIFF/NUND header.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = (int)Math.Min(MaxPrefixBytes, stream.Length);
        var bytes = new byte[length];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        // Try the version-width-aware structural reader first (RIFF/32-bit or RIF2/64-bit).
        // If it cannot decode a recognized envelope, keep bounded token matches as candidates.
        var structural = await new CprStructureInspector().InspectAsync(path, cancellationToken);
        // Structural validation is attempted independently. If the envelope is recognized
        // but a chunk is truncated, continue bounded token extraction and retain the failure
        // as a project warning so users can inspect whatever survived without implying recovery.
        // Finish all asynchronous reads before creating the short-lived Span,
        // which cannot survive across an await boundary.
        // A CPR may store plugin metadata far beyond its opening chunks.
        var scan = await CprPluginStreamScanner.ScanAsync(stream, cancellationToken);
        var plugins = CprPluginLocationCorrelator.AttachCandidateLocations(scan.Plugins,
            structural.IsSuccess ? structural.Value?.NamedRecordCandidates : null);
        var data = bytes.AsSpan(0, count);
        var isRiff = data.StartsWith("RIFF"u8);
        var versionOffset = data.IndexOf(AppVersionToken);
        // Reject unrelated renamed files; a RIFF header alone is not sufficient (WAV uses RIFF too).
        var hasCubaseHeader = CprFileIdentity.HasCprHeader(path);
        if (versionOffset < 0 && plugins.Count == 0 && !structural.IsSuccess && !hasCubaseHeader)
            return Result<ProjectSnapshot>.Fail("NOT_RECOGNIZED", "No known CPR evidence tokens found. The file may be unsupported, encrypted, or not a Cubase project.");

        var warnings = new List<string>
        {
            "CPR inspection retains supported track, MIDI, media-path, mixer, insert, EQ and routing observations for review. Native ownership, timing, parameter meaning, EQ placement, send tap order, plugin state and version-wide layouts are not confirmed; use source fields and offsets to judge each interpretation.",
            "Cubase 15.0.40's APluginSlot load path looks up Plugin/Plugin UID and Original Plugin Name properties. Extracted UID/name values are stored CPR data; VST2/VST3 format, installed identity, owner assignment, state and instance count still require separate evidence."
        };
        if (Path.GetExtension(path).Equals(".bak", StringComparison.OrdinalIgnoreCase))
            warnings.Add("CPR backup (.bak) opened read-only; it may be incomplete after a crash, and no recovery or repair is attempted.");
        if (!structural.IsSuccess)
            warnings.Add("CPR structural read error [" + (structural.Error?.Code ?? "UNKNOWN") + "]: " +
                (structural.Error?.Message ?? "Unknown container error") +
                ". The reader continued with bounded metadata scans; the results are partial and not a recovered project.");
        if (scan.Partial) warnings.Add(scan.TokenCapReached
            ? "CPR plugin property-token extraction stopped after 10,000 markers; plugin list incomplete."
            : $"CPR plugin property-token extraction stopped after {scan.BytesRead / (1024 * 1024)} MiB " +
              $"of the {stream.Length / (1024 * 1024)} MiB project; list incomplete.");
        if (stream.Length > MaxPrefixBytes)
            warnings.Add("Plugin UID/name tokens were scanned across the project file, not just the opening prefix. " +
                "Version fallback tokens are searched only in the first 256 KiB.");
        if (!isRiff && !data.StartsWith("RIF2"u8))
            warnings.Add("RIFF/RIF2 signature absent; file recognized only through internal candidate tokens.");
        if (structural.IsSuccess && structural.Value is not null)
        {
            warnings.Add((structural.Value.ContainerMagic ?? "RIFF/RIF2") + "/NUND chunk structure parsed (read-only), with " +
                structural.Value.Chunks.Count + " chunks.");
            if (structural.Value.Arrangement is not null)
                warnings.Add("PArrangement candidate class-record headers: " +
                    structural.Value.Arrangement.RecordsFound +
                    (structural.Value.Arrangement.Partial ? " (partial)." : "."));
            if (structural.Value.Arrangement is not null)
            warnings.Add("Cubase native track/lane class records found: " +
                    structural.Value.Arrangement.TrackHints.Count +
                    "; their exact class names are recognized from the Cubase 15.0.40 executable, while extracted names, visibility and parent hierarchy may be incomplete.");
            warnings.AddRange(structural.Value.Warnings);
            if (structural.Value.MetadataCandidates is { Count: > 0 } metadataCandidates)
            {
                foreach (var candidate in metadataCandidates.Take(8))
                    warnings.Add($"CPR {candidate.Field} candidate: {candidate.Value} at file offset {candidate.ArchiveOffset}; not independently verified.");
                if (metadataCandidates.Count > 8)
                    warnings.Add($"{metadataCandidates.Count - 8} additional typed metadata candidates are available in the CPR structure report.");
            }
        }
        else if (isRiff) warnings.Add("RIFF/NUND structural check unavailable: " + structural.Error?.Code);

        // Decode bounded UID/name property tokens. ASM confirms the native
        // property vocabulary, but this byte-window reader has not located a
        // complete APluginSlot body or proven its owner/state.
        var namedCount = plugins.Count(p => !p.Name.StartsWith("Name not decoded", StringComparison.Ordinal) &&
            !p.Id.StartsWith("offset-", StringComparison.Ordinal));
        var uidOnlyCount = plugins.Count(p => p.Name == "Name not decoded");
        if (plugins.Count > 0)
            warnings.Add($"CPR plugin UID metadata: {namedCount} named UID token(s), " +
                $"{uidOnlyCount} UID-only token(s), " +
                $"{plugins.Count - namedCount - uidOnlyCount} unresolved offset marker(s). " +
                "Cubase ASM confirms these stored property names; UID format, complete slot boundaries, track owner, state and insert-instance count are not decoded here.");
        var locatedCandidates = plugins.Count(plugin => !string.IsNullOrWhiteSpace(plugin.CandidateOwnerName));
        if (locatedCandidates > 0)
            warnings.Add($"{locatedCandidates} plug-in name marker(s) overlap CPR named track records. The nearby record label is shown; Cubase plug-in ownership is unverified.");
        // The streaming scanner enforces a single 10,000-marker limit across all file windows.
        string? version = structural.IsSuccess ? structural.Value?.VersionTextCandidate : null;
        if (version is null && versionOffset >= 0)
        {
            var start = versionOffset + AppVersionToken.Length;
            // Version token is known, but surrounding binary structure is not; don't invent a version.
            var printables = ExtractPrintableStrings(data[start..Math.Min(data.Length, start + 256)]);
            version = printables.FirstOrDefault(s => s.Contains("Cubase", StringComparison.OrdinalIgnoreCase));
        }
        var generation = CubaseGenerationClassifier.Classify(version);
        if (generation.SavedByMajorVersion.HasValue)
            warnings.Add("Application generation: " + generation.Display +
                ". The exact original creation release, historical compatibility and native inserts are NOT verified.");
        else
            warnings.Add("Saved-by version unavailable. This can occur with early SX/SL and unknown CPR archives; " +
                "do not assume the file's age or infer SX from a small version number.");
        // The ASM-backed class map identifies the stored class as a Cubase lane/track
        // record. Name extraction, visible-track status, parentage and mixer-channel
        // ownership remain provisional, so the normalized project stays candidate-level.
        var trackHints = structural.IsSuccess ? structural.Value?.Arrangement?.TrackHints : null;
        var tracks = trackHints is null ? new List<TrackInfo>() : trackHints.Select(h => new TrackInfo(
            "cpr-record-" + h.ArchiveOffset.ToString("X"),
            h.Name ?? "(unnamed CPR record)",
            CprTrackRecordKinds.KindFor(h.RecordClass) ?? "Unknown",
            null, null, null, null, null, EvidenceLevel.Candidate,
            h.RecordClass, h.ArchiveOffset, SourceEndOffset: h.ArchiveEndOffset)).ToList();
        if (tracks.Count > 0)
            warnings.Add("Candidate CPR track count includes hidden/device carrier records; do not compare it directly with Cubase visible track count.");
        var fieldCandidates = structural.IsSuccess
            ? (structural.Value?.MetadataCandidates ?? []).Concat(
                ReadNamedRecordMixerObservations(structural.Value?.NamedRecordCandidates)).ToArray()
            : [];
        var mixerObservations = structural.IsSuccess
            ? ReadNamedRecordMixerStrips(structural.Value?.NamedRecordCandidates)
            : [];
        var nativeMidiNoteCandidates = structural.IsSuccess
            ? structural.Value?.Arrangement?.MidiNoteCandidates
            : null;
        var nativeAudioMediaCandidates = structural.IsSuccess
            ? structural.Value?.Arrangement?.AudioMediaCandidates
            : null;
        if (nativeMidiNoteCandidates is { Count: > 0 })
            warnings.Add($"{(nativeMidiNoteCandidates.All(note => note.SourceLayout.StartsWith("RIF2", StringComparison.Ordinal)) ? "RIF2 MIDI note observations" : "Native CPR MIDI note observations")}: {nativeMidiNoteCandidates.Count:N0} candidates from {string.Join(", ", nativeMidiNoteCandidates.Select(note => note.SourceLayout).Distinct())}. Timing units, track/clip ownership and arrangement semantics remain unverified; these are not normalized MIDI notes.");
        if (nativeAudioMediaCandidates is { Count: > 0 })
            warnings.Add($"CPR audio media: {nativeAudioMediaCandidates.Count:N0} file-path token candidate(s) found inside MAudioEvent records nested under audio-track records. Path existence and track/media ownership are unverified.");
        return Result<ProjectSnapshot>.Ok(new ProjectSnapshot(path, Path.GetFileNameWithoutExtension(path),
            "CPR structural evidence (read-only)", version, null, EvidenceLevel.Candidate,
            tracks, plugins, [], warnings,
            NativeMetadataCandidates: fieldCandidates,
            NativeMixerObservations: mixerObservations,
            NativeMidiNoteCandidates: nativeMidiNoteCandidates,
            NativeAudioMediaCandidates: nativeAudioMediaCandidates));
    }

    private static IReadOnlyList<CprMixerObservation> ReadNamedRecordMixerStrips(
        IReadOnlyList<CprNamedRecordCandidate>? records)
    {
        if (records is null || records.Count == 0) return [];
        var result = new List<CprMixerObservation>();
        foreach (var record in records)
        {
            var names = record.Values.Where(value => value.Path.Equals("Name/String", StringComparison.OrdinalIgnoreCase))
                .Select(value => value.Value).Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length != 1) continue;
            var volume = ReadDouble("Volume/AnchorValue", -150, 20);
            var mute = ReadFlag("Mute");
            var solo = ReadFlag("Solo");
            var candidateFields = record.Values.Where(value => IsMixerCandidatePath(value.Path))
                .OrderBy(value => value.Path.StartsWith("OwnInputBus/", StringComparison.OrdinalIgnoreCase) ||
                    value.Path.StartsWith("OutputBus/", StringComparison.OrdinalIgnoreCase) ||
                    value.Path.StartsWith("SendFolder/", StringComparison.OrdinalIgnoreCase) ||
                    value.Path.Equals("InputBusValue", StringComparison.OrdinalIgnoreCase) ||
                    value.Path.Equals("OutputBusValue", StringComparison.OrdinalIgnoreCase) ? 0 :
                    value.Path.Equals("postInsertsPreFader", StringComparison.OrdinalIgnoreCase) ||
                    value.Path.StartsWith("InsertFolder/", StringComparison.OrdinalIgnoreCase) ||
                    value.Path.StartsWith("EQ/", StringComparison.OrdinalIgnoreCase) ||
                    value.Path.StartsWith("ChannelEQ/", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .Take(512).ToArray();
            // Include named records even when mixer fields are absent so the view can
            // show what the reader found without implying that a channel was omitted.
            result.Add(new CprMixerObservation(names[0], volume, null, mute, solo,
                record.ArchiveOffset, record.StreamName, CandidateFields: candidateFields));

            double? ReadDouble(string path, double min, double max)
            {
                var values = record.Values.Where(value => value.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && value.Type == "f64")
                    .Select(value => double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                        double.IsFinite(parsed) && parsed >= min && parsed <= max ? (double?)parsed : null)
                    .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
                return values.Length == 1 ? values[0] : null;
            }

            bool? ReadFlag(string path)
            {
                var values = record.Values.Where(value => value.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && value.Type == "i64")
                    .Select(value => long.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                        parsed is 0 or 1 ? (bool?)(parsed == 1) : null)
                    .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
                return values.Length == 1 ? values[0] : null;
            }
        }
        return result;

        static bool IsMixerCandidatePath(string path) =>
            path.StartsWith("Volume/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("InputGain/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("InputPhase", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Pan/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Panner/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("EQ/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("ChannelEQ/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("InsertFolder/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("OwnInputBus/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("OutputBus/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("SendFolder/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("InputBusValue", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("OutputBusValue", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("postInsertsPreFader", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("directRoutingFolder/", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CprMetadataCandidate> ReadNamedRecordMixerObservations(
        IReadOnlyList<CprNamedRecordCandidate>? records)
    {
        if (records is null || records.Count == 0) return [];
        var observations = new List<CprMetadataCandidate>();
        foreach (var record in records)
        {
            var names = record.Values.Where(value => value.Path.Equals("Name/String", StringComparison.OrdinalIgnoreCase))
                .Select(value => value.Value).Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length != 1 || string.IsNullOrWhiteSpace(names[0])) continue;
            AddNumber("Volume/AnchorValue", "Mixer volume", value => value is >= -150 and <= 20,
                value => value + " dB");
            AddFlag("Mute", "Mute state");
            AddFlag("Solo", "Solo state");

            void AddNumber(string path, string field, Func<double, bool> valid, Func<string, string> format)
            {
                foreach (var item in record.Values.Where(value => value.Path.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                    value.Type == "f64"))
                    if (double.TryParse(item.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                        double.IsFinite(value) && valid(value))
                        observations.Add(new CprMetadataCandidate(field, names[0] + ": " + format(value.ToString("0.##", CultureInfo.InvariantCulture)),
                            item.ArchiveOffset, "Cubase 12 named record " + path,
                            "Unverified named-record field; mixer ownership and semantics are not independently validated"));
            }

            void AddFlag(string path, string field)
            {
                foreach (var item in record.Values.Where(value => value.Path.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                    value.Type == "i64"))
                    if (long.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value is 0 or 1)
                        observations.Add(new CprMetadataCandidate(field, names[0] + ": " + (value == 1 ? "On" : "Off"),
                            item.ArchiveOffset, "Cubase 12 named record " + path,
                            "Unverified named-record field; mixer ownership and semantics are not independently validated"));
            }
        }
        return observations;
    }

    private static IReadOnlyList<string> ExtractPrintableStrings(ReadOnlySpan<byte> input)
    {
        var result = new List<string>();
        var start = -1;
        for (var i = 0; i <= input.Length; i++)
        {
            var printable = i < input.Length && input[i] >= 32 && input[i] <= 126;
            if (printable && start < 0) start = i;
            if (!printable && start >= 0)
            {
                if (i - start is >= 4 and <= 100) result.Add(Encoding.ASCII.GetString(input[start..i]));
                start = -1;
            }
        }
        return result;
    }
}
