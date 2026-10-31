# Cubase Project Formats — Reverse Engineering Research and Reader Implementation Blueprint

> **REVISION 3 — Cubase 15 EXE static analysis, 2026-10-31.** The appended sections 34–44 provide binary-level evidence for native `RIFF` and `RIF2` variants, the big-endian 32/64-bit size-like field, `NUND`, `ROOT`/`ARCH`, candidate model strings, and concrete function addresses. This improves and updates the earlier RIFF-only assumptions in sections 1–33. **Do not interpret the old RIFF-only text as excluding RIF2.** The first-stage reader source and a reproducible evidence script are delivered separately.


**Research date:** 2026-10-30 (expanded forum + source-code audit, revision 2)  
**Target:** CubaseProjectExplorer, read-only native parser in C# / .NET 10  
**Scope:** `.cpr`, `.npr`, `.bak`, historic `.all`/`.arr`, associated project and interchange formats  
**Objective:** Get as close as technically possible to complete project inspection, with explicit evidence and confidence tracking; do not claim that a proprietary field has been decoded until real samples validate it.

> **Important qualification:** No complete, authoritative public specification for current Cubase `.cpr` binary internals was established in this research. A RIFF-like wrapper is not a fully documented object model. Parsing arbitrary tracks, notes, routing and plug-in state requires systematic empirical reverse engineering with controlled Cubase-generated samples. The document distinguishes proven facts, implementation recommendations, and hypotheses.

> **Revision 2 — materially new evidence.** Sections 18–33 add a source-level audit of the MIT-licensed [CubaseTools](https://github.com/schwifty00/CubaseTools) modern CPR parser, concrete SX2/3 object-token evidence, the independent [CubaseMarkers.js](https://gist.github.com/andigamesandmusic/94be52eff000013d7cdf5daef54bc4aa) marker reader, October 2026 forum developments, documented DAWproject gaps, VST persistence semantics, a safer .NET implementation strategy, and a prioritized evidence ledger. The original sections 1–17 are retained for continuity; **where the two conflict, the more specific revision-2 audit and source-level cautions take precedence**. This is a research implementation blueprint, not an official Steinberg specification.

## 1. Executive findings

1. Modern Cubase uses `.cpr`; the closely related Nuendo project uses `.npr`. They store session metadata and references to project media, rather than replacing the associated audio/video files.
2. A recognisable CPR signature is ASCII `RIFF` at offset `0`, followed by a size-like field, and ASCII `NUND` at offset `8`. The `NUND` signature is used by TestDisk/PhotoRec's CPR file recognizer. **Do not automatically parse the wrapper as canonical RIFF with little-endian length fields.** TestDisk interprets bytes 4–7 as a big-endian integer and estimates `length = value + 12`. This is a documented observation of that tool, **not** a verified specification for every Cubase generation.
3. Steinberg changed its project format in Cubase/Nuendo **13.0.30** to support projects over 2 GB. Earlier versions can corrupt oversized projects. A reader needs independent legacy/large-format handling and 64-bit offsets.
4. `.all` (Cubase Song) and `.arr` (Arrangement) are **older, distinct formats**, used by Cubase VST and earlier. Cubase SX 3 was the last Cubase generation with a conversion workflow to `.cpr`; this historical importer is not the same as a native modern CPR parser.
5. One public repository specifically targets reading/editing **Cubase SX 2/3 `.cpr`**. Another offers a **conservative** RIFF-aware evidence scanner but explicitly does not claim complete semantic decoding. These are complementary research starting points, not a complete modern reader.
6. Cubase **Track Archive XML** exports tracks, channel settings, parts/events, and automation but omits project-global settings such as tempo. It is a key semantic oracle when paired with the original CPR.
7. **DAWproject** (`.dawproject`, ZIP + XML) is openly specified and available in newer Cubase releases; it offers a useful second oracle for tracks, events, routing, plug-ins and automation, subject to feature/version limitations.
8. The most realistic target is **native CPR reader + optional XML/DAWproject/SMF evidence fusion**, preserving unknown blobs and distinguishing observed facts from guesses.

### Evidence levels used in this document

- **Confirmed:** directly stated by Steinberg documentation or directly visible in identified source code.
- **Observed in third-party implementation:** explicit implementation or description, may not generalize across versions.
- **Hypothesis/test required:** plausible structural interpretation not yet demonstrated on multiple CPR fixtures.
- **Recommendation:** engineering decision, not a statement about proprietary format internals.

## 2. Format taxonomy and version coverage

| Format | Use | Relation to project reader | Confidence |
|---|---|---|---|
| `.cpr` | Cubase project, SX generation onward | **Primary binary reader target** | Confirmed |
| `.npr` | Nuendo project | Related project family; independently validate signature/version | Confirmed as project type; shared internals not fully verified |
| `.bak` | Backup project | Probe by content signature, not extension; backups may have full project structure | Project backup existence confirmed; exact encoding requires samples |
| `.all` | Historical Cubase Song | Separate legacy decoder or SX3 conversion path | Confirmed |
| `.arr` | Historical Cubase Arrangement | Separate legacy decoder or SX3 conversion path | Confirmed |
| Track Archive `.xml` | Exported selected tracks | Semantic oracle and optional alternative import | Confirmed |
| `.dawproject` | Cross-DAW project exchange | Openly documented structured importer and validation oracle | Confirmed |
| `.mid` / `.midi` | Standard MIDI File | Independent notes/CC/tempo cross-check, with conversion caveats | Confirmed |
| `.wav`, `.aiff`, `.bwf`, `.mxf` | Media referenced by project | Media resolver, metadata validation | Confirmed associated media types |
| `.peak`, `.ovw`, `.csh` | Waveform/cache/overview adjuncts | Optional, not canonical session state | Identified extensions; semantics vary |
| `.drm`, `.expressionmap`, preset files | Related user artifacts | Optional enrichment only; not substitutes for CPR internals | Identified formats |

**Version tiers for testing (proposed):** SX 1–3; Cubase 4–5; 6–8; 9–11; 12; 13 before 13.0.30; 13.0.30+; 14; 15; matching Nuendo variants. These are **test cohorts**, not claims of distinct wire protocols. Introduce version-specific parser strategies only when fixture evidence supports them.

## 3. Observed native binary envelope

### 3.1 Signature

Known signature reported by file recovery tools:

```text
Offset  Size   Meaning                       Status
0x00    4      ASCII "RIFF"                  Observed/verified in TestDisk detection
0x04    4      Size-like integer             TestDisk reads big-endian; full semantics unproven
0x08    4      ASCII "NUND"                  Observed/verified in TestDisk detection
0x0C    ...    Subsequent binary content     No universal field grammar verified
```

**Critical endianness caveat:** Standard RIFF uses little-endian chunk sizes, but in TestDisk's special `NUND` branch the four bytes from offset 4 are explicitly assembled **big-endian** before adding 12. The program's *other* RIFF branches use little-endian sizes. This establishes the behavior of that recovery implementation, **not universal chunk grammar, header correctness, or all version sizes**. Implement competing size interpretations and validate them against actual samples, especially 13.0.30+ and files >2 GB. Do not apply ordinary RIFF `LIST`/padding parsing until actual bytes prove it.

### 3.2 What has not been established

The sources inspected do **not** establish, for all versions:

- the exact top-level chunk table, per-chunk size endianness or alignment;
- a canonical list of class IDs or serialization object headers;
- the persistence schema for tracks, events, notes, automation, time conversion and routing;
- universal compression markers and compression algorithm;
- complete VST2/VST3 plug-in wrapper/parameter encoding;
- post-13.0.30 64-bit container rules;
- a stable mapping from binary offsets to Cubase UI values.

Treat any internet claim of a fully documented modern CPR structure skeptically unless it includes a public decoder, controlled input samples, and expected/actual comparisons.

### 3.3 Safe first-pass classification algorithm

1. Open the input **read-only** and compute SHA-256.
2. Check first 16–64 bytes; identify signatures; do not classify from extension alone.
3. If `RIFF` + `NUND`, classify as a **candidate** Cubase-family container.
4. Record both BE and LE interpretation of bytes 4–7, using unsigned arithmetic; compare derived sizes with actual file length and any observed trailing data.
5. Record strings and plausible producer/version markers **with byte offsets and encoding**, but do not promote proximity to structural links.
6. Investigate section boundaries by validated per-version grammars, not by scanning indiscriminately for printable FourCC sequences.
7. For unsupported, damaged or truncated files, emit partial evidence and diagnostics; do not silently fabricate a project graph.

### 3.4 Minimal C# probing code

This deliberately handles **classification only**, not project parsing:

```csharp
using System.Buffers.Binary;
using System.Text;

public sealed record CprProbe(
    bool HasRiffNundSignature,
    uint SizeFieldBigEndian,
    uint SizeFieldLittleEndian,
    long ActualLength,
    ulong TestDiskEstimatedLength,
    bool TestDiskEstimateMatches);

public static CprProbe Probe(Stream source)
{
    if (!source.CanSeek || !source.CanRead)
        throw new ArgumentException("A readable seekable stream is required.");

    long originalPosition = source.Position;
    try
    {
        source.Position = 0;
        Span<byte> header = stackalloc byte[12];
        source.ReadExactly(header);
        bool signature =
            header[..4].SequenceEqual("RIFF"u8) &&
            header[8..12].SequenceEqual("NUND"u8);

        uint be = BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);
        uint le = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        ulong predicted = (ulong)be + 12UL;
        return new CprProbe(signature, be, le, source.Length,
                            predicted, predicted == (ulong)source.Length);
    }
    finally { source.Position = originalPosition; }
}
```

**Important:** `TestDiskEstimatedLength` is a comparative diagnostic; it must **not** be treated as a validated length or a security boundary, nor extrapolated to the large-file variant.

## 4. Modern CPR reverse engineering: required experiments

### 4.1 Controlled fixture principle

Use a Cubase installation to produce pairs (or triplets) that differ in **exactly one deliberate semantic property**, then byte-diff files and correlate structured export data. Keep a baseline fixture and a CSV/JSON manifest with Cubase build, OS, feature flag, project configuration, edit, expected value, and file hashes.

Create each fixture as:

```text
fixtures/<cubase-version>/<case>/
    baseline.cpr
    changed.cpr
    exported-track-archive.xml
    exported.dawproject        # when available/supported
    exported.mid               # where relevant
    expected.json
    sha256.txt
    observations.md
```

Disable or document features that inject nondeterministic changes, and run each case multiple times to separate timestamps, IDs, random seeds, autosaves and cache changes from the controlled variable.

### 4.2 Essential experiment matrix

| Experiment | Control/edit | Intended discovery |
|---|---|---|
| Empty project | Save fresh blank project twice | Header, version, IDs, nondeterministic bytes |
| Project title | Rename only | String storage, length encoding |
| Audio track | Add exactly one; rename | Track objects, track type/name |
| Folder/group/FX | Add one type per sample | Type discriminators, hierarchy |
| MIDI track | Add one track then one part | Track, part and event links |
| MIDI note | One note, vary pitch/velocity/start/duration separately | Value encoding and timebase |
| MIDI CC | One CC point, then move/value change | Controller structures |
| Audio event | One imported known WAV, move/trim/fade | References, positions, clip offsets |
| Tempo | Change constant tempo, then add step/ramp | Tempo graph and interpolation |
| Meter | Change 4/4 to 3/4; add changes | Signature encoding |
| Track fader | Change one channel level | Gain encoding and channel associations |
| Pan | Change left/center/right | Pan law/value mapping |
| Routing | Route track to different group | Object identity/references |
| Sends | Toggle send; vary gain/destination | Send graph |
| Automation | Two points, change interpolation | Automation nodes and interpolation |
| VST3 | Insert plug-in with small state, then one param change | Plug-in wrapper and opaque state |
| VST2 legacy | Same, where available | Format-specific wrappers |
| Track versions | Add alternate version | Alternate state/hierarchy |
| Chord/marker | Add one each | Timeline objects |
| Media relink | Change media path only | Path encoding and external references |
| Undo/save | Save without changing project | Serialization nondeterminism |
| Large file | Produce >2GB only in isolated controlled environment | 13.0.30+ size format and bounds |
| Mixed versions | Save a clone across Cubase versions | Version-aware schema evolution |

### 4.3 Binary analysis techniques

- **Rolling byte diff / aligned diff:** naive byte differences become misleading after insertion; use alignment and content-defined chunks.
- **Entropy windows:** detect opaque compressed/encrypted-like regions; entropy alone cannot distinguish encryption from well-compressed content.
- **String indexing:** ASCII, UTF-8, UTF-16LE/BE with offsets and confidence; preserve the surrounding original bytes.
- **Integer hypothesis testing:** BE/LE 16/32/64, IEEE float/double, variable-length integers; compare results against multiple controlled values.
- **Pointer/ID correlation:** find repeated IDs across tracks, routing and events; do not presume endianness or width from one example.
- **Temporal calibration:** test bar/beat/tick, musical time, seconds and sample positions with multiple BPMs and project sample rates.
- **Semantic differential:** correlate changed CPR regions with XML/DAWproject changes and expected.json.
- **Version regression:** replay the same experiments on SX, 4–12, 13 pre/post fix, 14 and 15.
- **Compression probing:** attempt zlib/deflate/gzip/LZ4/Zstandard only on bounded *candidate* payloads with plausible signatures/frames; do not brute-force the whole file as if it were one compressed stream.
- **Blob extraction:** retain unknown plug-in states byte-for-byte; never interpret arbitrary bytes as track names or paths without evidence.

## 5. Public codebases and their actual capabilities

### 5.1 `omeriko9/Cubase-Project-File-Reverse-Engineering`

- URL: https://github.com/omeriko9/Cubase-Project-File-Reverse-Engineering
- Description: Cubase Project File Viewer and Editor for **versions 2 and 3**, work in progress.
- Research value: potentially the strongest public example of early CPR object decoding and a starting point for legacy SX structures. Its C#/Visual Studio solution is worth auditing closely.
- **Do not assume** support for Cubase 4–15, or lift guessed chunk layouts untested into a modern parser.
- Follow-up: locally inspect every parser source file, fixtures (if any), source license, class identifiers, binary primitives, string lengths, recursion patterns and error handling. Build and run tests before reuse.

### 5.2 `ColonelKernel/session-state-explorer-cubase`

- URL: https://github.com/ColonelKernel/session-state-explorer-cubase
- Description: cross-evidence Cubase session explorer with `cpr-lab`, DAWproject, Track Archive, MIDI and runtime extractors.
- Crucial limitation: its CPR component is described as **RIFF-aware conservative evidence extraction**, with string/token proximity and plug-in candidates; it explicitly does **not** establish full native structural state.
- Research value: provenance-driven evidence model, fixture protocol, unknown-state accounting and differential validation.
- Not a drop-in full CPR parser.

### 5.3 TestDisk/PhotoRec `file_riff.c`

- URL: https://git.cgsecurity.org/cgit/testdisk/tree/src/file_riff.c
- Confirms recognition of ASCII `RIFF` + `NUND`, and explicitly uses a BE size expression + 12 for its CPR recovery estimate.
- **Scope:** file recovery detection, not a semantic Cubase parser. Its length calculation must not be copied unquestioningly as a format specification.

### 5.4 `orchetect/swift-daw-file-tools`

- URL: https://github.com/orchetect/swift-daw-file-tools
- Cubase Track Archive XML support is currently declared for **marker tracks only**, with restrictions on tempo ramps.
- Research value: XML marker schema and musical-time conversion tests.

### 5.5 `bitwig/dawproject`

- URL: https://github.com/bitwig/dawproject
- Public stable 1.0 format: `.dawproject` ZIP containing XML (including `project.xml` and `metadata.xml`), media and embedded plug-in state according to format conventions.
- Research value: well-documented importer, XSD-based validation, reference model for tracks/events/automation/routing.
- **Not** a binary CPR decoder.

### 5.6 `SatyrDiamond/DawVert`, `git-moss/ProjectConverter`

- URLs: https://github.com/SatyrDiamond/DawVert and https://github.com/git-moss/ProjectConverter
- Listed by DAWproject as format converters. Audit their currently supported sources/targets and licenses independently; do **not** infer native `.cpr` capability from their presence in the DAWproject ecosystem.

### 5.7 Other public requests

- https://github.com/ozzaii/bravoh-daw/issues/3 — July 2026 issue requesting CPR support, with a narrow initial scope (version/track names/plug-in list), **not evidence of a completed parser**.

**Implementation rule:** before importing code, verify licenses, provenance, actual source behavior, supported releases, and current build status.

## 6. Track Archive XML as an oracle

Steinberg documents selected-track exports as `.xml` that can contain **channel settings, parts, events and automation**. Crucially, project-specific properties such as **tempo are not exported**. The export workflow can reference media or copy it to a new folder; newer versions offer a compatibility selector for older Cubase/Nuendo releases.

**Use it to map:**

- track type/name/order, hierarchy and channel associations;
- MIDI and audio parts/events, with controlled examples;
- channel volume/pan/sends, routing and insert references;
- instrument data and expression map associations where represented;
- automation lanes and values;
- ID references and data structures potentially shared conceptually with CPR.

**Known hard limit:** Steinberg/community examples show plug-in-specific state (e.g. Kontakt `.nki` selection) may be hidden inside large opaque compressed `audioComponent` state rather than a readable path. Do not conflate identifying a plug-in with understanding its internal preset patch or compressed state. Track archives cannot be trusted to reconstruct the entire project tempo map.

Implementation: secure streaming XML reader (`XmlReader`), DTD disabled, no external entity resolution, bounded depth and text size, trace each parsed field to XML path and original source.

## 7. DAWproject as a second oracle and usable importer

Format specification: https://github.com/bitwig/dawproject

- Container: ZIP; XML (`project.xml`, `metadata.xml`), UTF-8.
- Designed for portable exchange of tracks, clips, audio events, note expressions, automation, routing and plug-in state.
- The file is **not** a guaranteed lossless representation of all Cubase features and UI settings.
- Steinberg's current Cubase documentation lists import/export coverage for track types (audio, instrument, group, effect, marker, folder), output channels, mix controls, routing, automation and VST3 inserts. Coverage is **version-dependent**.
- Steinberg documents specific shortcomings in early Cubase 14.0.20 DAWproject support, including some MIDI-controller, tempo, event-transform and note issues. Do not expect all historical exports to match the 1.0 schema's full capabilities.

Version note: the Steinberg support page identifies Cubase 15 variants and Cubase Pro/Artist 14.0.20+ as supporting interchange. Verify exact edition/build and import/export capability on the installed test machine.

**Recommended plan:** build a complete DAWproject reader for a reliable feature baseline, while retaining raw CPR as the authoritative native source. For native CPR breakthroughs, compare canonical models with DAWproject outputs and explicitly label any features lost in export.

Secure ZIP parsing: cap entry count, compressed/uncompressed sizes and expansion ratio, reject traversal paths and symlinks, never extract blindly to disk, validate required entries and XML schema as appropriate.

## 8. `.all` and `.arr`: legacy is a separate project

Steinberg explains that `.all` Song and `.arr` Arrangement formats from the older Cubase VST line were replaced by `.cpr` with the SX architecture. Cubase SX3 was the last version with a built-in legacy import route; support was removed from Cubase 4 onward.

- **`.all`:** a song may contain multiple arrangements; a conversion process may require importing each arrangement separately.
- **`.arr`:** an arrangement in the older project family.
- Compatibility conversion is not necessarily lossless: community reports mention missing notes, plug-in effects or MIDI setup. Do not advertise it as equivalent to decoding the native old binary format.
- Distinguish `.all` from unrelated products using the same extension using signatures and structure.
- No public complete binary specifications for ALL/ARR were verified by this research. Therefore introduce `LegacyCubaseSongReader` and `LegacyCubaseArrangementReader` interfaces but **do not fabricate their layouts**.

Suggested support states: `Recognized`, `MetadataOnly`, `PartiallyDecoded`, `FullyDecodedForTestCorpus`, `Unsupported`, `Damaged`.

Useful source: https://helpcenter.steinberg.de/hc/en-us/articles/115000075750-Converting-Cubase-VST-songs-ALL-ARR-into-CPR-format

## 9. Domain model for CubaseProjectExplorer

A robust **loss-minimizing** model should not collapse Cubase distinctions into generic track objects:

```text
Project
 ├─ Identity / FormatInfo / ProducerInfo
 ├─ GlobalTiming
 │   ├─ TempoEvents (step/ramp; interpolation; precision)
 │   ├─ TimeSignatures
 │   └─ ClockConversion / SampleRate / FrameRate
 ├─ TrackTree
 │   ├─ FolderTracks / GroupChannels / FXChannels
 │   ├─ AudioTracks / MidiTracks / InstrumentTracks / MarkerTracks
 │   └─ TrackVersions
 ├─ Timeline
 │   ├─ AudioParts / AudioEvents / Regions / Fades / Warp
 │   ├─ MidiParts / Notes / CC / PitchBend / Aftertouch / NoteExpression
 │   ├─ Markers / CycleMarkers / ChordEvents
 │   └─ AutomationCurves
 ├─ MixerGraph
 │   ├─ Channels / Buses / Inputs / Outputs
 │   ├─ Inserts / Instruments / Sends
 │   └─ Routes / Sidechains / DirectRouting
 ├─ MediaPool
 │   ├─ ReferencedFiles / RelativePaths / OfflineMedia
 │   └─ FileHashes / MediaMetadata
 ├─ NativeData
 │   ├─ UnknownObjects / RawBlobReferences
 │   └─ OriginalOffsets / UnknownProperties
 └─ Diagnostics / Evidence / Coverage
```

### Typed C# model sketch

```csharp
public enum EvidenceKind { NativeDecoded, NativeObserved, TrackArchiveXml,
                           DawProject, StandardMidi, Heuristic }
public enum Confidence { Verified, Supported, Candidate, Unknown }

public sealed record SourceEvidence(
    string SourceSha256, string Format, EvidenceKind Kind,
    Confidence Confidence, long? Offset, long? Length,
    string? ObjectPath, string? ValidationCase);

public sealed record ObservedValue<T>(T? Value, IReadOnlyList<SourceEvidence> Evidence);

public sealed record NativeSpan(long Offset, long Length, string? ObjectType);

public sealed record UnknownNativeObject(
    NativeSpan Span, string Reason, IReadOnlyDictionary<string, string> Hypotheses);
```

Do not force unvalidated objects into `Verified` simply because their bytes contain a recognizable string. Prefer typed object relations with stable extracted identifiers and explicit unknowns.

### Distinct semantics to preserve

- Audio versus MIDI versus instrument tracks.
- Folder containment versus audio signal routing.
- Group versus FX channel.
- Instrument tracks versus rack instrument associations.
- Track mute versus channel mute versus automation.
- Source media offset versus project timeline offset and part/event nesting.
- Multiple timing bases and tempo ramps.
- Plug-in instance identity, VST format, binary state blob and automation parameter IDs.
- Track versions, expression maps, drum maps, note expression and articulations.

## 10. Suggested .NET 10 architecture

```text
src/
  CubaseProjectExplorer.Formats/
    IProjectFormatSniffer.cs
    ProjectReadResult.cs
    Evidence.cs
  CubaseProjectExplorer.Cpr/
    CprSignatureProbe.cs
    CprEnvelopeReader.cs
    CprBinaryCursor.cs
    CprVersionDetector.cs
    CprParserRegistry.cs
    CprObjectIndex.cs
    CprUnknownObjectStore.cs
    Versions/Sx2Sx3/
    Versions/Legacy4To12/
    Versions/V13PreLarge/
    Versions/V13LargeAndLater/
    Diagnostics/CprHexReport.cs
  CubaseProjectExplorer.Legacy/
    AllSniffer.cs
    ArrSniffer.cs
  CubaseProjectExplorer.TrackArchive/
    TrackArchiveReader.cs
  CubaseProjectExplorer.DawProject/
    DawProjectZipReader.cs
    DawProjectXmlReader.cs
  CubaseProjectExplorer.Midi/
    StandardMidiReader.cs
  CubaseProjectExplorer.Model/
    Project.cs
    MixerGraph.cs
    Timeline.cs
    Evidence.cs
  CubaseProjectExplorer.Fusion/
    ObservationMerger.cs
    ConfidenceRules.cs
  CubaseProjectExplorer.Cli/
    Commands/Probe.cs
    Commands/Inspect.cs
    Commands/Diff.cs
    Commands/Corpus.cs
    Commands/ExportJson.cs
    Commands/AnalyzeUnknowns.cs
  CubaseProjectExplorer.Tests/
    Fixtures/
    ParserRegressionTests.cs
    FuzzAndBoundaryTests.cs
```

### Reader phases

**P0 — Reliable file detection:** format probe, hashes, stream safety, no false confidence, diagnostic report.

**P1 — CPR binary atlas:** deterministic offset/region indexing, strings, version candidates, entropy histograms and annotated hex; support truncated inputs.

**P2 — Legacy SX parser:** independently audit/port existing SX2/3 project reader where verified; build regression fixtures. Do not infer modern support.

**P3 — Cubase 4–12 structural extraction:** track objects/IDs/parts/event trees only when mapping tests pass; use discovery evidence otherwise.

**P4 — Cubase 13+ and large-file variant:** dedicated 64-bit read paths, boundaries, size semantics, controlled >2 GB test cases.

**P5 — Musical timeline:** tempo/meter/timebase calibration, MIDI notes and events, audio clips, markers, automation.

**P6 — Mixer and plug-ins:** graph IDs, inserts, send/routing edges, opaque plug-in state blobs, known structured parameters.

**P7 — Legacy ALL/ARR:** independent documented sniffers/decoders or explicitly supported assisted conversion; do not alias as CPR.

**P8 — Cross-format validation:** Track Archive + DAWproject + MIDI evidence correlation; expose coverage, ambiguity, conflicts.

**P9 — Hardening and performance:** malicious/truncated files, archive bombs, very large projects, incremental/lazy reads, cancellation, fuzzing.

### CLI contract

```text
cpr-explorer probe file.cpr --json
cpr-explorer inspect file.cpr --summary --evidence
cpr-explorer atlas file.cpr --output atlas.json
cpr-explorer strings file.cpr --offsets --encoding auto
cpr-explorer diff baseline.cpr changed.cpr --align --report diff.html
cpr-explorer export file.cpr --format json --include-unknowns
cpr-explorer validate file.cpr --oracle track.xml --oracle project.dawproject
cpr-explorer corpus run fixtures/ --report coverage.json
```

These are **proposed commands**, not claimed to exist in the current repository.

## 11. Binary-reader correctness and resilience

- Do not write to source files; never use mutation or repair in the primary reader.
- Use **64-bit** offsets, seek positions, sizes and checked arithmetic end-to-end.
- Validate `offset <= length`, `size <= length - offset`, nesting levels, object counts, string limits, array lengths and timeouts.
- Never allocate user-declared lengths without limits; avoid full-file materialization, especially above 2 GB.
- Make version detection capability-based, not extension-based.
- Preserve raw unknown slices by file offset; avoid copying gigabyte opaque plug-in states into memory.
- Separate content-based facts from heuristic string hits in the UI and JSON export.
- Distinguish missing data, unrecognized data, encrypted/opaque data and unsupported feature data.
- Support partial decoding with stable diagnostics (`code`, `offset`, `severity`, `suspectedVersion`, `source`).
- Make parsers cancellation-aware; consider `RandomAccess.Read` / streams and bounded caches.
- Protect against XML entity expansion, ZIP traversal/zip bombs, pathological recursion, malformed UTF strings and bogus size headers.
- Test cross-platform path normalization without losing the original media path string.

## 12. Specific unknowns and research backlog

| Priority | Open question | How to answer conclusively |
|---|---|---|
| Critical | Modern CPR top-level chunk grammar? | Hex dumps + aligned empty/add-one-track fixtures across releases |
| Critical | CPR per-object identity/length/endianness? | Two tracks, reorder/rename, cross-reference diffs |
| Critical | 13.0.30+ large-size representation? | Paired pre/post version and safely generated >2 GB fixtures |
| Critical | Track hierarchy and class IDs? | One object of each track type, Track Archive comparisons |
| High | Timeline clock/timebase and units? | Deliberate BPM/meter/position tests |
| High | MIDI note structure and pitch/velocity scaling? | Single-variable note fixture matrix |
| High | Audio event versus pool/source offsets? | Known WAV + trim/move/duplicate tests |
| High | VST3 wrapper and plug-in state encapsulation? | Single simple VST3 with controlled parameter/state changes |
| High | Routing identity / directed mixer graph? | Small graph with group/FX/send/sidechain modifications |
| High | Automation curve interpolation? | Two/three nodes plus interpolation changes |
| Medium | Project color/display/UI-only state? | UI property differential pairs |
| Medium | Track versions and articulations? | Controlled feature fixture pairs |
| Medium | `.npr` relationship to CPR? | Nuendo-generated project with matched feature sets |
| Medium | `.bak` signature and recovery behavior? | Real backup generation across versions |
| Medium | Legacy ALL/ARR binary taxonomy? | Historical fixture collection + independent parser research |
| Medium | Unknown blob compression? | Encapsulation-boundary mapping + identified codec/frame tests |

## 13. Acceptance criteria: definition of “implemented”

A field is **implemented** only when:

1. It is decoded from the **actual selected source** (native CPR vs XML vs DAWproject clearly distinguished).
2. At least three contrasting fixtures cover its valid range and edge cases; an independent oracle or known user edit validates it.
3. There are no unrelated false-positive objects on negative fixtures.
4. It works with the explicitly listed Cubase build cohort; support is not extrapolated to other major versions.
5. Its extraction retains source provenance (offset/path), meaningful confidence and error diagnostics.
6. Malformed/truncated tests are safe and bounded.
7. Regression tests and a production build pass.

Recommended status reporting per format/version:

```json
{
  "format": "cpr",
  "versionFamily": "cubase-15",
  "status": "partial",
  "features": {
    "containerSignature": "verified",
    "trackNames": "unverified",
    "midiNotes": "unsupported",
    "routing": "unsupported",
    "pluginBinaryState": "opaque"
  }
}
```

The above JSON is an **illustrative status schema** and does **not** claim Cubase 15 track decoding already works.

## 14. Implementation plan / agent handoff prompt

> Work directly in the CubaseProjectExplorer repository on `main`. First read `AGENTS.md`, repository-specific skills, current parser code, tests and `PLAN.md`. Inventory existing CPR capabilities and preserve already working functionality. Convert the evidence-backed findings of this document into prioritized, actionable `PLAN.md` tasks. Implement substantial functional batches, not speculative UI work. After each batch run the relevant unit/integration tests and production build, correct failures, update feature-level coverage and source-offset evidence, and commit logically grouped changes. Continue through remaining actionable tasks without pausing after each batch. If missing Cubase-authored binary fixtures block actual decoding, do not fabricate internals: add the fixture-generating protocol and safe classification/evidence tools, document the precise missing inputs, and proceed with all unblocked work. Native CPR extraction must not be presented as XML/DAWproject decoding. Use the listed sources with license checks. Avoid rewriting unrelated UI. Keep `PLAN.md` accurate and mark a feature complete only with validating fixtures.

### Work package order

1. Audit current repository and write support matrix without regressions.
2. Add test fixture manifests, hashes, repeatable corpus runner and format sniffer.
3. Implement safe CPR `RIFF`/`NUND` recognition and dual-endian diagnostic probes.
4. Audit SX2/3 open-source decoder and port only proven, licensed portions.
5. Implement native CPR evidence atlas and aligned diff CLI.
6. Build Track Archive XML and DAWproject readers as validation oracles.
7. Map one track class at a time from real CPR fixtures, recording precise field offsets.
8. Decode timing, MIDI, media and mixer relationships through controlled tests.
9. Add version-specific decoders only after demonstrating actual incompatible grammars.
10. Implement limits/fuzz/performance diagnostics and publish a granular support matrix.

## 15. What is necessary to achieve much higher completeness

**Obtain real native `.cpr` files from Cubase 2/3, 4/5, 7/8, 10/11, 12, 13.0.20, 13.0.30+, 14 and 15, with author/edition and exact build logged.** Ideally obtain a small controlled project matrix from each generation and matching Track Archive XML / DAWproject exports. For >2 GB projects use synthetic/controlled oversized content with no commercially sensitive sound libraries.

A full reader will probably need iterative binary work beyond publicly available code. In particular, **plug-in internal state cannot be fully decoded generically**: VST state is often vendor-owned arbitrary binary, and can be retained or identified without recovering every preset or parameter.

## 16. Source register (reviewed 2026-10-30)

### Steinberg primary documentation

1. Cubase 15 project files/media layout and >2GB note: https://www.steinberg.help/r/cubase-pro/15.0/en/cubase_nuendo/topics/project_handling/project_handling_about_project_files_c.html
2. Cubase 13 release notes, changes in 13.0.30: https://www.steinberg.net/cubase/release-notes/13/
3. Steinberg support, oversized corrupt projects: https://helpcenter.steinberg.de/hc/en-us/articles/31885407408018-Cubase-Nuendo-Invalid-project-after-saving
4. Steinberg Track Archive format and coverage: https://www.steinberg.help/r/cubase-pro/15.0/en/cubase_nuendo/topics/track_handling/track_handling_exporting_tracks_as_track_archive_c.html
5. Track export workflow and older-version compatibility: https://www.steinberg.help/r/cubase-pro/15.0/en/cubase_nuendo/topics/track_handling/track_handling_exporting_tracks_as_track_archives_t.html
6. Steinberg DAWproject interchange/capability limitations: https://helpcenter.steinberg.de/hc/en-us/articles/25142209226642-DAWproject-Exchange-Cubasis-projects-with-Cubase-and-other-DAWs
7. Steinberg legacy ALL/ARR conversion: https://helpcenter.steinberg.de/hc/en-us/articles/115000075750-Converting-Cubase-VST-songs-ALL-ARR-into-CPR-format
8. Cubase Pro/Artist operation manuals, project/Track Archive/DAWproject export sections: https://www.steinberg.help/

### Source code / open formats

9. SX 2/3 project file reverse engineering: https://github.com/omeriko9/Cubase-Project-File-Reverse-Engineering
10. Cubase session-state explorer (including CPR scanner and fixture protocol): https://github.com/ColonelKernel/session-state-explorer-cubase
11. Fixture design: https://github.com/ColonelKernel/session-state-explorer-cubase/blob/main/docs/CUBASE_FIXTURE_PROTOCOL.md
12. TestDisk `file_riff.c`, CPR recognition and byte order: https://git.cgsecurity.org/cgit/testdisk/tree/src/file_riff.c
13. DAWproject format and schema: https://github.com/bitwig/dawproject
14. Swift Cubase Track Archive partial parser: https://github.com/orchetect/swift-daw-file-tools
15. DAWproject-associated converters: https://github.com/SatyrDiamond/DawVert and https://github.com/git-moss/ProjectConverter
16. Narrow requested CPR feature scope: https://github.com/ozzaii/bravoh-daw/issues/3

### Discussion/field reports (less authoritative)

17. Historical project conversion and compatibility: https://forums.steinberg.net/t/older-cubase-files/810097
18. Community conversions and limitations: https://forums.steinberg.net/t/how-to-convert-old-cubase-files-arr-and-all-to-cpr-format/689460
19. Compressed Kontakt state inside Track Archive `audioComponent`: https://forums.steinberg.net/t/track-archive-xml-anyone-can-decrypt-where-the-kontakt-instrument-nki-name-hide-in-the-xml/1034306
20. Track Archive specification discussion: https://forums.steinberg.net/t/steinberg-cubase-track-archive-xml-specification/658214

## 17. Final strategic recommendation

Prioritize **a trustworthy native CPR parser with measured partial coverage**, not a misleading “complete reader” that extracts string matches and calls them tracks. Implement the fully specified DAWproject and XML readers in parallel as *test instruments*. Use controlled CPR differential fixtures to uncover one semantic component at a time; preserve unknown regions to make future support additive. The biggest remaining dependency is a cross-version corpus of real Cubase-generated projects, especially around the 13.0.30 file-size change.

---

# Revision 2 — Additional deep research, public forums and source-code audit

**Additional research performed:** 2026-10-30. New primary emphasis: actual public source files, legacy laboratory notes, and forum cases with technical artifacts. This is an ADDENDUM to the baseline sections, not an assertion that all Cubase versions use one serialization grammar.

## 18. Source-quality inventory: what was actually found

| ID | Evidence and access | Version scope | What can be used immediately | Limits and disposition |
|---|---|---|---|---|
| R2-01 | [`schwifty00/CubaseTools`](https://github.com/schwifty00/CubaseTools), public, **MIT** | Author claims Cubase 10–15 | Implemented Python metadata, mixer, plug-in, routing, track, marker and experimental MIDI extractors; detailed format notes | Heuristics, proximity assignment, synthetic tests; **port selectively and verify each field** |
| R2-02 | [CubaseTools format reference](https://github.com/schwifty00/CubaseTools/blob/master/docs/cpr_format.md), public | Author's cross-version observations | Concrete candidate named-field types and binary byte patterns | Third-party reverse-engineering observations, not Steinberg specification |
| R2-03 | [CubaseTools extractor implementation](https://github.com/schwifty00/CubaseTools/tree/master/cubasetools/core/extractors), public | 10–15 claimed | Functional extractor boundaries; metadata, tracks, mixer, media, plugins | All-file regex, fixed offsets, duplicate-name merges risk false positives |
| R2-04 | [CubaseTools tests](https://github.com/schwifty00/CubaseTools/tree/master/tests), public | Mostly synthetic snippets | Reproducible unit-test patterns for encoding | Synthetic bytes intentionally omit real CPR envelope: passing them **does not prove native compatibility** |
| R2-05 | [Andi's sample-precise marker script](https://gist.github.com/andigamesandmusic/94be52eff000013d7cdf5daef54bc4aa), public | Unknown Cubase/Nuendo subset | `MMarkerTrackEvent`, `AudioSampleRate`, `Float`, big-endian doubles; two timebases | Marker walking has hard-coded sentinels/offsets, requires cross-version fixtures |
| R2-06 | [omeriko9's SX2 research notebook](https://github.com/omeriko9/Cubase-Project-File-Reverse-Engineering/blob/master/My%20thought%20process.txt), public | SX 2.0.2, some SX3 | `ARCH`, object names, explicit size fields, reference tags, `0xFFFFFFFF` and `0xFFFFFFFE` sentinels | Research notes include unverified hypotheses; do **not** extrapolate to Cubase 10+ |
| R2-07 | [Steinberg forum, Oct 30 2026](https://forums.steinberg.net/t/project-inspector-cross-platform-vibe-coded/1044606), public | Reported Pro 11–15 | Developer says they documented format using 138 files and made a first-pass reader | Author has **not published** the claimed documentation or source as of research date |
| R2-08 | [CPR Inspector product page](https://courses.domsigalas.com/cpr-inspector), public product claims; proprietary paid app | Page claims Cubase 12–16 | Capability target: tracks, plugs, sends, sidechains, I/O, missing plugins, comparisons, previews/reports | **No parser source or algorithm available**; not a format specification or free reusable code |
| R2-09 | [Atari2Cubase public forum release](https://forums.steinberg.net/t/free-tool-open-your-atari-st-cubase-songs-arr-all-in-cubase-nuendo-14/1043292) | Atari ST Cubase 2/3 and variants; early PC gradually supported | Real `.ALL/.ARR/.PRT/.SNG` → `.dawproject`/MIDI conversion oracle | Converter's binary implementation is not published; do not claim standalone reader source exists |
| R2-10 | [VST3 SDK persistence reference](https://steinbergmedia.github.io/vst3_dev_portal/pages/FAQ/Persistence.html), Steinberg primary source | VST3 | Distinguish `audioComponent` (processor) and `editController` state conceptually | Does not specify Cubase's CPR wrapper or an arbitrary plug-in's blob grammar |
| R2-11 | [Steinberg forums: Kontakt XML binary analysis](https://forums.steinberg.net/t/track-archive-xml-anyone-can-decrypt-where-the-kontakt-instrument-nki-name-hide-in-the-xml/1034306) | Kontakt 8 example, 2026 | Real instance of proprietary component-state compression and why simple string-scanning fails | Specific to inspected plug-in/state, not a generic Cubase compression rule |
| R2-12 | [PluginInspector thread](https://forums.steinberg.net/t/plugininspector-for-cubase-and-nuendo-projects-an-xlsx-report-generator-of-used-plugins/846415) | Cubase/Nuendo plug-in registry | Local VST2/3 plug-in cache XML files as an **independent matching database** | Registry is installation-local, may be stale, and does not substitute for CPR parsing |

### Evidence confidence rubric (apply to each *field*, not just each parser)

- **A: independently reproducible** — exact byte range and type; 3+ genuine Cubase-authored contrasting fixtures; oracle comparisons; correct positive/negative cases; version scoped.
- **B: source-level candidate** — public program has a concrete field decoder but no independent real project validation in this study.
- **C: pattern observation** — a string/sentinel/offset found in a code example or historical notebook; no established ownership/parent relationship.
- **D: inference** — plausible data interpretation, unsupported by direct bytes.
- **N/A: unavailable** — proprietary or private material not accessed.

**The new raw field patterns below are presently B or C, not A.** Never promote a field to A merely because the original project's README describes it as “verified”.

## 19. New breakthrough: modern CPR named-field grammar (CubaseTools, third-party)

Source: [`docs/cpr_format.md`](https://github.com/schwifty00/CubaseTools/blob/master/docs/cpr_format.md), implementation in [`cpr_parser.py`](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/cpr_parser.py) and extractor modules. The following describes the *author's interpretation*, not a complete general grammar:

```text
Candidate named field:
  UTF-8 / ASCII name + NUL     e.g. "Volume\0", "SampleRate\0"
  type tag, 2 bytes big-endian
  value, dependent on type

Reported tag map:
  0x0001  signed integer, often 8-byte BE int64
  0x0002  compound, followed by flags and child count
  0x0004  8-byte BE IEEE754 double
  0x0008  length-prefixed text, often BE uint32 length

Reported compound child entry:
  uint32 BE name_length
  name bytes (often including terminal NUL)
  uint16 BE type_tag
  typed value
```

**Scope caveat:** The original SX project notebook also shows *length-prefixed object names*, explicit section sizes and *compact references* to previously declared names. Consequently, not every occurrence of a name + `0x0004` + eight bytes is a unique semantic node; a parser needs container/object boundaries and a bounded field grammar. Mixing old SX object headers with the modern named-field grammar without format-family detection is unjustified.

### 19.1 Reported named-field and project objects

| Named field or marker | Candidate semantic meaning | Reported raw encoding / locator | Confidence / warning |
|---|---|---|---|
| `Version X.Y.Z` | Last saved Cubase build | NUL-terminated text in the first ~2 KB on modern projects | B. Does **not** establish original authoring version |
| `Cubase 10`, `Cubase 11` | Legacy build family | Plain ASCII prefix | B; confirm manufacturer/product metadata separately |
| `SampleRate` → `Float` | Project sample rate | `Float\0 00 04` + BE double in a binary compound | B. Plugin XML can also contain `SampleRate` |
| `AudioSampleRate` → `Float` | Alternative sample rate locator | Independent marker gist uses `Float+8` → BE double | B; **do not** assume equivalence for every build |
| `MTempoEvent` → `BPM` | Initial/fixed tempo | `BPM\0 00 04` + BE double | B; do not take `PGridDefinition/Tempo` or `RehearsalTempo` |
| `MTempoTrackEvent` | Tempo track configuration | Object header; various internal bytes and tempo structures | C; naive search for first double in range 20–400 is unsafe |
| `TimeSignatureEvent` | Time signature | `Numerator`/`Denominator`, tag `0x0001` BE int64 | B; multiple events possible |
| `UColorSet` / `Color 16` | Color palette | `Color 16\0 EF BB BF` + 4-byte ARGB reported | B; palette index mapping to track is not solved for C12+ |
| `IDString` | Channel engine classification | nearby text (`Audio`, `GroupChannel`, `FxChannel`, `Synth`...) | B; proximity is not a stable entity association |
| `Volume` / `AnchorValue` | Fader value | complex field with `Value` and `AnchorValue` BE doubles | B; applies to specific channel context |
| `SummingMode` / `Standard Panner` | Main channel panner context | plugin state nested inside main Panner after SummingMode | B; many insert-slot panners also exist |
| `OwnInputBus`, `Bus UID` | Bus ID registry | UID candidate stored in BE bytes | B; confirm object identity and version scope |
| `OutputBus` / `Value` | Destination bus link | candidate 4-byte BE UID | B; avoid nearest-by-position joins |
| `SendFolder` | Sends collection | send Volume + Output UID in a bounded region | B; send level curve unverified |
| `MMarkerEvent`, `MRangeMarkerEvent` | Marker/range | marker names and tick-like position candidates | B/C; marker gist has separate implementation |
| `PresetChunkXMLTree` | Vendor-specific plug-in state | readable XML fragment in some plug-in chunks | B; cannot generalize to opaque binary plug-ins |
| `MMidiPartEvent` / `adcn` | MIDI part/note record candidate | experimental `adcn` note signature in current code | C; README contradicts code about MIDI support, tests synthetic |
| `MAutomationTrackEvent` / `VffO`, `TDRH` | Automation points candidate | experimental double parsing | C; tests synthetic, parameter domain may be non-normalized |
| `Project Preview` | Embedded audio preview, Cubase 15 | Steinberg forum reports preview saved in `.cpr` | Reported behavior confirmed; **binary audio offset/codec unknown** |

### 19.2 Typed C# reader: safe building block, not yet a general CPR deserializer

```csharp
using System.Buffers.Binary;
using System.Text;

public ref struct CprBoundedCursor
{
    private readonly ReadOnlySpan<byte> _data;
    private int _offset;
    public int Offset => _offset;
    public int Remaining => _data.Length - _offset;

    public CprBoundedCursor(ReadOnlySpan<byte> bytes)
    {
        _data = bytes;
        _offset = 0;
    }

    private ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || length > Remaining)
            throw new InvalidDataException("Truncated or out-of-range CPR field.");
        var value = _data.Slice(_offset, length);
        _offset += length;
        return value;
    }

    public ushort U16BE() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
    public uint U32BE() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
    public ulong U64BE() => BinaryPrimitives.ReadUInt64BigEndian(Take(8));
    public long I64BE() => unchecked((long)U64BE());
    public double F64BE() => BitConverter.Int64BitsToDouble(I64BE());
    public float F32LE() => BitConverter.Int32BitsToSingle(
        BinaryPrimitives.ReadInt32LittleEndian(Take(4)));

    public string LengthPrefixedUtf8(int maxBytes = 4096)
    {
        uint size = U32BE();
        if (size > maxBytes || size > Remaining)
            throw new InvalidDataException("Invalid CPR string length.");
        var bytes = Take((int)size);
        if (!bytes.IsEmpty && bytes[^1] == 0) bytes = bytes[..^1];
        return Encoding.UTF8.GetString(bytes);
    }
}
```

This cursor must be instantiated over a **proven, bounded substructure**. The parser must determine whether string lengths include the NUL, whether a `name_length` itself is valid, and how a 64-bit project stores section sizes. Numeric casts, negative positions, resource limits and recursion depth require explicit guards. The code intentionally contains no fake `ParseProject()` implementation.

### 19.3 Cross-endian trap

The general envelope's TestDisk length is interpreted **big-endian** for `NUND`. Named integer/double fields in CubaseTools are also frequently **big-endian**. But an `audioComponent` blob can contain **little-endian floats** (one observed Standard Panner implementation), while VST plug-in state can define its own endianness. Therefore: **endianness is scoped to a field codec, not to the entire CPR file**.

## 20. Modern metadata and transport: what to extract first

Extract *candidate* values into an evidence structure with byte offsets, not into final defaults:

```json
{
  "field": "Project.SampleRate",
  "value": 48000.0,
  "unit": "Hz",
  "source": "native-cpr",
  "encoding": "float64-be",
  "offset": 123456,
  "objectPath": null,
  "status": "candidate",
  "parserRule": "SampleRate/Float-be-v1",
  "versionEvidence": "Cubase 15.0.x",
  "validationFixture": null
}
```

**Illustrative JSON only.** The number and byte offset above are examples, not a real decoded file. Keep separate `DetectedLastSavedVersion`, `OriginalCreationVersion` (normally unknown), `ProjectFormatVariant`, `Edition`, and `Provenance`. Cubase/MediaBay forum participants note that last saved version is not the original version if a project is resaved in newer Cubase [R2-13](https://forums.steinberg.net/t/cubase-version-cpr-file/591395).

- **Sample rate:** locate all structured `SampleRate`/`AudioSampleRate` occurrences; test whether their surrounding named-field shape matches; evaluate 44.1/48/88.2/96/176.4/192 kHz without prematurely restricting other rates; reject XML attribute lookalikes.
- **Tempo:** prefer `MTempoEvent/BPM` within a validated parent; report an array of TempoEvents and independent fixed-tempo flag. A hard-coded 120 BPM fallback is a *Cubase application assumption*, not evidence from the file; use status `default-assumed`, never `decoded`.
- **Signature:** do not return only initial 4/4. Preserve all `TimeSignatureEvent` occurrences with bars/positions and validate the tempo-map-independent interpretation.
- **Transport/locators:** inspect named fields and controlled save pairs; avoid presenting a VST parameter labelled `Cycle` as transport state.
- **Record bit depth:** `SampleSize` appears as a candidate named field in CubaseTools' current `metadata.py`, but its interpretation is not fully established in the format doc. Mark it independently.
- **Identity:** exact version/build, last modification, project name and creator can be duplicated inside nested state. Prefer validated top-level metadata and report discrepancies.

### 20.1 Track classes observed in modern CPR source

CubaseTools reports these `IDString` values: `Audio`, `GroupChannel`, `FxChannel`, `SamplerChannel`, `Synth`, `MidiChannel`, `InputChannel`, `OutputChannel`. Its fallback searches `MAudioTrackEvent`, `MInstrumentTrackEvent`, `MMidiTrackEvent`, `MFXChannelTrackEvent`, `MGroupChannelTrackEvent`, `MVCATrackEvent`, `MMixerTrackEvent`, `MFolderTrackEvent`, `MSamplerTrackEvent`.

**Important:** `MFolderTrack` and `MFolderTrackEvent` are not interchangeable without evidence. In older sources `MFolderTrack` occurs as an actual container object. Build a version-aware object taxonomy with aliases *only after fixture validation*.

### 20.2 Track ownership and duplicates (critical correction to existing third-party code)

The source of `CubaseTools` merges all tracks with the same **name**, and uses byte distance to match `IDString` and plug-ins to tracks. Both operations can be wrong in real sessions:

1. Cubase permits two or more tracks with identical display names.
2. A track can contain a large sample-library plug-in state extending beyond a 40 KB/200 KB search window.
3. Mixer processors, track-editor representations and hardware I/O sections can contain duplicate names and objects for legitimate reasons.
4. `IDString` nearest-to-previous-strip can attach a signal component to the wrong track if the serialization graph is non-contiguous.
5. A track having MIDI or being audio is **not evidence** that its part contains notes/audio; the current parser marks some tracks as `has_content` simply by type.

**Required upgrade:** identify **stable saved entity IDs and parent/child references** from genuine CPR object nodes. Preserve tracks as `(objectId, parent, kind, displayName)`, not a dictionary keyed by `displayName`. Define stable identity for cross-file comparisons using saved GUID/UID whenever found, with deterministic structural fallback and an `identityConfidence` flag.


## 21. SX 2/3 object graph: independent legacy investigation

Primary notebook: [omeriko9: My thought process](https://github.com/omeriko9/Cubase-Project-File-Reverse-Engineering/blob/master/My%20thought%20process.txt). It is a particularly useful *research log*, but its proposed object-reference arithmetic is not a stable published specification. Target an independent **SX compatibility decoder**, never a collection of unconditional offsets added to the modern parser.

### 21.1 Candidate structural vocabulary

| Token | Candidate role (investigate, do not assume) |
|---|---|
| `ARCH` | Serialized archive / top-level context |
| `PArrangement` | Arrangement root |
| `MRoot`, `MGroupEvent`, `MPartEvent`, `MEvent` | Event-container/object graph |
| `MTrackList`, `MTrackEvent`, `MListNode`, `MDataNode` | Tracks, list nodes and data references |
| `MMidiTrack`, `MMidiTrackEvent`, `PMidiChannel` | MIDI track, serialization event, MIDI channel |
| `MAudioTrackEvent`, `PAudioClip` | Audio track and clip nodes |
| `MTempoTrackEvent`, `MSignatureTrackEvent` | Musical timing nodes |
| `MFolderTrack` | Folder track/container |
| `PMixerChannel`, `PDevice`, `PDrumMapPool`, `MDrumMap` | Mixer, device, drum map resources |
| `MMidiQuantize`, `MGridQuantize` | Quantization state |

**Observed in the original investigation**: big-endian 32-bit size-like fields, marker words `FF FF FF FF` and `FF FF FF FE`, and some name references with the high bit set (`0x80……`). The notebook conjectures that a reference ending in `0x225` can resolve to a prior name located at byte offset `0x268`, i.e., delta `0x43` in that specific fixture. **Do not implement a universal `pointer+0x43` rule.** Test at least two genuine SX2 and SX3 files, plus a modified copy of each, and map the scope of the relevant base offset. Repeated string names do not establish that these values are file offsets.

### 21.2 Legacy parsing experiments

1. Recover the full `ARCH` header, including offsets, bounds, endian interpretation and object-count semantics, with *independent valid projects*.
2. Distinguish object declarations from backreferences, and resolve names only within the proven scope of their symbol table.
3. Reconstruct a typed ordered object graph including unknown objects; preserve serialized occurrence order and raw spans.
4. Establish the `MTrackList` count and traversal; cross-check two identically named tracks and a folder with nested tracks.
5. Compare a MIDI part containing one note at C-2 and one at C4, plus a pitch bend and CC64 change, against Cubase’s Standard MIDI File export.
6. Establish which historical `.cpr` variants actually carry `RIFF`/`NUND`; never infer `ARCH` from the file extension alone.

### 21.3 `.all`, `.arr`, and Atari-era formats are not SX CPR

A contemporaneous [Steinberg forum announcement](https://forums.steinberg.net/t/free-tool-open-your-atari-st-cubase-songs-arr-all-in-cubase-nuendo-14/1043292) describes an in-browser migration tool supporting Atari-era Cubase 2/3, Pro 24 and variations, legacy PC `.ALL`/`.ARR` in an evolving compatibility matrix, and exporting MIDI and DAWproject. The announcement is evidence that a conversion path exists; it is **not** a published binary format reference or licensed converter source. Capture its *exact supported editions/builds*, especially differences among `.ALL`, `.ARR`, `.PRT`, `.SNG`, disk-image containers and MIDI-only variations.

Older Steinberg user discussions describe intermediate opening/resave steps with old Cubase VST or SX3 versions for some `.ALL` files; this is operational evidence of compatibility gaps, not proof that SX3 can natively read every historical `.ALL`: [conversion discussion](https://forums.steinberg.net/t/unable-to-import-all-songs-to-cubase-sx3-midi-import-works/152499) and [Atari-to-CPR thread](https://forums.steinberg.net/t/looking-for-a-solution-to-convert-atari-all-files-to-cpr-via-sx3-on-a-mac/973935).

**Strategy:** define `ILegacyProjectReader` separately and create a format-specific manifest that distinguishes recognition, metadata, MIDI content, audio references and mixer reconstruction. Where an authenticated output conversion is available, its export is an *oracle*, not byte-for-byte proof of an internal `.all` representation.

## 22. Marker records: two useful public approaches, one unresolved timebase

Sources: [CubaseMarkers.js](https://gist.github.com/andigamesandmusic/94be52eff000013d7cdf5daef54bc4aa), [CubaseTools `media.py`](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/media.py).

The marker gist searches `MMarkerTrackEvent`, `MMarkerEvent` / `MRangeMarkerEvent`, reads BE-encoded numbers, and recognizes separate **musical and linear timebases**. Its code interprets a byte near the marker name (relative offset reported as `nameOffset-24`) as a mode (`0x40` versus `0x41`), converts some musical positions using a reported divisor of 480, and converts linear-time positions into sample positions using the project sample rate. By contrast, CubaseTools scans named `MRangeMarkerEvent` and treats a fixed 32-bit value after its BOM-delimited name as a PPQ tick offset; it is much simpler and may not preserve real-time positions or all marker types.

Recommended internal representation:

```csharp
public enum TimelineDomain { Unknown, MusicalTicks, Seconds, AudioSamples, SMPTE }
public sealed record TimePoint(
    TimelineDomain Domain,
    decimal RawValue,
    long? MusicalTicks,
    long? Samples,
    decimal? Seconds,
    string EvidenceId);
public sealed record ProjectMarker(
    long? SavedId,
    string Name,
    TimePoint Start,
    TimePoint? End,
    bool IsRange,
    string? Description);
```

**Why keep multiple units?** A marker at tick 1920 is not generally a fixed number of seconds: tempo ramps, time-signature mapping and song start offsets change the conversion. A marker stored in samples is not inherently quantized to 480 PPQ. Keep source units unchanged, then convert using independently decoded tempo events and the exact sample rate. If the timebase is not proven, return an unknown raw value rather than calling it `ticks`.

Suggested fixture contrasts: fixed 120 BPM vs 90 BPM, marker anchored to musical bar vs absolute time, 44.1 vs 48 kHz, standard marker vs cycle marker, nonzero project offset, marker at project end, marker names with German umlauts/emoji, and projects with no marker track. Compare both the position seen in Cubase and the raw stored marker timebase.

**Additional Cubase 15 development:** users report [project preview data stored in CPR when generated/saved](https://forums.steinberg.net/t/cubase-15pro-project-previews-and-test-signal-not-playing-in-hub/1013479?page=2). The embedded representation/codec is unknown; treat this as a prospective opaque resource with bounds, not a known PCM/WAV segment.

## 23. MIDI decoding: promising byte layout, substantial validation deficit

Public implementation: [CubaseTools media extractor, `_extract_midi`](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/media.py); source-specific tests: [test_midi_automation.py](https://github.com/schwifty00/CubaseTools/blob/master/tests/test_midi_automation.py).

A reported candidate note record follows `MMidiPartEvent` and the signature `adcn\0\x01`. Its Python reader treats:

```text
adcn 00 01             candidate record discriminator
[0..3]  BE uint32      reported record-size code, e.g. 0x20
[4..7]  BE uint32      candidate pitch, sometimes only last byte
[8..15] BE float64     note length (reported in musical ticks)
[16..33]              reserved / other data
[34]                  alternate candidate pitch
[35]                  candidate event status, sometimes 0x90
[36..43] BE float64    note start
[44]                  padding / field
[45]                  note-on velocity
[46]                  note-off velocity
```

*Offsets above are relative to the six-byte `adcn\0\x01` marker and follow the source’s 8-byte capture + 37-byte block. Layout requires validation on real versioned fixtures.* The Python code dynamically selects one of two pitch offsets based on whether one set consists only of zeroes. It assigns a MIDI part to the nearest preceding track marker in file order and reads a fixed 50 KB region. Both may mis-assign data on real large VST instruments or high-note-count parts.

**Critical source-audit details:**

- Source tests synthesize byte arrays and do not contain an actual full `.cpr` header or real saves. Some assertions are conditional (`if piano_tracks and ...`) and may therefore pass even if no MIDI event is parsed. This is not sufficient proof of Cubase-version compatibility.
- The test deliberately rejects pitch **0** as a sentinel; MIDI pitch 0 is **valid** (C-1 by common MIDI octave labelling). A production reader must not silently drop it without a proven semantic reason.
- Acceptable MIDI velocity range includes `0` in some contexts (notably note-off semantics); reject invalid events using the event grammar, not generic `velocity > 0` assumptions.
- Preserve note-on/off velocity, channel, expression/per-note controllers, multiple takes, muted notes, nested containers, event offsets, duration and output routing where available.
- Treat note positions and durations as typed rational/tick values with unknown or known PPQ, rather than rounding to two decimal places as the source currently does.

**Validation oracle:** Export the *same* Cubase part to Standard MIDI File (`.mid`) from the DAW. Compare note count, pitch 0/60/127, overlap, velocity, duration, sustain CC64, modulation CC1, pitch bend, aftertouch, program change, system metadata, multi-channel and drum notes, loops and non-zero part starts. A zero-diff test must first normalize legitimate format differences (tick base, part offset, explicit note-off), not mask missing notes.

### 23.1 Automation pattern and scope

`CubaseTools` experimentally detects `MAutomationTrackEvent` and scans for `TDRH\0\x04` (BE float64 interpreted as time) near `VffO\0\x04` (BE float64 interpreted as a normalized value). Its reader currently accepts only values in `[0,1]`. This may work for some parameter domains but may *exclude* signed, absolute, logarithmic or multirange automation. The source does not show a proven general parameter ID lookup, event graph parent reference, interpolation encoding or curved segments.

Require fixture families for volume/pan/plug-in parameter, 0/50/100%, stepped values, negative pan, non-linear ramps, discrete mute, touch/latch modes, interpolation variants, and point IDs. Capture saved parent ownership and stable parameter identity; avoid attaching a lane to a track by byte proximity alone.

**Never** conflate `VffO` with an arbitrary plug-in’s own automation metadata. Only upgrade confidence after locating its object graph parent and validating a controlled saved change.

## 24. Mixer, routing, plug-ins and colors: reusable patterns with strict boundaries

Source: [CubaseTools format doc](https://github.com/schwifty00/CubaseTools/blob/master/docs/cpr_format.md), [mixer.py](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/mixer.py), [tracks.py](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/tracks.py).

| Feature | Reported candidate | Robust-reader requirement |
|---|---|---|
| Main fader | `Volume` compound with `Value` BE double and `AnchorValue` BE double; source says `AnchorValue` is display dB | Verify correct *main fader* object vs send level, trim, plug-in parameter, mix automation; store raw and display units separately |
| Fader unity | Raw ~`25856.0` (source observation) | Nonlinear transfer is not established; **do not** calculate dB via `20log10(raw/25856)` as a universal map |
| Main pan | `SummingMode` → `Panner` → `Standard Panner` → `audioComponent`; one LE float 0..1 | Distinguish actual channel panner from insert processors, sidechains and output panners; support stereo width and pan law when proven |
| Sends | `SendFolder` plus per-slot `Volume`/`Output` | Decode slot index, active/pre-post/bypass and UID; preserve silent/unconnected sends, do not cap at eight without version evidence |
| Output routing | `OutputBus` → numeric UID; bus name in `OwnInputBus` / `Bus UID` | Resolve by saved identity, allow unresolved UID, avoid name match collisions; route source-to-destination graph |
| Plug-in inserts | `Plugin Name`, `Slot`, `Bay Program`, serialized component state | Detect exact slot/track owner; plug-in vendor, UID, state and disabled/bypass are separate fields |
| Color palette | `UColorSet`, `Color 16` + UTF-8 BOM and 4-byte ARGB | Palette is not the mapping; per-track index on Cubase 12+ is not established |
| Mute | Legacy `Mute` integer may occur inside other nested states | Newer mute-button persistence is unresolved; do not infer from any arbitrary `Mute` field |
| Solo | Source authors did not observe saved solo state on tested files | Avoid universal assertion that solo is never saved; validate version and save behavior |
| Monitor / record enable | Possible named fields near channel or track | Need real save-delta and parent ownership, not prefix scan |

**Source-code inconsistency to record:** CubaseTools’ written description cites a 1 MB I/O-boundary gap, while the current `constants.py` contains `IO_GAP_THRESHOLD = 5_000_000`. A byte-distance heuristic is not a stable semantic boundary, regardless of which literal is used. An instrument plug-in chunk can exceed either threshold. This is why native node parentage and stable UIDs must eventually replace strip-location clustering.

**False-positive safety:** track names can repeat; the same plug-in may appear multiple times in one track; sends can be disabled or pre-fader; routing can be complex (VCA, multi-outs, side-chain buses, groups). Store all edges and raw IDs with decoder evidence even when friendly names are unresolved. No global `DistinctBy(Name)` or hidden deduplication by fixed byte radius.

## 25. Plug-in persistence, Track Archive XML, and local plug-in registries

The [Steinberg VST3 persistence FAQ](https://steinbergmedia.github.io/vst3_dev_portal/pages/FAQ/Persistence.html) documents the distinction between a processor component’s and edit controller’s saved state. Cubase may wrap these in serialized slots/components, but vendor-provided state bytes remain an **opaque vendor-specific payload** unless a public plug-in state format is known. A complete CPR *project structure* reader does **not** require reverse engineering every vendor preset; it requires identifying owner, unique plug-in ID, slot, bypass/enable state and preserving the byte array or safe reference to it.

A concrete [Steinberg forum Track Archive example involving Kontakt](https://forums.steinberg.net/t/track-archive-xml-anyone-can-decrypt-where-the-kontakt-instrument-nki-name-hide-in-the-xml/1034306) has an `<bin name="audioComponent">` element containing a large binary state; extracting the loaded `.nki` name was not straightforward, apparently because data is compressed or encoded within vendor state. **Do not** assume universal zlib compression, named Kontakt fields, or plain-text sample paths. Another forum conversation shows [Track Archive XML binary payloads can be represented as hex text](https://forums.steinberg.net/t/any-coders-here-by-any-chance/918444?page=2), so XML binary parsing must select base64/hex according to the actual node representation, not blindly `Convert.FromBase64String`.

A separate [PluginInspector forum thread](https://forums.steinberg.net/t/plugininspector-for-cubase-and-nuendo-projects-an-xlsx-report-generator-of-used-plugins/846415) notes local metadata caches such as `Cubase Pro VST3 Cache/vst3plugins.xml`, `Vst2xPlugin Infos Cubase.xml`, and Waves plug-in registry files. These can enrich UID/name/vendor matching if the user opts in to local filesystem discovery. They cannot prove a binary chunk belongs to a track and may be out-of-date.

Recommended plug-in model:

```csharp
public sealed record PluginInstanceInfo(
    string OwnerEntityId,
    string SlotKind,
    int SlotIndex,
    string? DisplayName,
    string? Vendor,
    string? Vst2Id,
    Guid? Vst3ClassId,
    bool? IsBypassed,
    string? StateBlobId,
    string? ControllerBlobId,
    string EvidenceId);
```

Opaque states should be addressed by content hash + verified `(offset, length)` rather than loaded into memory for every scan. Do not execute a VST plug-in, load foreign DLLs, or interpret unknown compressed content as trusted instructions. Full plug-in *parameter interpretation* belongs to an opt-in, plugin-specific extension system.

## 26. DAWproject and Track Archive XML as imperfect validation oracles

The [DAWproject specification](https://github.com/bitwig/dawproject) is valuable because it is an open session interchange format (DAWproject containers use ZIP and XML structures). But it is **not** a lossless representation of every native Cubase feature; a CPR reader must not be validated exclusively against it.

| Forum-reported limitation / example | Why important to CPR testing | Independent oracle |
|---|---|---|
| [Cubase 15 Pro DAWproject export reportedly loses CC64 sustain events](https://forums.steinberg.net/t/issue-dawproject-export-missing-midi-cc64-sustain-events-cubase-15-pro/1023436) | Missing CC in exported DAWproject **does not** establish missing CC in CPR | Cubase MIDI List Editor and exported `.mid` |
| [Cubase 14 export MIDI CC discussion](https://forums.steinberg.net/t/cubase-14-dawproject/948676) | Version-specific MIDI-controller fidelity is contested / limited | `.mid` + direct UI observations |
| [Nuendo/Cubase DAWproject MIDI track omission](https://forums.steinberg.net/t/dawproject-export-no-midi-track-export/1009070) | Native MIDI tracks can be absent or represented differently | Native project screenshot / MIDI export |
| [Cubase 15.0.10 clip gain missing from DAWproject](https://forums.steinberg.net/t/cubase-dawproject/1024306) | A null gain in interchange could be export loss | Audio event UI + control CPR delta |
| [Studio One ↔ Cubase DAWproject import discrepancies](https://forums.steinberg.net/t/dawproject-import-from-studio-one-unusable/953751) | Round-trip mismatches may be *import/export* bugs | Same-version direct CPR save/reopen |

**Oracle ranking for each field:** (1) controlled GUI value when project saved; (2) Cubase’s native exports specific to that field; (3) Track Archive XML for specific track/plugin metadata; (4) DAWproject when the known implementation supports the field; (5) third-party tool outputs as secondary comparison. Every oracle has `softwareVersion`, `exportOptions`, `fileHash`, `timestamp` and known-loss notes. A verified difference in a field lacking native export is especially valuable; it should become a repro fixture and a tracked parser hypothesis.


## 27. CPR/NPR format identity and the 2 GB transition (Steinberg confirmation)

- Steinberg's [Cubase 15 manual](https://www.steinberg.help/r/cubase-pro/15.0/en/cubase_nuendo/topics/project_handling/project_handling_about_project_files_c.html) says `.cpr` is the main project document containing references to media. The `.cpr` alone is not guaranteed to contain the actual referenced audio, offline edits, waveforms, track pictures, video or external sample libraries. The reader should build a **project dependency graph** of references relative to the project directory and original absolute paths, reporting missing media without modifying files.
- Steinberg's [Nuendo 15 manual](https://www.steinberg.help/r/nuendo/15.0/en/cubase_nuendo/topics/project_handling/project_handling_about_project_files_c.html) describes `.npr` as its project format. Nuendo/Cubase compatibility is application/edition dependent: a [forum thread](https://forums.steinberg.net/t/cubase-projects-from-nuendo/665258/2) says Cubase **Pro** can open Nuendo `.npr` (unlike Artist/Elements), and a [Nuendo 13 thread](https://forums.steinberg.net/t/saving-as-nuendo-project-file/725368) describes projects retaining the `.cpr` extension when opened and saved in Nuendo. **Therefore do not use file extension as a format-version or manufacturer ground truth.**
- In a [February 2024 public warning](https://forums.steinberg.net/t/a-critical-announcement-about-npr-files-larger-than-2gb-getting-corrupted/895881), Steinberg reported potentially corrupted projects once the project size exceeded 2 GB, particularly when plug-ins embedded substantial WAV/XML/ARA data.
- In a [March 22, 2024 Steinberg engineer update](https://forums.steinberg.net/t/a-critical-announcement-about-cpr-files-larger-than-2gb-getting-corrupted/895880/77), **Cubase 13.0.30 introduced a new format for over-2-GB projects** while still using `.cpr`; older Cubase builds cannot open that new variant. This was a *conditional transition*, not proof that every Cubase 13.0.30 file has a new container layout. Steinberg's 15 manuals say files over 2 GB are supported beginning with 13.0.30. **A new reader must identify variant by binary evidence and length, not infer it solely from the version string.**

### Practical 64-bit / large-file safeguards

```text
Reader constraints:
  - Native file offsets and lengths: unsigned 64-bit / checked arithmetic.
  - Explicit check: payload_offset <= file_length; payload_length <= file_length - payload_offset.
  - Do not cast a 4-byte declared size to signed Int32, then seek backward on overflow.
  - If a 32-bit header size conflicts with a >2 GB physical file, select a
    verified large-project variant or return UnsupportedVariant. Never truncate.
  - Use FileStream.RandomAccess / memory-mapped windows or bounded streaming,
    not File.ReadAllBytes for every multi-GB CPR.
  - Detection: signature + candidate container profile + bounded top-level scan.
  - Version/build is informational, not the branch selector on its own.
```

The existing TestDisk recognition code reports a size calculation for its recognized `RIFF`/`NUND` variant using **big-endian**, contrary to familiar little-endian RIFF; the size rule is still subject to the file variant and should be confirmed against a corpus. Avoid simply importing a WAV RIFF parser and trusting its chunk traversal.

## 28. Recommended architecture for CubaseProjectExplorer (.NET 10)

Goal: **genuine native CPR import**, with deterministic analysis and explicit uncertainty. XML/DAWproject/SMF are supporting decoders and test oracles, not replacements.

```text
CubaseProjectExplorer.Reader
  Detection/
    SignatureProbe.cs           .cpr / .npr / .all / .arr / .dawproject / .xml
    ContainerVariant.cs         modern-old / modern-large / SX2/3 / legacy
  Storage/
    RandomAccessCprSource.cs    ReadOnlyMemory windows, bounded 64-bit seeks
    Slice.cs                    offset, length, source file SHA-256
    StringTable.cs              encoding + NUL/symbol-table contexts
  Parse/
    CprContainerReader.cs       validate envelope, chunk boundaries
    CprFieldReader.cs           typed BE fields, compound flags/counts
    CprObjectGraphReader.cs     classes, parent/child, stable identities
    SxArchiveReader.cs          historical ARCH/object-reference grammar
    LegacyAllReader.cs          separate, progressively supported format
  Semantics/
    MetadataDecoder.cs
    TrackDecoder.cs
    MixerDecoder.cs
    RoutingDecoder.cs
    TempoSignatureDecoder.cs
    MidiDecoder.cs
    AudioPoolDecoder.cs
    PluginStateDecoder.cs
    MarkerDecoder.cs
    AutomationDecoder.cs
  Exchange/
    TrackArchiveXmlReader.cs
    DawprojectReader.cs
    StandardMidiFileReader.cs
  Validation/
    EvidenceRecord.cs
    FieldConfidence.cs
    NativeExportComparer.cs
    DifferentialFixtureRunner.cs
    UnknownBytesReporter.cs
  API/
    CubaseProjectReader.cs
    CanonicalProject.cs
    ProjectDependencyGraph.cs
```

### 28.1 Parse into structural AST *before* semantic DTOs

Represent each node with `(typeName, fieldType, sourceStart, sourceLength, parentId, children, rawId, rawPayloadRef, flags)`; allow duplicate node names. Unknown fields retain their offsets and lengths and do **not** cause later fields to shift or disappear. The AST decoder is version-family scoped, while semantic mapping can be extended by plug-in or project-type rules. A general byte-token scanner is allowed in **evidence mode**; it is not allowed to emit confidently named tracks, note objects or routing until the parser establishes parentage.

Use a result shape like:

```csharp
public enum ParseStatus { Decoded, Candidate, AssumedDefault, Missing, Unsupported, Corrupt }
public sealed record SourceSpan(string FileSha256, long Offset, long Length);
public sealed record FieldEvidence(
    string RuleId,
    string FormatVariant,
    SourceSpan Source,
    string? ParentNodeId,
    ParseStatus Status,
    string? FixtureId);
public sealed record ReadField<T>(T? Value, FieldEvidence Evidence);

public sealed record ProjectReadResult(
    CanonicalProject? Project,
    IReadOnlyList<ReaderDiagnostic> Diagnostics,
    IReadOnlyList<UnknownRegion> UnknownRegions,
    IReadOnlyList<FieldEvidence> Evidence);
```

All end-user-visible values need an evidence trail. Show unknown/missing data as unknown—not a value of zero, 4/4, 120 BPM, unmuted, or empty plug-in list unless the file or an explicit application default supports that conclusion.

### 28.2 Identifier mapping and source-order handling

Native track GUID/UID, mixer channel IDs, parent references, plug-in IDs and media pool IDs may be stored in different graphs. Resolve each table by **its actual ownership scope**. Preserve objects with the same display name, repeated buses with the same name and duplicate plug-in instances. Build `Track -> MixerChannel -> Inserts/Sends/Outputs` edges only when their native references prove the relationship. Preserve a separate fallback `PossibleAssociation` record with candidates and confidence, not a fabricated definitive edge.

### 28.3 Defensive parser policy

Read only. Reject negative counts, impossible strings, integer overflows, deep recursion and cycles where they are invalid. Default to bounded memory and CPU budgets, secure XML settings (no DTD, no external entities), ZIP-bomb guards for `.dawproject`, decompression ratio limits, and output path sandboxing. Do not dynamically instantiate a plug-in or trust embedded HTML/JS; hostile user-supplied project files are possible. Source references to absolute media paths should never cause automatic filesystem reads outside explicit user-authorized paths.

## 29. Controlled fixture protocol: reproduce, isolate, validate

**Minimum recommended corpus:** Cubase 10, 11, 12, 13 pre-13.0.30, 13.0.30+, 14, 15, plus SX2/SX3 and Nuendo `.npr` where available; **multiple independent projects per build**. One reference session per version plus controlled mutations is better than a giant unlabelled collection, but large real-world projects are also essential for testing.

### 29.1 Experiment families / exact changes

| Group | Paired project A/B | Key assertion | Most reliable independent check |
|---|---|---|---|
| Header | Save empty project in two patch versions | Container profile, actual version | File header / project GUI |
| Identifiers | Two same-name audio tracks | Both survive, unique identities | Project track list |
| Tracks | One Audio, MIDI, Instrument, Folder, Group, FX, VCA, Sampler | Correct classes/count and order | Project / mixer GUI |
| Folders | One nested folder, grouped and ungrouped children | Parent edges, no false group buses | Project folder tree |
| Tempo | Fixed 90→120; track ramp; tempo nodes | Event positions and interpolations | Tempo Track Editor/export |
| Time signatures | 4/4→7/8 at bar N | Full event map (not only initial) | Signature track |
| Markers | Point→range, musical→linear, 44.1→48 kHz | Range, ID, sample/tick position | Marker track / marker list |
| MIDI 1 | Pitch 0, 60 and 127, velocity 1/127 | Every valid note survives | SMF export |
| MIDI 2 | CC1, CC64, PB, program change, aftertouch | Event and channel fidelity | SMF export and MIDI List Editor |
| MIDI 3 | Notes crossing bar, overlapping notes, offset part | Part-relative vs absolute positions | SMF / visual grid |
| MIDI 4 | Duplicate parts, ghost/shared part, mute note | Containers, not duplicate-note aliases | Project GUI |
| Audio | Import WAV, trim, slip, copy, reverse, stretch, fade | Pool->event->clip mapping | GUI + media files |
| Audio references | Remove referenced WAV after save | Missing media is reported but no invented clips | Pool/missing files dialog |
| Mixer levels | 0, -6, -12 dB, -inf and +6 dB | Display dB vs raw fader | MixConsole |
| Pan | Hard L, C, hard R; stereo panner width | Correct real channel panner | MixConsole |
| Sends | One pre, one post, bypassed, muted, empty | Slot, state, destination and level | MixConsole |
| Routing | Direct output, group route, nested group, side-chain | Correct directed signal graph | MixConsole |
| Plug-ins | Repeated same VST3 on two tracks; same VST twice | Correct owner and slot | Inserts view |
| Plug-in state | Save changed vendor preset, disabled slot | State existence and byte span | Preset change & diff |
| Color | Change one track color with custom palette | Actual track index, not just palette | Track color GUI |
| Mute / solo | Save toggle A→B; reopen both | Persistence **per version** | Reopen and inspect |
| Name encoding | ASCII, umlauts, Japanese, emoji, trailing spaces | Encodings and length fields | Track/marker GUI |
| Large files | Known >=2 GB project in 13.0.30+ and <=2 GB control | Correct variant + 64-bit bounds | Steinberg version/profiles |
| Nuendo | `.npr` with unique Nuendo feature vs ordinary `.cpr` | Family detection independent of suffix | Nuendo, Cubase Pro |
| Legacy | Atari `.ALL`/`.ARR`; SX2/SX3 `.cpr` | Distinct recognition and partial decoded graph | Original app or authorized conversion |

### 29.2 Fixture manifest (commit alongside test data)

```yaml
id: cubase15-midi-pitch0-cc64
producer:
  product: Cubase Pro
  version: 15.0.x   # replace with EXACT version used
  operatingSystem: Windows
  licenseConfirmedByOperator: true
source:
  projectFile: fixture.cpr
  sha256: "<real sha256>"
  sizeBytes: null
  nativeFormatProfile: unknown
expected:
  trackCount: 1
  midiNotes:
    - pitch: 0
      velocity: 64
      positionBeats: 0
  midiControllers:
    - controller: 64
      value: 127
observations:
  guiScreenshots: []
  dawproject: null
  exportedSmf: fixture.mid
  trackArchiveXml: null
  notes: "SMF is the authority for CC64; DAWproject may omit it"
privacy:
  mediaFilesIncluded: false
  thirdPartyState: "remove-or-consent"
```

This is a **template** only; paths and values must be replaced with actual project data, and exact expected note pitch may require Cubase display-octave normalization. Hash the raw original before reducing or sanitizing it. Keep private instruments/presets and personally sensitive embedded metadata out of public fixture repositories without explicit rights and approval.

### 29.3 Delta-analysis workflow

1. `A.cpr` is a saved baseline. Change **one UI setting**; save as `B.cpr` (same Cubase build, local project context).
2. Record GUI expected value, SHA-256, exact length, and timestamp.
3. Detect any container header/chunk changes and identify invariant boundaries. CPR may rewrite unrelated metadata or reorder object nodes; raw whole-file diff may be noisy.
4. Parse both versions into structural AST, then normalize ephemeral IDs *only after proving they are ephemeral*.
5. Search for **value-correlated differences** across ≥3 settings (not just one delta), plus a return-to-baseline save (`A→B→A`).
6. Promote a field rule to reliable only if an unrelated control file has no matching delta and parser reports exact source spans.
7. Run against a second real project/track layout and a different Cubase version within the claimed compatibility range.
8. Maintain failing regression fixtures permanently. Report `Unsupported` instead of guessing for unvalidated formats.

## 30. Public communities, proprietary claims, and legitimately accessible private material

### 30.1 Primary forum/community findings

| Location | What it adds | Confidence and access |
|---|---|---|
| [Steinberg Project Inspector developer thread, 2026-10-30](https://forums.steinberg.net/t/project-inspector-cross-platform-vibe-coded/1044606) | Author seeks varied CPR fixtures; reports analysis of 138 projects and preliminary parser; plans future public repository | Public post only; docs/code **not yet published** when reviewed |
| [Steinberg SX/old-project migration discussions](https://forums.steinberg.net/t/unable-to-import-all-songs-to-cubase-sx3-midi-import-works/152499) | Historical import chain, failures and format distinctions | Anecdotal but actionable fixture targets |
| [Atari2Cubase thread, 2026-10-10 / updated 2026-10-27](https://forums.steinberg.net/t/free-tool-open-your-atari-st-cubase-songs-arr-all-in-cubase-nuendo-14/1043292) | An actively maintained converter and supported legacy edition targets | Converter internals undisclosed; verify output against originals |
| [CubaseTools author on Reddit, 2026-04-14](https://www.reddit.com/r/cubase/comments/1sl7an8/shared_my_cubase_parsing_tool_in_another_forum/) | Author explicitly acknowledges parser is a side project, invites feedback | Community corroborates authorship but not algorithm correctness |
| [GitHub feature issue about CPR support](https://github.com/ozzaii/bravoh-daw/issues/3) | Recognizes RIFF-style CPR and recommends cautious metadata/track/plugin scope | Proposal, not implemented decoder |
| [Steinberg plug-in inspection discussion](https://forums.steinberg.net/t/plugininspector-for-cubase-and-nuendo-projects-an-xlsx-report-generator-of-used-plugins/846415) | Native plug-in caches, reports and examples | Useful supplementary ID matching |
| [Nuendo `.npr` interoperability thread](https://forums.steinberg.net/t/saving-as-nuendo-project-file/725368) | Saving and opening can preserve `.cpr` suffix even within Nuendo | Application/version dependent observation |

### 30.2 Private-source access policy and practical next steps

This study did **not** access any private forum, paywalled source repository, private account, internal Steinberg documentation, decrypted commercial installer, leaked SDK or credentials. Public forum references to unpublished research do **not** grant access to its underlying artifacts. The paid **CPR Inspector** may demonstrate end-user functionality, but its implementation is proprietary. Do not describe its binary format internals unless its author has published them or provided authorized access.

Productive legitimate avenues:

- Request format field notes and permission to cite/compare work from the Project Inspector forum author; ask whether their 138-file dataset includes clean shareable tests and whether they can publish *anonymized field observations* rather than private project files.
- Open an issue or discussion in `schwifty00/CubaseTools` about exact field provenance, multi-version test results, `adcn` note 0 correctness, the duplicate-track-name merge and the `IO_GAP_THRESHOLD` documentation disagreement. Share repros and propose cross-validated C# implementation.
- Request author-provided sample projects with permission and privacy conditions; favor small **self-authored synthetic Cubase project files** over real productions and avoid redistribution of third-party plug-in state.
- If the user has legitimate access to a private reference repo, archive, manual, licensed tool or forum thread, ingest only data they explicitly provide/authorize; check license terms before porting code. Never bypass logins, DRM, access controls or purchase restrictions.
- Link every claim to a specific public post, commit, or authorized local fixture. Clearly distinguish “author reports,” “source code implements,” and “independently observed.”

## 31. Revised prioritized implementation backlog for Codex

Implement in large, testable blocks; never pretend the entire native format is already known.

| Phase | Deliverable | Exit evidence / acceptance criteria |
|---|---|---|
| P0 | Format detector, 64-bit bounded source reader, stable diagnostics | Recognizes all supplied fixture families; never mis-parses unknown as known; no crash on short/corrupt input |
| P1 | Parsed modern named-field AST and SX-family independent scanner | Class + field tree, exact spans, full raw unknown preservation; validated real CPR fixtures |
| P2 | Metadata, tempo, signature, markers | Multiple changes; valid source units and precision; no misleading defaults |
| P3 | Tracks, identity, nesting, mixer channel association | Exact names/count/order incl. duplicates; folder/group distinctions; no nearest-byte ownership in final graph |
| P4 | Plug-in list, processor/controller blob references, local UID enrich | Exact insert order; repeated plug-ins; missing/local plug-in metadata separate |
| P5 | Mixer Volume/Pan/Sends/Outputs | UI-controlled level/pan curves; pre/post and bypass; full graph edges, no guessed dB |
| P6 | MIDI parts/events and media-pool/audio clips | Pitch 0/127, CC64/CC1, positions, audio refs/trim/loop/fades against independent oracles |
| P7 | Automation, tempo curves, alternative timelines | Value domains, ramps, parent association and all event points tested |
| P8 | Large-project format and Nuendo equivalence | >2GB on-demand files with real 64-bit lengths; edition-aware semantic mapping |
| P9 | `.all`/`.arr`, historical SX2/3 coverage | Separate versioned decoders, supported feature matrix, golden converted sessions |
| P10 | Fuzzing, performance, regression/test fixture docs and UI integration | CI, measurable corpus coverage, no silent data loss; architecture documented; production build successful |

**On stopping criteria:** “all supported features implemented” is not the same as “all Cubase fields known.” PLAN.md must record the exact remaining unknown types and unsupported versions, with evidence, rather than deleting issues to make the plan appear complete. A hard blocker is lack of a genuine versioned CPR fixture or an undecoded structure after repeatable analysis, not a guessed 120 BPM or fixed-track default.

### 31.1 Strong VS Code / Codex implementation prompt

> You are implementing a native, read-only Cubase/Nuendo project-format reader in **CubaseProjectExplorer** (.NET 10). Read `AGENTS.md`, project skills and `PLAN.md`; use this research document as a **source catalog and hypothesis ledger**, never as an official complete binary specification. Audit existing parser implementation and preserve its valid working functionality, but replace proximity/name-based guesses with bounded parse trees, saved identity graphs, exact byte-span evidence and version-aware decoding. Start with `RIFF`/`NUND` and the verified modern field vocabulary, and implement a separate legacy/SX family instead of inventing one universal grammar. Use `schwifty00/CubaseTools` (MIT) as a reference, especially `docs/cpr_format.md` and the `extractors`, but verify each candidate against actual CPR fixtures; do not blindly port its synthetic-test assumptions or deduplication. Create versioned golden fixtures and delta tests for all decoded fields, including duplicate track names, pitch-zero MIDI notes, nested folders, sends, automation, multi-GB and Nuendo variant behavior. Preserve unknown chunks, represent missing or speculative data honestly, and never execute plug-ins or write to CPR. Run tests and production Release build after each substantial batch, inspect real project output, update coverage matrix and `PLAN.md` with evidence, commit changes in logically grouped batches on the working branch, and keep progressing through all actionable items until validated completion or an explicitly documented external blocker. Do not stop after merely adding placeholder parsers, a regex scanner or one batch.

## 32. Additional source register for revision 2

### Public code / technical format investigation

- [CubaseTools repository, MIT](https://github.com/schwifty00/CubaseTools)
- [CubaseTools complete candidate CPR structure reference](https://github.com/schwifty00/CubaseTools/blob/master/docs/cpr_format.md)
- [CubaseTools main CPR parser](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/cpr_parser.py)
- [CubaseTools metadata](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/metadata.py)
- [CubaseTools track extraction](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/tracks.py)
- [CubaseTools mixer extraction](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/mixer.py)
- [CubaseTools media, MIDI, marker and automation extraction](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/media.py)
- [CubaseTools plug-in extraction](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/plugins.py)
- [CubaseTools test fixtures and synthetic unit tests](https://github.com/schwifty00/CubaseTools/tree/master/tests)
- [CubaseMarkers.js](https://gist.github.com/andigamesandmusic/94be52eff000013d7cdf5daef54bc4aa)
- [SX2/SX3 reverse engineering repository](https://github.com/omeriko9/Cubase-Project-File-Reverse-Engineering)
- [SX2 research notebook](https://github.com/omeriko9/Cubase-Project-File-Reverse-Engineering/blob/master/My%20thought%20process.txt)
- [Conservative CPR fixture protocol](https://github.com/ColonelKernel/session-state-explorer-cubase/blob/main/docs/CUBASE_FIXTURE_PROTOCOL.md)
- [Steinberg VST3 state persistence](https://steinbergmedia.github.io/vst3_dev_portal/pages/FAQ/Persistence.html)
- [Public DAWproject spec](https://github.com/bitwig/dawproject)

### Steinberg forum and official documentation

- [Cubase Project Inspector (cross-platform) discussion](https://forums.steinberg.net/t/project-inspector-cross-platform-vibe-coded/1044606)
- [Atari2Cubase community release](https://forums.steinberg.net/t/free-tool-open-your-atari-st-cubase-songs-arr-all-in-cubase-nuendo-14/1043292)
- [Cubase >2GB issue, official fix statement](https://forums.steinberg.net/t/a-critical-announcement-about-cpr-files-larger-than-2gb-getting-corrupted/895880/77)
- [Nuendo >2GB issue announcement](https://forums.steinberg.net/t/a-critical-announcement-about-npr-files-larger-than-2gb-getting-corrupted/895881)
- [Cubase 15 project file manual](https://www.steinberg.help/r/cubase-pro/15.0/en/cubase_nuendo/topics/project_handling/project_handling_about_project_files_c.html)
- [Nuendo 15 project file manual](https://www.steinberg.help/r/nuendo/15.0/en/cubase_nuendo/topics/project_handling/project_handling_about_project_files_c.html)
- [NPR/CPR use with Nuendo](https://forums.steinberg.net/t/saving-as-nuendo-project-file/725368)
- [NPR/CPR compatibility by Cubase edition](https://forums.steinberg.net/t/cubase-projects-from-nuendo/665258/2)
- [DAWproject CC64 event omission](https://forums.steinberg.net/t/issue-dawproject-export-missing-midi-cc64-sustain-events-cubase-15-pro/1023436)
- [DAWproject missing native MIDI tracks](https://forums.steinberg.net/t/dawproject-export-no-midi-track-export/1009070)
- [Cubase 15 DAWproject clip-gain report](https://forums.steinberg.net/t/cubase-dawproject/1024306)
- [Kontakt `audioComponent` analysis](https://forums.steinberg.net/t/track-archive-xml-anyone-can-decrypt-where-the-kontakt-instrument-nki-name-hide-in-the-xml/1034306)
- [Track Archive XML coded state discussion](https://forums.steinberg.net/t/any-coders-here-by-any-chance/918444?page=2)
- [Plug-in registry / PluginInspector](https://forums.steinberg.net/t/plugininspector-for-cubase-and-nuendo-projects-an-xlsx-report-generator-of-used-plugins/846415)
- [Cubase 15 embedded project preview anecdote](https://forums.steinberg.net/t/cubase-15pro-project-previews-and-test-signal-not-playing-in-hub/1013479?page=2)

### Community / product claims (lower evidentiary weight)

- [Reddit: CubaseTools author sharing project](https://www.reddit.com/r/cubase/comments/1sl7an8/shared_my_cubase_parsing_tool_in_another_forum/)
- [CPR Inspector proprietary tool](https://courses.domsigalas.com/cpr-inspector)
- [bravoh-daw CPR feature request](https://github.com/ozzaii/bravoh-daw/issues/3)

## 33. Revision 2 conclusions and outstanding unknowns

**What is now implementable with referenced concrete candidate code:** bounded top-level probes, common metadata, version markers, sample-rate/tempo/signature candidate objects, basic track classes, channel volume/pan candidates, send and bus UID candidates, plugin identifiers and opaque state spans, marker candidates, selected MIDI-note signatures and selected automation-point candidates. Important: “candidate” does not mean verified across all Cubase 10–15 builds.

**What remains unproven:** a complete modern CPR object-graph grammar, all per-version chunk layouts (including the large-project variant), stable parent ID schema, authoritative current color assignment, comprehensive MIDI CC/per-note data, automation scaling/interpolation, complex media pool and audio event/take graph, folder/group channel edges, VST2/3 state owner resolution in all cases, and binary `.ALL` / `.ARR` specifications. No publicly reviewed source establishes 100% compatibility across every Cubase version, every extension and every plug-in.

The decisive implementation improvement is to **port selectively, prove via genuine Cubase-generated paired fixtures, and retain source-offset confidence**. The public parser gives a head start; source auditing shows why blindly treating regex hits as fully decoded project objects would undermine the project explorer. Format research and project coverage should remain an evidence-driven, versioned part of `PLAN.md`.



---

# Revision 3 addendum: direct Cubase 15 EXE evidence

## 1. Executive assessment

This EXE contains code that deals with Steinberg's native project container, including named project reader/writer components and relevant object types. It yields an independently grounded, highly valuable **RIFF/RIF2 + NUND parsing discriminator**, strongly supports **big-endian header integers**, and exposes a second layer involving `ROOT` and `ARCH` identifiers. It does **not** provide a turnkey source-level mapping from every serialized field to every Cubase data model property: there is no embedded PDB, and neither registration strings nor machine-code references, in isolation, reveal a full serialization grammar.

### Confidence vocabulary

- **Confirmed in this EXE:** observed exact string, instruction, or control-flow relationship; exact address supplied.
- **Strong inference:** multiple instructions support an interpretation but independent CPR fixtures are still necessary.
- **Candidate from third-party research:** published parser rule that has not been revalidated against this EXE or real paired `.cpr` files in this session.
- **Unknown:** no defensible format rule yet.

## 2. Input provenance and binary characteristics

| Property | Result |
|---|---|
| File size | `135,534,616` bytes (~129.3 MiB) |
| SHA-256 | `b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0` |
| Format | Windows `PE32+`, x86-64, GUI executable |
| Image base | `0x0000000140000000` |
| PE timestamp | `2026-10-09 09:50:17 UTC` (from executable header; not a trustworthy release date) |
| Code sections | `.text`, `IPPCODE` (an additional executable section; meaning not established) |
| Read-only section | `.rdata` with numerous project / track / object strings |
| Export table | Empty |
| Debug metadata | Reference ending in `cubase15.pdb`; the actual PDB was not provided |
| CLR metadata | Absent: native binary, not directly C#-decompilable |
| Unwind entries | ~401,430 `.pdata` entries provide many recoverable code-function bounds |

**Address convention:** `VA` below is the virtual address with image base `0x140000000`. Addresses are **for this SHA-256 only**. On a different build, recalculate addresses; do not hardcode them into the reader.

## 3. Most important finding: `RIFF` and `RIF2` are separately decoded

A cluster of closely related native routines in `.text` compares the initial 4-byte header against **two distinct container signatures**:

- `RIFF` — integer compare constant `0x52494646` at `VA 0x144615218`.
- `RIF2` — integer compare constant `0x52494632` at `VA 0x144615220`.
- `RIF2` then follows a read routine at `VA 0x1439df880` that reads **8 bytes**.
- `RIFF` uses a read routine at `VA 0x1439df650` that reads **4 bytes**.
- A second analogous code path at `VA 0x144615348` / `0x144615353` similarly branches on `RIFF` vs `RIF2`.
- A third routine at `VA 0x14461553c` / `0x144615548` distinguishes the same signatures.

The code also sets a stream byte-order flag to `1` at `VA 0x1446151e9`. The invoked 32-bit and 64-bit readers read fixed-width integers and, unless the stream's byte-order flag is zero, **reverse the byte order** (`0x1439df650` and `0x1439df880`). Given the x86-64 little-endian host and the comparison constants representing ASCII in natural order, **big-endian interpretation of the 32/64-bit header fields is strongly supported**. That is a property of this internal read path, not a blanket assertion about every payload in a CPR.

**External cross-check:** Steinberg community threads explicitly discuss the `RIF2` 64-bit project format (introduced starting with the Cubase/Nuendo 13.0.30 compatibility boundary); Steinberg's Cubase 15 documentation confirms that project files exceeding 2 GB require version 13.0.30 or later. Sources:

- https://forums.steinberg.net/t/nuendo-14-session-compatibility/978644
- https://forums.steinberg.net/t/nuendo-14-session-compatibility/978644/15
- https://www.steinberg.help/r/cubase-pro/15.0/en/cubase_nuendo/topics/project_handling/project_handling_about_project_files_c.html

### Header hypothesis backed by the binary and community evidence

| Offset | `RIFF` project (older 32-bit envelope) | `RIF2` project (newer 64-bit envelope) |
|---|---|---|
| `+0x00` | ASCII `RIFF` | ASCII `RIF2` |
| `+0x04` | Four-byte **big-endian** size-like integer | Eight-byte **big-endian** size-like integer |
| `+0x08` / `+0x0C` | Expected ASCII `NUND` at `+0x08` | Expected ASCII `NUND` at `+0x0C` |
| Following bytes | Proprietary project payload / chunk graph | Proprietary project payload / chunk graph |

The exact semantic equation linking the size-like field to physical file length is **not proven** here. Do not assert `declaredSize == FileSize - 8` for all versions. A third-party TestDisk signature recognizer uses its own interpretation of the field; compare against genuine Cubase-generated `RIFF` and `RIF2` fixtures.

**Avoid a common mistake:** the executable contains the instruction bytes `44 4E 55 4E` (`DNUN`) for the immediate integer `0x4E554E44`. On a little-endian CPU, the *instruction operand bytes* are reversed; the actual four-character value is **`NUND`**, not `DNUN`. Searching the EXE for the literal byte sequence `NUND` misses this important instruction pattern.

## 4. `NUND`, `ROOT`, and `ARCH`: native serialization layer

The following instructions are present in the project-/archive-related code paths:

| VA | Observation | Interpretation |
|---|---|---|
| `0x14239fdbb` | `mov edx, 0x4e554e44` before call to `0x144603b90` | A writer/formatter path passes the `NUND` identifier |
| `0x1423a0337` | Another `mov edx, 0x4e554e44` | A second `NUND` code path |
| `0x1429149a1` | `mov edx, 0x4e554e44` before call to `0x144603d30` | A parser/reader checks the `NUND` identifier |
| `0x142932f7a` | Another `NUND` identifier before a call | Related container stream handling |
| `0x144603dbc` | `cmp ecx, 0x524f4f54` | The reader discriminates `ROOT` |
| `0x144603f25` | `cmp ecx, 0x41524348` | The reader handles `ARCH` |
| `0x144603d30` | Entry of the routine containing both `ROOT` and `ARCH` conditions | High-priority archive/chunk deserializer to map |
| `0x144603b90` | Corresponding routine used with a `NUND` identifier | High-priority archive/chunk serializer to map |

**Supported inference:** there is a structured container layer **beyond the RIFF/RIF2 header**, with typed subrecords or archive entries. `ROOT` and `ARCH` are tangible comparison values. **Unknown:** their exact on-disk offsets, nesting rules, record headers, the semantics of their payloads, and how they relate to a complete object graph. Do not write a recursive `ROOT/ARCH` reader that invents record-length rules. Instead, trace `0x144603d30` and its callees on genuine fixtures.

### Relevant function boundaries (from `.pdata`)

| Entry VA | End VA | Why investigate |
|---|---|---|
| `0x1446151a0` | `0x1446152c3` | RIFF/RIF2 dispatch and 32/64-bit length handling |
| `0x1446152d0` | `0x1446153fc` | Another RIFF/RIF2 parse path |
| `0x1446154a0` | `0x1446155ef` | Additional RIFF/RIF2 input path |
| `0x1439df650` | `0x1439df6b0` | Read 32-bit value, conditional byte-swap |
| `0x1439df880` | `0x1439df8fd` | Read 64-bit value, conditional byte-swap |
| `0x144603d30` | `0x144603fb3` | `ROOT` / `ARCH` reader routine |
| `0x144603b90` | `0x144603d20` | Archive writer routine invoked with `NUND` |
| `0x1429148c0` | `0x1429149d6` | Contains `NUND`-related parse/validation logic |
| `0x14239fa40` | `0x14239fe6e` | Uses `NUND` writer path |

## 5. Supported file types / registration strings

The EXE registers these strings in UTF-16LE (located near raw file offset `0x060fd9a8`):

| Literal | Role |
|---|---|
| `Cubase Project File`, `cpr` | Native Cubase project |
| `Nuendo Project File`, `npr` | Native Nuendo project |
| `com.steinberg.cubase.project` | Project identifier |
| `com.steinberg.nuendo.project` | Project identifier |
| `audio/x-vnd.Steinberg-Cubase-Project` | Registered MIME-like value |
| `application/x-vnd.Steinberg-Cubase-Cachefile` | Separate cache-file type |
| `audio/x-vnd.Steinberg-Backup-File`, `bak` | Backup format registration |
| `com.steinberg.project-pack` | Another project packing format |
| `Nuendo Live Project`, `nlpr` | Separate Nuendo Live variant |
| `riff`, `RIFF` | Generic RIFF type registration |

File extensions alone do not guarantee a shared serialization grammar. Dispatch on **actual magic bytes and internal form** first; use filename extension as secondary metadata.

## 6. Object/model names recovered from EXE

Some literal names found in `.rdata`:

- `MTempoEvent` (`0x05f85bf8`), `MTempoTrackEvent` (`0x05fafed8`), `MTimeSignatureEvent` (`0x05fa3308`)
- `MTrackEvent` (`0x05f3ad00`), `MMidiTrackEvent` (`0x05edf898`), `MAudioTrackEvent` (`0x05f8a358`)
- `MMidiPartEvent` (`0x05eeb038`), `MMidiNote` (`0x05ed9e88`), `MAudioPartEvent` (`0x05f92420`)
- `MAutomationTrackEvent` (`0x05f46568`), `MMarkerEvent` (`0x05f4d878`), `MMarkerTrackEvent` (`0x05edc388`)
- `MDrumTrackEvent` (`0x05fc2f70`), `MChordTrackEvent` (near `0x05f465a0`), `MLaneTrackEvent` (`0x05f3adf8`)
- `DeserializerFactory` (`0x060fdea0`)
- `VstXmlProjectReaderConverter` (`0x061122b0`), `VstXmlProjectWriterConverter` (`0x06112d10`)
- `FBlockStreamReader` (`0x065a8ed0`), `FCompressionStream` (`0x065abbd8`)
- `ZLibCompressor` (`0x0652db30`)

**Crucial:** the names alone do **not** establish that each is a serialized `.cpr` node or that `FCompressionStream` / zlib is applied to the CPR container. Some code is generic audio, UI, or resource handling. The attached TSV contains **185 candidate music-model strings** extracted from the executable with original offsets. It deliberately labels them as *literal strings, serialization not independently verified*.

### Useful nearby strings that may identify object properties

`MTempoEvent` appears beside literal strings `Tempo`, `SigNom`, `SigDenom`, `Beats`, `offsetTempo`, `PPQ Source`, `Beat Offset`, `VariationMark`, `Bar`, `Tempo-Scaling`, `TempoMark`, `MusicalUpbeat`, `MusicalUpTempo`, `AudioMarkerPpq`, and `AudioMarkerValue` (around raw offsets `0x05f85c04`–`0x05f85cd0`). **These are candidate property/semantic vocabulary, not verified byte-level field layouts.** The reader must test them against an exported tempo/signature map and controlled projects.

## 7. Third-party candidate rules to validate against the executable

The independently maintained [CubaseTools parser](https://github.com/schwifty00/CubaseTools) contains useful candidate CPR-specific patterns and source code at:

- [Binary format notes](https://github.com/schwifty00/CubaseTools/blob/master/docs/cpr_format.md)
- [Metadata extraction](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/metadata.py)
- [Track extraction](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/tracks.py)
- [Mixer extraction](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/mixer.py)
- [MIDI and automation extraction](https://github.com/schwifty00/CubaseTools/blob/master/cubasetools/core/extractors/media.py)

Its proposed named-field tags are `0x0001` (integer), `0x0002` (compound), `0x0004` (double), and `0x0008` (string), with further BE integers and compound child-count conventions. **Not independently proven in this EXE audit**. Treat as *schema hypotheses* until paired fixture tests pass.

Use the native executable to target validation of the following candidates:

1. The hierarchy and offsets of `ROOT` / `ARCH` and the relationship between archive entries and named-field objects.
2. The 32/64-bit length and endian rules at every nested chunk level, not just the top-level envelope.
3. `MTempoEvent`, signature event structures and time base (PPQ/ticks vs samples) across versions.
4. Track identity, parent-child references and IDs; never merge tracks solely by name or nearby file offsets.
5. MIDI part + note serialization; validate pitch, note-on/off velocity, start, duration and MIDI CC against independent exported MIDI.
6. Automation-point data, interpolation types, scaling and owner identifiers.
7. Audio event/media pool object relationships and external media references.
8. Plug-in class IDs and opaque component states; do not assume all third-party plug-in blobs share XML schemas.
9. Compression handling: discriminate actual chunk compression on fixtures before assuming a global zlib transform.

**Source-code caution:** CubaseTools' visible test suite includes hand-built fake CPR byte buffers. These are useful unit tests for scanning heuristics but do not prove full Cubase compatibility. In its postprocessing it collapses tracks by name, potentially losing legitimate duplicate names. Some MIDI and automation extraction is bounded heuristic scanning rather than a recursive owner-validated parse.

## 8. Minimal working reader artifact: `Cubase15HeaderProbe.cs`

The accompanying source file implements a conservative, read-only **container header probe** in .NET 10:

- Reads `RIFF` (32-bit length) or `RIF2` (64-bit length).
- Reads the numeric field using big-endian conversions.
- Validates `NUND` at the appropriate position.
- Reports the physical stream length and preserves the incoming stream position.
- Rejects unrelated `RIFF/WAVE` files instead of treating arbitrary RIFF files as Cubase.
- Deliberately does **not** impose an unproven size-equation or attempt speculative `ROOT`/`ARCH` interpretation.

`Cubase15HeaderProbeTests.cs` supplies synthetic tests for endian handling and dispatch. **The C# has not been compiled in this environment** (no .NET SDK available), and it has not been validated with a genuine `RIF2` CPR file; compile and extend it in the real project.

## 9. High-priority implementation plan for CubaseProjectExplorer

| Priority | Work package | Completion criterion |
|---|---|---|
| P0 | Header parser for `RIFF` and `RIF2` | Genuine samples from both variants recognized; no false WAV positives; sizes and stream offsets measured |
| P0 | Binary-diagnostic mode | Exact file offset, raw bytes, declared endian/width and context recorded for each parsed field |
| P0 | Recursive chunk boundary inference | `ROOT` / `ARCH` record bounds confirmed on multiple saved projects, including RIF2 |
| P1 | Named-field grammar and object reader | Parses nested compound values and unknown type tags without corrupting stream positions |
| P1 | Object graph and identity mapping | Duplicate-name tracks kept separately with stable IDs and parents |
| P1 | Metadata/tempo/signature | Every decoded value reconciled to Cubase screenshots and MIDI/Track Archive exports |
| P1 | Track/folder/event/media pool | Ordered audio/MIDI/instrument tracks and clips mapped to the correct owner |
| P2 | MIDI event families | Notes, controllers, program changes, expression validated against MIDI exports |
| P2 | Automation and routing | Values, interpolation and bus/send graph compared with Cubase project settings |
| P2 | VST states | IDs, instance ordering, components preserved as opaque binary where decoding is unknown |
| P3 | Legacy `.all` / `.arr` | Separate format path, with independent fixtures from old Cubase versions |

### Recommended differential fixture set

For each version/build available, save projects with **one controlled change** between files: empty project; one renamed track; duplicate track names; a single MIDI note with known pitch/velocity; change tempo from 120→121; add a 7/8 signature at bar 3; add a marker; change pan from center→full left; turn mute on; add an automation point; insert one known VST3; change a single preset parameter; add an audio clip and relink its media. Export corresponding MIDI, Track Archive XML, and DAWproject wherever supported. Record a manifest with the project version/build, file signature, SHA-256, and exact change.

### Minimum next reversing targets

1. Trace `0x1446151a0` to prove full `RIFF`/`RIF2` wrapper semantics and size constraints.
2. Trace `0x144603d30` and its helper `0x1446150b0` to map `ROOT` / `ARCH` records, raw lengths, and nested record iteration.
3. Follow callers of the `NUND`-checking function within `0x1429148c0`–`0x1429149d6` to locate the full project read path.
4. Find `DeserializerFactory` registration and resolver logic, not just the string literal; recover a mapping from type key to constructor/deserializer callbacks.
5. Identify code references for `MTempoEvent`, `MMidiPartEvent`, `MAutomationTrackEvent`, `MMarkerEvent` and inspect memory/object fields in controlled *legitimate local* test sessions, if a user-owned Windows debugger is available.
6. Verify whether compression is per block, per field, plug-in state only, or unrelated to CPR.
7. Design large-file tests that exercise `RIF2` and stream positions above 2 GiB without allocating the entire file.

## 10. What this does **not** establish

- No complete `.cpr`/`.npr` serializer class layout or vtables were reconstructed.
- No binary sample project was provided for differential validation; **only the Cubase EXE** was available.
- We did not derive or validate full named-field tags from the executable in this pass.
- `FCompressionStream` / `ZLibCompressor` presence does not prove use for CPR serialization.
- The `RIFF` / `RIF2` code can be shared with other Steinberg resource types; connect its callers to the CPR registration and `NUND` branch before claiming each generic helper is CPR-exclusive.
- Old `.all`/`.arr` binary layouts cannot be inferred from modern Cubase15.exe alone.
- No assurance that the exact filename `Cubase15.exe` or PE timestamp proves a particular commercial release; use the SHA-256 to identify the analyzed build.

## 11. Deliverables

- `Cubase_Project_Formats_Deep_Dive_v3.md`: earlier 33-section research + this new native-binary evidence.
- `Cubase15_Executable_Reverse_Engineering_Findings.md`: this standalone evidence report.
- `Cubase15HeaderProbe.cs`: working *design* for a read-only container prefix reader; compile and test in .NET 10.
- `Cubase15HeaderProbeTests.cs`: synthetic xUnit tests (not a real-CPR acceptance suite).
- `Cubase15_project_type_catalog.tsv`: 185 candidate `M*` model strings and file offsets.
- `verify_cubase15_binary_evidence.py`: minimal no-dependencies script to reproduce important static observations against **the same hash**.

**Most valuable next input:** at least one Cubase 15 `RIF2` project and one legacy `RIFF` project, each with an empty baseline plus a one-change copy. The actual debugger symbols `cubase15.pdb`, if legitimately available, would also be unusually useful; a PDB *path string* in the EXE is not equivalent to having the symbols.
