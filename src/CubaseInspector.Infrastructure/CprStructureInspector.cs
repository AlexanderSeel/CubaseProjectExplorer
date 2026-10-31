using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Read-only candidate parser for a recognized RIFF/NUND envelope and
/// ROOT/ARCH class-header grammar. No source-project mutations or plugin execution.
/// Structural record counts are NOT validated tracks, plugins or MIDI events.
/// </summary>
public sealed class CprStructureInspector : ICprStructureInspector
{
    private const int MaxChunks = 4096;
    private const int MaxRootBytes = 4096;
    private const int MaxArrangementBytes = 128 * 1024 * 1024;
    private const int MaxVersionBytes = 64 * 1024;
    private const int MaxRecords = 250_000;
    private const int MaxDepth = 48;
    private const int MaxNamedArchiveBytes = 32 * 1024 * 1024;
    private const int MaxNamedScanBytes = 128 * 1024 * 1024;
    private const int MaxMetadataScanBytes = 128 * 1024 * 1024;
    private const int MaxInventoryArchives = 128;
    private const int MaxInventoryBytes = 128 * 1024 * 1024;
    private const int MaxInventoryRecords = 10_000;
    private const int MaxInventoryRanges = 4096;

    public async Task<Result<CprStructureReport>> InspectAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Result<CprStructureReport>.Fail("FILE_NOT_FOUND", "CPR project was not found.");
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length < 12)
                return Result<CprStructureReport>.Fail("TOO_SHORT", "CPR container header is incomplete.");
            var prefix = new byte[4];
            await file.ReadExactlyAsync(prefix, ct);
            var isRif2 = prefix.AsSpan().SequenceEqual("RIF2"u8);
            if (!isRif2 && !prefix.AsSpan().SequenceEqual("RIFF"u8))
                return Result<CprStructureReport>.Fail("NOT_CPR_CONTAINER", "Expected RIFF or RIF2 CPR header.");
            var headerLength = isRif2 ? 16 : 12;
            if (file.Length < headerLength)
                return Result<CprStructureReport>.Fail("TOO_SHORT", "CPR container header is incomplete.");
            file.Position = 0;
            var header = new byte[headerLength];
            await file.ReadExactlyAsync(header, ct);
            if (!header.AsSpan(headerLength - 4, 4).SequenceEqual("NUND"u8))
                return Result<CprStructureReport>.Fail("NOT_CPR_CONTAINER", "Expected NUND Cubase project form.");
            CprEnvelopeSizeDiagnostics? sizeDiagnostics = isRif2 ? null : CprEnvelopeProbe.Inspect(file);
            var rawSize = isRif2
                ? BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4, 8))
                : BigEndian32(header, 4);
            if (rawSize > (ulong)(file.Length - headerLength))
                return Result<CprStructureReport>.Fail("TRUNCATED_CPR", "Declared CPR container exceeds file length.");
            var declared = (long)rawSize;
            var end = headerLength + declared;
            var chunks = new List<CprChunkSummary>();
            var warnings = new List<string>();
            if (isRif2)
                warnings.Add("RIF2/NUND 64-bit chunk framing verified against two Cubase 15.0.40 projects; nested values remain candidate-only.");
            else
            {
                warnings.Add("RIFF/NUND identifies a Cubase-family candidate; the 32-bit size field and chunk grammar are not verified as universal across Cubase versions.");
                warnings.Add("This structural inventory applies a big-endian size interpretation as a candidate parsing strategy; it is not a validated format specification.");
            }
            if (end < file.Length)
                warnings.Add("File includes trailing bytes not covered by the container size.");
            string? pendingStream = null, pendingClass = null;
            CprArchiveSummary? arrangement = null;
            string? versionCandidate = null;
            var namedRecordCandidates = new List<CprNamedRecordCandidate>();
            long namedScanBytes = 0;
            var metadataCandidates = new List<CprMetadataCandidate>();
            long metadataScanBytes = 0;
            var inventories = new List<CprArchiveRecordInventory>();
            var inventoryBytes = 0;
            var inventoryRecords = 0;
            var inventoryDetails = 0;
            var inventoryRanges = 0;
            var inventorySymbols = 0;
            var omittedInventories = 0;
            long cursor = headerLength;
            var chunkHeaderLength = isRif2 ? 12 : 8;
            while (cursor < end)
            {
                ct.ThrowIfCancellationRequested();
                if (chunks.Count >= MaxChunks)
                    return Result<CprStructureReport>.Fail("CHUNK_LIMIT", "CPR chunk count exceeds safety limit.");
                if (end - cursor < chunkHeaderLength)
                    return Result<CprStructureReport>.Fail("TRUNCATED_CHUNK", "Incomplete CPR chunk header.");
                file.Position = cursor;
                var chunkHeader = new byte[chunkHeaderLength];
                await file.ReadExactlyAsync(chunkHeader, ct);
                var id = Encoding.ASCII.GetString(chunkHeader, 0, 4);
                var rawChunkSize = isRif2
                    ? BinaryPrimitives.ReadUInt64BigEndian(chunkHeader.AsSpan(4, 8))
                    : BigEndian32(chunkHeader, 4);
                var payloadOffset = cursor + chunkHeaderLength;
                if (rawChunkSize > (ulong)(end - payloadOffset))
                    return Result<CprStructureReport>.Fail("TRUNCATED_CHUNK", "CPR chunk extends beyond declared container.");
                var size = (long)rawChunkSize;
                string? stream = null, cls = null;
                if (id == "ROOT")
                {
                    pendingStream = pendingClass = null;
                    if (size is > 0 and <= MaxRootBytes)
                    {
                        var data = new byte[(int)size];
                        await file.ReadExactlyAsync(data, ct);
                        if (TryRoot(data, out var s, out var c)) { pendingStream = s; pendingClass = c; }
                        else warnings.Add("Could not interpret a ROOT chunk at offset " + cursor + ".");
                    }
                    else warnings.Add("ROOT chunk length outside safe parsing range at " + cursor + ".");
                }
                else if (id == "ARCH")
                {
                    stream = pendingStream;
                    cls = pendingClass;
                    pendingStream = pendingClass = null;
                    if (arrangement is null && cls == "PArrangement")
                    {
                        var count = (int)Math.Min(size, MaxArrangementBytes);
                        var data = new byte[count];
                        await file.ReadExactlyAsync(data, ct);
                        arrangement = ScanArrangement(stream ?? "Unnamed", size, data, isRif2, payloadOffset, ct);
                        if (arrangement.Partial)
                            warnings.Add("Arrangement evidence is partial: " + string.Join("; ", arrangement.LimitReasons ?? []));
                    }
                    else if (versionCandidate is null && cls == "PAppVersion")
                    {
                        var count = (int)Math.Min(size, MaxVersionBytes);
                        var data = new byte[count];
                        await file.ReadExactlyAsync(data, ct);
                        versionCandidate = FindVersionText(data);
                        if (size > MaxVersionBytes)
                            warnings.Add("Version ARCH exceeds scan budget; version is incomplete.");
                    }
                    if (!isRif2 && size <= MaxNamedArchiveBytes && namedScanBytes + size <= MaxNamedScanBytes &&
                        namedRecordCandidates.Count < 128)
                    {
                        file.Position = payloadOffset;
                        var namedData = new byte[(int)size];
                        await file.ReadExactlyAsync(namedData, ct);
                        namedScanBytes += size;
                        namedRecordCandidates.AddRange(CprNamedRecordScanner.Scan(stream ?? "Unnamed", namedData,
                            payloadOffset, 128 - namedRecordCandidates.Count));
                    }
                    if (!isRif2 && size <= MaxNamedArchiveBytes && metadataScanBytes + size <= MaxMetadataScanBytes &&
                        metadataCandidates.Count < 128)
                    {
                        file.Position = payloadOffset;
                        var metadataData = new byte[(int)size];
                        await file.ReadExactlyAsync(metadataData, ct);
                        metadataScanBytes += size;
                        var metadataReadErrors = new List<string>();
                        metadataCandidates.AddRange(CprMetadataCandidateScanner.Scan(metadataData, payloadOffset, metadataReadErrors)
                            .Take(128 - metadataCandidates.Count));
                        foreach (var readError in metadataReadErrors)
                            if (warnings.Count < 512) warnings.Add(readError);
                    }
                    if (inventories.Count < MaxInventoryArchives)
                    {
                        var count = inventoryRecords >= MaxRecords ? 0 :
                            (int)Math.Min(size, MaxInventoryBytes - inventoryBytes);
                        file.Position = payloadOffset;
                        var archiveData = new byte[count];
                        await file.ReadExactlyAsync(archiveData, ct);
                        var inventory = CprArchiveRecordScanner.Scan(stream ?? "Unnamed", cls,
                            archiveData, payloadOffset, size, isRif2, ct,
                            MaxInventoryRecords - inventoryDetails, MaxRecords - inventoryRecords,
                            MaxInventoryRanges - inventoryRanges, MaxRecords - inventorySymbols);
                        inventory = inventory with
                        {
                            Records = inventory.Records.Select(record => record with
                            {
                                RawFields = CprBinaryFieldCandidateDecoder.Read(record, archiveData, payloadOffset)
                            }).ToArray()
                        };
                        inventories.Add(inventory);
                        inventoryBytes += count;
                        inventoryRecords += inventory.RecordsFound;
                        inventoryDetails += inventory.Records.Count;
                        inventoryRanges += inventory.UninterpretedRanges.Count;
                        inventorySymbols += inventory.SymbolsFound;
                    }
                    else omittedInventories++;
                }
                else pendingStream = pendingClass = null;
                chunks.Add(new CprChunkSummary(id, cursor, size, stream, cls));
                cursor = payloadOffset + size; // No ordinary RIFF even-byte padding.
            }
            if (arrangement is null)
                warnings.Add("PArrangement ARCH not located; no arrangement-specific track/MIDI evidence available.");
            warnings.Add("Serialized class names/counts are direct structural observations. Only names in the Cubase serializer class map receive a track/lane kind; this does not establish visible-track status, ownership, hierarchy, or semantic completeness.");
            if (namedRecordCandidates.Count > 0)
                warnings.Add($"Cubase 12 named-record grammar: {namedRecordCandidates.Count} channel-shaped candidate record sequence(s); values are source-level observations, not verified channel semantics.");
            if (metadataCandidates.Count > 0)
                warnings.Add($"Public CubaseTools metadata patterns produced {metadataCandidates.Count} typed candidate value(s); values retain source offsets and have not been promoted to project metadata.");
            if (namedScanBytes >= MaxNamedScanBytes)
                warnings.Add("Named-record scan reached its 128 MiB aggregate budget; later ARCH payloads were not scanned.");
            if (metadataScanBytes >= MaxMetadataScanBytes)
                warnings.Add("Metadata candidate scan reached its 128 MiB aggregate budget; later ARCH payloads were not scanned.");
            if (inventories.Any(inventory => inventory.Partial))
                warnings.Add("Detailed ARCH evidence is partial. Per-archive LimitReasons disclose byte, record, depth and retained-detail limits; uninterpreted bytes are not necessarily corrupt.");
            if (omittedInventories > 0)
                warnings.Add($"Detailed ARCH evidence omitted {omittedInventories} archive(s) after the {MaxInventoryArchives}-archive limit; their container chunk entries remain available.");
            warnings.Add("Detailed class-header matches and byte containment are candidate evidence, not a verified Cubase object graph or musical field coverage. Unknown class names are retained without assigning semantics.");
            return Result<CprStructureReport>.Ok(new CprStructureReport(path, declared, chunks,
                arrangement, versionCandidate, warnings, namedRecordCandidates, sizeDiagnostics, metadataCandidates,
                isRif2 ? "RIF2" : "RIFF", inventories));
        }
        catch (OperationCanceledException) { return Result<CprStructureReport>.Fail("CANCELLED", "CPR inspection cancelled."); }
        catch (IOException e) { return Result<CprStructureReport>.Fail("IO_ERROR", e.Message); }
        catch (UnauthorizedAccessException e) { return Result<CprStructureReport>.Fail("ACCESS_DENIED", e.Message); }
    }

    private static uint BigEndian32(ReadOnlySpan<byte> b, int off) =>
        BinaryPrimitives.ReadUInt32BigEndian(b.Slice(off, 4));

    private static bool TryRoot(ReadOnlySpan<byte> bytes, out string stream, out string cls)
    {
        stream = cls = "";
        var p = 0;
        var fields = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            if (p + 4 > bytes.Length) return false;
            var n = BigEndian32(bytes, p);
            p += 4;
            if (n is < 1 or > 256 || n > bytes.Length - p) return false;
            var field = bytes.Slice(p, (int)n);
            if (field.IndexOf((byte)0) >= 0 || field.AnyNonAsciiControl()) return false;
            fields.Add(Encoding.ASCII.GetString(field));
            p += (int)n;
        }
        if (p != bytes.Length) return false;
        stream = fields[0]; cls = fields[1];
        return true;
    }

    private static string? FindVersionText(ReadOnlySpan<byte> bytes)
    {
        // The dedicated Version ARCH often stores separate length-prefixed
        // strings ("Cubase" then "Version 5.1.1"), not a single text field.
        // Treat the result as descriptive evidence, not a format-version proof.
        for (var p = 0; p + 6 <= bytes.Length; p++)
        {
            if (!bytes[p..].StartsWith("Cubase"u8) && !bytes[p..].StartsWith("Nuendo"u8))
                continue;
            var tail = p;
            while (tail < bytes.Length && tail - p < 96 && bytes[tail] is >= 32 and <= 126) tail++;
            var application = Encoding.ASCII.GetString(bytes[p..tail]);
            if (application.Length < 6) continue;

            // Prefer a separate version string close to the app-name in the
            // same version archive, bounded to 128 bytes and never crossing the chunk.
            var searchEnd = Math.Min(bytes.Length, tail + 128);
            for (var q = tail; q + 8 <= searchEnd; q++)
            {
                if (!bytes[q..].StartsWith("Version "u8)) continue;
                var vEnd = q;
                while (vEnd < searchEnd && vEnd - q < 64 &&
                       bytes[vEnd] is >= 32 and <= 126) vEnd++;
                if (vEnd > q + 8)
                    return application + " " + Encoding.ASCII.GetString(bytes[q..vEnd]);
            }
            if (application.Length >= 8) return application;
        }
        return null;
    }

    private static CprArchiveSummary ScanArrangement(string stream, long size, byte[] bytes, bool isRif2,
        long archiveFileOffset, CancellationToken ct)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var interned = new Dictionary<uint, string>();
        var frames = new Stack<(int End, int Resume, int Depth, int? PreviousTrack,
            (string? Name, long Offset)? PreviousPart)>();
        var hints = new List<CprTrackHint>();
        var midiNotes = new List<CprRif2MidiNoteCandidate>();
        var audioMediaCandidates = new List<CprAudioMediaReferenceCandidate>();
        var activeTrack = (int?)null;
        (string? Name, long Offset)? activePart = null;
        var packedMidiScanBytes = 0;
        var packedParts = new List<(int Start, int End, int Header, int? Track)>();
        var midiRecordRanges = new List<CprUninterpretedRange>();
        const int maxPackedMidiBytes = 32 * 1024 * 1024;
        var position = 0;
        var upper = bytes.Length;
        var depth = 0;
        var total = 0;
        var iterations = 0;
        var limits = new HashSet<string>(StringComparer.Ordinal);
        if (bytes.Length < size) limits.Add("128 MiB arrangement byte limit");
        while (true)
        {
            if ((iterations++ & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (position >= upper)
            {
                if (frames.Count == 0) break;
                var previous = frames.Pop();
                upper = previous.End;
                position = previous.Resume;
                depth = previous.Depth;
                activeTrack = previous.PreviousTrack;
                activePart = previous.PreviousPart;
                continue;
            }
            if (total >= MaxRecords)
            {
                limits.Add("250,000 arrangement record limit");
                break;
            }
            if (interned.Count >= MaxRecords)
            {
                limits.Add("250,000 arrangement class-symbol limit");
                break;
            }
            string name64;
            int bodyStart64, bodyEnd64;
            if (total < MaxRecords && (isRif2
                    ? TryRecord64(bytes, position, upper, interned, out name64, out bodyStart64, out bodyEnd64)
                    : TryRecord(bytes, position, upper, interned, out name64, out bodyStart64, out bodyEnd64)))
            {
                var name = name64;
                var bodyStart = bodyStart64;
                var bodyEnd = bodyEnd64;
                counts[name] = counts.GetValueOrDefault(name) + 1;
                total++;
                if (name == "MMidiNote")
                    midiRecordRanges.Add(new CprUninterpretedRange(archiveFileOffset + position, bodyEnd - position));
                if (name == "MMidiNote" &&
                    CprMidiPartNoteScanner.TryReadRecord(bytes.AsSpan(bodyStart, bodyEnd - bodyStart), archiveFileOffset + bodyStart, isRif2,
                        out var noteCandidate))
                {
                    if (midiNotes.Count < 10000) midiNotes.Add(WithContext(noteCandidate));
                    else limits.Add("10,000 MIDI note candidate limit");
                }
                if (name == "MMidiPart")
                {
                    if (packedParts.Count < 4096) packedParts.Add((bodyStart, bodyEnd, position, activeTrack));
                    else limits.Add("4,096 packed MIDI part scan limit");
                }

                // Audio path strings observed in the corpus are contained by
                // MAudioEvent records, which in turn can be nested under an
                // MAudioTrackEvent. Keep the association candidate-only.
                if (name == "MAudioEvent" && activeTrack is int audioOwner &&
                    audioOwner >= 0 && audioOwner < hints.Count &&
                    hints[audioOwner].RecordClass == "MAudioTrackEvent")
                {
                    foreach (var (declaredPath, pathOffset) in ReadAudioPathCandidates(bytes, bodyStart, bodyEnd,
                                 ct, Math.Max(1, 10001 - audioMediaCandidates.Count)))
                    {
                        if (audioMediaCandidates.Count >= 10000)
                        {
                            limits.Add("10,000 audio-path candidate limit");
                            break;
                        }
                        audioMediaCandidates.Add(new CprAudioMediaReferenceCandidate(
                            declaredPath, archiveFileOffset + pathOffset, hints[audioOwner].Name,
                            hints[audioOwner].ArchiveOffset, name));
                    }
                }

                if (activeTrack is int owner && name is "MListNode" or "MTrackList" &&
                    hints[owner].Name is null)
                {
                    var candidate = ReadBoundedName(bytes, bodyStart, bodyEnd);
                    if (candidate is not null)
                        hints[owner] = hints[owner] with { Name = candidate,
                            NameSource = "Nested length-prefixed string (candidate)" };
                    else if (name == "MListNode" && TryLegacyListNodeName(bytes, bodyStart, bodyEnd,
                                 hints[owner].RecordClass, out candidate))
                        hints[owner] = hints[owner] with { Name = candidate,
                            NameSource = "SX2/SX3 MListNode marker and length-prefixed string (candidate)" };
                }

                var nextTrack = activeTrack;
                var nextPart = name == "MMidiPart"
                    ? (ReadBoundedName(bytes, bodyStart, bodyEnd), archiveFileOffset + position)
                    : activePart;
                // These serialized lane/event class names occur in Cubase 15
                // serializer/load paths (see the checked-in
                // IDA export and CPR format research). Keep the exact class
                // identity; do not merge them by name or nearby offset.
                if (CprTrackRecordKinds.IsTrackRecord(name))
                {
                    if (hints.Count < 4096)
                    {
                        nextTrack = hints.Count;
                        hints.Add(new CprTrackHint(name, null, archiveFileOffset + position,
                            ArchiveEndOffset: archiveFileOffset + bodyEnd));
                    }
                    else
                    {
                        nextTrack = null;
                        limits.Add("4,096 track candidate limit");
                    }
                }
                if (depth < MaxDepth && bodyEnd > bodyStart)
                {
                    frames.Push((upper, bodyEnd, depth, activeTrack, activePart));
                    upper = bodyEnd;
                    position = bodyStart;
                    activeTrack = nextTrack;
                    activePart = nextPart;
                    depth++;
                }
                else
                {
                    if (bodyEnd > bodyStart) limits.Add("48-level arrangement nesting limit");
                    position = bodyEnd;
                }
            }
            else position++;
        }
        // A size byte (e.g. a 154-byte body ends in 0x9A) can resemble a compact
        // MIDI status. Exclude recognized note record ranges before scanning
        // packed streams so the same note is never counted through both paths.
        var excluded = new List<CprUninterpretedRange>();
        foreach (var range in midiRecordRanges.OrderBy(range => range.FileOffset))
        {
            if (excluded.Count > 0 && excluded[^1].FileOffset + excluded[^1].ByteCount >= range.FileOffset)
            {
                var previous = excluded[^1];
                excluded[^1] = previous with { ByteCount = (int)(Math.Max(previous.FileOffset + previous.ByteCount,
                    range.FileOffset + range.ByteCount) - previous.FileOffset) };
            }
            else excluded.Add(range);
        }
        foreach (var part in packedParts)
        {
            ct.ThrowIfCancellationRequested();
            var scan = CprMidiPartNoteScanner.Scan(bytes.AsSpan(part.Start, part.End - part.Start),
                archiveFileOffset + part.Start, 10000 - midiNotes.Count,
                Math.Min(CprMidiPartNoteScanner.MaximumScanBytes, maxPackedMidiBytes - packedMidiScanBytes), ct, isRif2, excluded);
            packedMidiScanBytes += scan.BytesScanned;
            foreach (var note in scan.Notes)
                midiNotes.Add(note with
                {
                    CandidateTrackName = part.Track is int owner ? hints[owner].Name : null,
                    CandidateTrackOffset = part.Track is int ownerIndex ? hints[ownerIndex].ArchiveOffset : null,
                    CandidatePartOffset = archiveFileOffset + part.Header
                });
            if (scan.Partial) limits.Add("Packed MIDI byte/note scan budget reached; part observations are partial");
        }
        return new CprArchiveSummary(stream, size, bytes.Length,
            total, limits.Count > 0, counts, hints, midiNotes.OrderBy(note => note.FileOffset).ToArray(), audioMediaCandidates, limits.ToArray());

        CprRif2MidiNoteCandidate WithContext(CprRif2MidiNoteCandidate note) => note with
        {
            CandidateTrackName = activeTrack is int owner ? hints[owner].Name : null,
            CandidateTrackOffset = activeTrack is int track ? hints[track].ArchiveOffset : null,
            CandidatePartName = note.CandidatePartName ?? activePart?.Name,
            CandidatePartOffset = activePart?.Offset
        };
    }

    private static IReadOnlyList<(string Path, int Start)> ReadAudioPathCandidates(byte[] bytes, int start, int end,
        CancellationToken ct, int maxCandidates)
    {
        ReadOnlySpan<string> extensions = [".wav", ".wave", ".aif", ".aiff", ".flac", ".mp3", ".ogg", ".opus", ".caf", ".w64"];
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var extensionStart = start; extensionStart < end; extensionStart++)
        {
            if ((extensionStart & 1023) == 0) ct.ThrowIfCancellationRequested();
            foreach (var extension in extensions)
            {
                if (extensionStart + extension.Length > end) continue;
                var matched = true;
                for (var index = 0; index < extension.Length; index++)
                    if (char.ToLowerInvariant((char)bytes[extensionStart + index]) != extension[index])
                    { matched = false; break; }
                if (!matched) continue;

                var pathStart = extensionStart;
                while (pathStart > start && extensionStart - pathStart < 1024 && IsPathStringByte(bytes[pathStart - 1]))
                    pathStart--;
                var pathLength = extensionStart + extension.Length - pathStart;
                if (pathLength is < 5 or > 1024) continue;
                string candidate;
                try { candidate = new UTF8Encoding(false, true).GetString(bytes, pathStart, pathLength).Trim(); }
                catch (DecoderFallbackException) { continue; }
                if (candidate.Length < 5 || candidate.Any(char.IsControl) ||
                    !Path.GetExtension(candidate).Equals(extension, StringComparison.OrdinalIgnoreCase)) continue;
                if (!found.ContainsKey(candidate)) found.Add(candidate, pathStart);
                if (found.Count >= maxCandidates)
                    return found.Select(item => (item.Key, item.Value)).ToArray();
                extensionStart += extension.Length - 1;
                break;
            }
        }
        return found.Select(item => (item.Key, item.Value)).ToArray();
    }

    private static bool IsPathStringByte(byte value) => value is >= 32 and not 127;

    private static string? ReadBoundedName(byte[] bytes, int start, int end)
    {
        if (start < 0 || start > end - 4) return null;
        var length = BigEndian32(bytes, start);
        if (length is < 2 or > 512 || length > end - start - 4) return null;
        var value = bytes.AsSpan(start + 4, (int)length);
        var nul = value.IndexOf((byte)0);
        if (nul is < 1 or > 256) return null;
        var stringBytes = value[..nul];
        string text;
        try { text = new UTF8Encoding(false, true).GetString(stringBytes); }
        catch (DecoderFallbackException) { return null; } // Other ANSI code pages need explicit format adapters.
        if (text.Any(char.IsControl)) return null;
        return text;
    }

    private static bool TryLegacyListNodeName(byte[] bytes, int start, int end,
        string trackClass, out string? candidate)
    {
        candidate = null;
        ReadOnlySpan<byte> marker = trackClass switch
        {
            "MMidiTrackEvent" => [0x80, 0x00, 0x01, 0x7C],
            "MAudioTrackEvent" => [0x08, 0x00, 0x01, 0x7D],
            _ => default
        };
        if (marker.IsEmpty || end - start < marker.Length + 8) return false;
        var body = bytes.AsSpan(start, end - start);
        var markerOffset = body.IndexOf(marker);
        if (markerOffset < 0) return false;
        // The SX2/SX3 notes show marker, sub-section length, then a length-prefixed
        // track name. Preserve it as a candidate; do not promote the guessed layout.
        candidate = ReadBoundedName(bytes, start + markerOffset + 8, end);
        return candidate is not null;
    }

    private static bool TryRecord(byte[] bytes, int start, int limit,
        Dictionary<uint, string> names, out string className, out int bodyStart, out int bodyEnd)
    {
        className = "";
        bodyStart = bodyEnd = 0;
        if (start > limit - 4) return false;
        var firstCode = BigEndian32(bytes, start);
        if (firstCode is not (0xFFFFFFFEu or 0xFFFFFFFFu) &&
            (firstCode < 0x80000000u || !names.ContainsKey(firstCode & 0x7FFFFFFFu))) return false;
        var pending = new List<(uint Id, string Name)>();
        var p = start;
        for (var chain = 0; chain < 16; chain++)
        {
            if (p > limit - 4) return false;
            var marker = BigEndian32(bytes, p);
            if (marker != 0xFFFFFFFEu) break;
            p += 4;
            if (p > limit - 4) return false;
            var nameCode = BigEndian32(bytes, p);
            if (nameCode >= 0x80000000u && nameCode is not (0xFFFFFFFEu or 0xFFFFFFFFu))
            {
                var id = nameCode & 0x7FFFFFFFu;
                if (!names.ContainsKey(id) && !pending.Any(v => v.Id == id)) return false;
                p += 4;
            }
            else
            {
                if (!TryClassName(bytes, p, limit, out var name, out var next)) return false;
                pending.Add(((uint)(p - 4), name));
                p = next;
            }
        }
        if (p > limit - 8) return false;
        var code = BigEndian32(bytes, p);
        if (code == 0xFFFFFFFFu)
        {
            if (!TryClassName(bytes, p + 4, limit, out className, out var next))
                return false;
            pending.Add(((uint)p, className)); // name starts at (p + 4), id is relative to ARCH offset +4
            p = next;
        }
        else if (code >= 0x80000000u && code is not (0xFFFFFFFEu or 0xFFFFFFFFu))
        {
            var id = code & 0x7FFFFFFFu;
            if (!names.TryGetValue(id, out var found))
            {
                var candidate = pending.FirstOrDefault(v => v.Id == id);
                if (candidate.Name is null) return false;
                found = candidate.Name;
            }
            className = found;
            p += 4;
        }
        else return false;
        if (p > limit - 4) return false;
        var size = BigEndian32(bytes, p);
        bodyStart = p + 4;
        if (size > limit - bodyStart) return false;
        bodyEnd = bodyStart + (int)size;
        foreach (var entry in pending) names[entry.Id] = entry.Name;
        return true;
    }

    // Cubase 15 RIF2 records use 64-bit base-class/symbol sentinels and
    // 64-bit body sizes; class-name string lengths remain 32-bit.
    // This is a structural inventory only, not a full semantic deserializer.
    private static bool TryRecord64(byte[] bytes, int start, int limit,
        Dictionary<uint, string> names, out string className, out int bodyStart, out int bodyEnd)
    {
        className = "";
        bodyStart = bodyEnd = 0;
        if (start > limit - 8) return false;
        var firstCode = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(start, 8));
        if (firstCode != ulong.MaxValue && firstCode != ulong.MaxValue - 1 &&
            ((firstCode & 0x8000000000000000UL) == 0 ||
             (firstCode & 0x7FFFFFFFFFFFFFFFUL) > uint.MaxValue ||
             !names.ContainsKey((uint)(firstCode & 0x7FFFFFFFFFFFFFFFUL)))) return false;
        var pending = new List<(uint Id, string Name)>();
        var p = start;
        for (var chain = 0; chain < 16; chain++)
        {
            if (p > limit - 8 || BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(p, 8)) != ulong.MaxValue - 1)
                break;
            p += 8;
            if (p > limit - 4) return false;
            var nameCode = BigEndian32(bytes, p);
            if (nameCode >= 0x80000000u && nameCode is not (0xFFFFFFFEu or 0xFFFFFFFFu))
            {
                var id = nameCode & 0x7FFFFFFFu;
                if (!names.ContainsKey(id) && !pending.Any(v => v.Id == id)) return false;
                p += 4;
            }
            else
            {
                if (!TryClassName(bytes, p, limit, out var name, out var next)) return false;
                pending.Add(((uint)(p - 8), name));
                p = next;
            }
        }
        if (p > limit - 8) return false;
        var code = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(p, 8));
        if (code == ulong.MaxValue)
        {
            if (!TryClassName(bytes, p + 8, limit, out className, out var next)) return false;
            pending.Add(((uint)p, className));
            p = next;
        }
        else if ((code & 0x8000000000000000UL) != 0 && code != ulong.MaxValue - 1)
        {
            var id = code & 0x7FFFFFFFFFFFFFFFUL;
            if (id > uint.MaxValue) return false;
            if (!names.TryGetValue((uint)id, out var found))
            {
                var candidate = pending.FirstOrDefault(v => v.Id == (uint)id);
                if (candidate.Name is null) return false;
                found = candidate.Name;
            }
            className = found;
            p += 8;
        }
        else return false;
        if (p > limit - 8) return false;
        var rawSize = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(p, 8));
        bodyStart = p + 8;
        if (rawSize > (ulong)(limit - bodyStart)) return false;
        bodyEnd = bodyStart + (int)rawSize;
        foreach (var entry in pending) names[entry.Id] = entry.Name;
        return true;
    }

    private static bool TryClassName(byte[] bytes, int p, int end, out string name, out int next)
    {
        name = "";
        next = p;
        if (p > end - 4) return false;
        var len = BigEndian32(bytes, p);
        if (len is < 2 or > 100 || len > end - p - 6) return false;
        var count = (int)len;
        if (bytes[p + 4 + count - 1] != 0) return false;
        for (var i = p + 4; i < p + 4 + count - 1; i++)
            if (bytes[i] is < 32 or > 126) return false;
        name = Encoding.ASCII.GetString(bytes, p + 4, count - 1);
        next = p + 4 + count + 2;
        return true;
    }

    public static string ToMarkdown(CprStructureReport report)
    {
        var b = new StringBuilder("# CPR structure inventory\n\n");
        b.AppendLine("- Source: " + report.Path).AppendLine("- Version text candidate: " +
            (report.VersionTextCandidate ?? "Unknown")).AppendLine("- Tracks and plugins: NOT decoded")
            .AppendLine("- Container: " + (report.ContainerMagic ?? "Unknown"))
            .AppendLine("- Source project unchanged.");
        if (report.SizeDiagnostics is { } sizes)
        {
            b.AppendLine().AppendLine("## RIFF/NUND size diagnostics")
                .AppendLine("These are competing header interpretations, not proof of the container grammar.")
                .AppendLine("- Signature recognized: " + sizes.HasRiffNundSignature)
                .AppendLine("- Big-endian size field: " + sizes.SizeFieldBigEndian +
                    "; candidate total length (+12): " + sizes.BigEndianTotalLength +
                    "; matches actual: " + sizes.BigEndianTotalLengthMatches)
                .AppendLine("- Little-endian size field: " + sizes.SizeFieldLittleEndian +
                    "; candidate total length (+12): " + sizes.LittleEndianTotalLength +
                    "; matches actual: " + sizes.LittleEndianTotalLengthMatches)
                .AppendLine("- Actual file length: " + sizes.ActualLength);
        }
        b.AppendLine().AppendLine("## Candidate container chunks")
            .AppendLine("| Chunk | Offset | Payload bytes | Stream | Class |")
            .AppendLine("|---|---:|---:|---|---|");
        foreach (var c in report.Chunks)
            b.AppendLine("| " + c.Id + " | " + c.Offset + " | " + c.PayloadBytes +
                " | " + (c.StreamName ?? "") + " | " + (c.ArchiveClass ?? "") + " |");
        if (report.ArchiveInventories is { Count: > 0 } inventories)
        {
            b.AppendLine().AppendLine("## Detailed ARCH evidence")
                .AppendLine("Class headers and byte containment are candidates, not verified objects, plugin identities or ownership. Offsets are absolute file offsets except the ARCH-relative symbol offset. Uninterpreted bytes include ordinary opaque/scalar data and may overlap separate raw-field candidates; they do not imply corruption.");
            foreach (var inventory in inventories)
            {
                b.AppendLine().AppendLine("### " + EscapeTable(inventory.StreamName) +
                    " / " + EscapeTable(inventory.ArchiveClass ?? "Unknown class"))
                    .AppendLine($"- Payload file offset: {inventory.PayloadFileOffset}; grammar: {inventory.Grammar}")
                    .AppendLine($"- Scanned: {inventory.BytesScanned} / {inventory.DeclaredBytes} bytes; header matches: {inventory.RecordsFound}; retained details: {inventory.Records.Count}")
                    .AppendLine($"- Class symbols retained: {inventory.SymbolsFound}")
                    .AppendLine($"- Uninterpreted scanned bytes: {inventory.UninterpretedBytes}; partial: {inventory.Partial}");
                foreach (var reason in inventory.LimitReasons) b.AppendLine("- Limit: " + reason);
                b.AppendLine().AppendLine("| Class | Header file offset | Body file offset | Body bytes | Depth | Enclosing header | Class version | Encoding | Class token | Symbol offset | Prefix classes |")
                    .AppendLine("|---|---:|---:|---:|---:|---:|---:|---|---|---:|---|");
                foreach (var record in inventory.Records)
                    b.AppendLine($"| {EscapeTable(record.ClassName)} | {record.HeaderFileOffset} | {record.BodyFileOffset} | {record.BodyBytes} | {record.Depth} | {record.ParentHeaderFileOffset?.ToString() ?? ""} | {record.ClassVersion?.ToString() ?? ""} | {record.ClassEncoding} | 0x{record.ClassToken:X} | {record.ClassSymbolArchiveOffset?.ToString() ?? ""} | {EscapeTable(string.Join(", ", record.PrefixClasses))} |");
                var rawFields = inventory.Records.SelectMany(record => record.RawFields ?? []).ToArray();
                if (rawFields.Length > 0)
                {
                    b.AppendLine().AppendLine("Anonymous binary-layout field candidates (no BPM, timing units or musical roles established):")
                        .AppendLine().AppendLine("| Field | Type | Value | File offset | Raw hex | Source |")
                        .AppendLine("|---|---|---|---:|---|---|");
                    foreach (var field in rawFields)
                        b.AppendLine($"| {field.Field} | {field.StorageType} | {EscapeTable(field.Value)} | {field.FileOffset} | {field.RawHex} | {EscapeTable(field.Source)} |");
                }
                if (inventory.UninterpretedRanges.Count > 0)
                {
                    b.AppendLine().AppendLine("Uninterpreted ranges (file offset + bytes; first 32 retained ranges): " +
                        string.Join(", ", inventory.UninterpretedRanges.Take(32).Select(range =>
                            $"{range.FileOffset} + {range.ByteCount}")) + ".");
                    if (inventory.UninterpretedRanges.Count > 32)
                        b.AppendLine("All retained ranges are available in JSON output.");
                }
            }
        }
        if (report.Arrangement is not null)
        {
            b.AppendLine().AppendLine("## Arrangement record classes")
                .AppendLine("Scanned bytes: " + report.Arrangement.BytesScanned +
                    "; incomplete: " + report.Arrangement.Partial)
                .AppendLine().AppendLine("| Record class | Observed headers |").AppendLine("|---|---:|");
            foreach (var entry in report.Arrangement.ClassCounts)
                b.AppendLine("| " + entry.Key + " | " + entry.Value + " |");
            b.AppendLine().AppendLine("### Track and lane class instances")
                .AppendLine("The class name matches a type used by Cubase serialization code. Names and visible-track hierarchy come from bounded containment and are not guaranteed.")
                .AppendLine("| Kind | Record class | Observed name | ARCH-relative offset | Name source |")
                .AppendLine("|---|---|---|---:|---|");
            foreach (var hint in report.Arrangement.TrackHints)
                b.AppendLine("| " + (CprTrackRecordKinds.KindFor(hint.RecordClass) ?? "Unknown") + " | " + hint.RecordClass + " | " +
                    (hint.Name?.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ") ?? "Unknown") +
                    " | " + hint.ArchiveOffset + " | " + EscapeTable(hint.NameSource ?? "Unknown source") + " |");
        }
        if (report.Arrangement?.MidiNoteCandidates is { Count: > 0 } midiNotes)
        {
            b.AppendLine().AppendLine("## Native CPR MIDI note candidates")
                .AppendLine("Layouts: " + string.Join(", ", midiNotes.Select(note => note.SourceLayout).Distinct()))
                .AppendLine("Track/part labels are candidate byte containment. Timing units, placement and complete MIDI semantics remain unverified.")
                .AppendLine().AppendLine("| File offset | Position (ticks) | Duration (ticks) | MIDI pitch | Velocity |")
                .AppendLine("|---:|---:|---:|---:|---:|");
            foreach (var note in midiNotes)
                b.AppendLine("| " + note.FileOffset + " | " + note.PositionTicks +
                    " | " + note.DurationTicks + " | " + note.MidiPitch + " | " + note.Velocity + " |");
        }
        if (report.NamedRecordCandidates is { Count: > 0 } candidates)
        {
            b.AppendLine().AppendLine("## Cubase 12 named-record candidates")
                .AppendLine("These records match an empirical Cubase 12 channel-name anchor and bounded named-record grammar. Field meaning is not independently verified.")
                .AppendLine().AppendLine("| Stream | File offset | Field | Type | Candidate value |")
                .AppendLine("|---|---:|---|---|---|");
            foreach (var candidate in candidates)
                foreach (var value in candidate.Values)
                    b.AppendLine("| " + EscapeTable(candidate.StreamName) + " | " + value.ArchiveOffset + " | " +
                        EscapeTable(value.Path) + " | " + EscapeTable(value.Type) + " | " + EscapeTable(value.Value) + " |");
        }
        if (report.MetadataCandidates is { Count: > 0 } metadata)
        {
            b.AppendLine().AppendLine("## Typed metadata candidates (not decoded project values)")
                .AppendLine("Patterns follow the CubaseTools public research notes. These are bounded byte-pattern observations, not verified object ownership or project settings.")
                .AppendLine().AppendLine("| Field | Candidate value | File offset | Pattern | Status |")
                .AppendLine("|---|---|---:|---|---|");
            foreach (var item in metadata)
                b.AppendLine("| " + EscapeTable(item.Field) + " | " + EscapeTable(item.Value) + " | " +
                    item.ArchiveOffset + " | " + EscapeTable(item.SourcePattern) + " | " + EscapeTable(item.Status) + " |");
        }
        b.AppendLine().AppendLine("## Limitations");
        foreach (var warning in report.Warnings) b.AppendLine("- " + warning);
        return b.ToString();
    }

    private static string EscapeTable(string value) => value.Replace("|", "\\|")
        .Replace("\r", " ").Replace("\n", " ");
}

internal static class CprSpanValidation
{
    public static bool AnyNonAsciiControl(this ReadOnlySpan<byte> span)
    {
        foreach (var b in span)
            if (b is < 32 or > 126) return true;
        return false;
    }
}

