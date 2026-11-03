# Cubase 15 executable: static reverse-engineering evidence for a native CPR/NPR reader

> Historical initial pass. The [2026-10-31 serializer analysis](CUBASE15_SERIALIZER_ANALYSIS.md)
> extends these findings with ROOT/ARCH framing, class versions and reader/writer
> chains. Current compilation and fixture-validation status is tracked in
> [PLAN.md](../../PLAN.md); statements below about unavailable fixtures or SDKs
> describe the original analysis environment.

**Date:** 2026-10-31  
**Target:** `CubaseProjectExplorer`, read-only, C# / .NET 10  
**Input:** user-supplied `/mnt/data/Cubase15.exe`  
**Method:** PE header inspection, byte-pattern searches, Windows x64 static disassembly (`llvm-objdump`), `.pdata` exception-table function boundaries, constant and string cross-references. **No execution or debugging.**  
**Completeness:** **partial, concrete native-reader evidence**, not a reconstructed production CPR parser.

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
