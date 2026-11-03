# Cubase 10–14 parser review and MIDI browsing changes

Reviewed 2026-10-31. This records source observations, implementation boundaries
and read-only comparison results. Successful parsing does not establish complete
Cubase musical semantics.

## Why the previous MIDI path missed these versions

The existing native path required an RIF2 `MMidiNote` with an exact 112-byte
body and an `adcn` marker at a fixed offset. It did not read packed note streams
inside `MMidiPart`. This was an implementation gap, rather than evidence that
Cubase 10–14 projects cannot be inspected. It also labelled every indexed native
note as a Cubase 15.0.40 observation, including uncontrolled corpus matches.

Two coexisting representations now have bounded candidate readers:

- Packed modern note fields inside recognized MMidiPart bodies, in RIFF or RIF2.
- MMidiNote record bodies with a bounded typed-attribute count. The body length
  is checked against `17 + 14 * attributeCount + 25`, instead of assuming 112.
  Five attributes produce 112 bytes; six produce 126. A local Cubase 14.0.40
  sample contains 189 nonempty 126-byte note records, plus packed notes.

The scanner preserves fractional/negative positions, zero velocity, MIDI pitch
zero and zero durations as observations. It does not round, clamp velocity to
one, trim parts or turn raw positions into beats. Recognized note-record ranges
are excluded from packed scanning: a class body-size byte such as 0x9A must not
masquerade as a note-on status and count a record twice.

## Reviewed sources and their actual scope

| Source | License reviewed | Useful evidence | Scope/limitations |
|---|---|---|---|
| [BandPass Records parser, commit 6d5d82ac](https://github.com/bandpassrecords/daw-project-manager/blob/6d5d82ac352ddb83212e1240612e24a252d03d5c/lib/services/daw_parsers/cubase_project_parser.dart) | MIT, copyright 2026 BandPass Records | ARCH class references; MMidiPart packed notes; track-node strings; FNPath pool candidates; plugin name properties | The source says reverse engineered against Cubase 13; its container check accepts RIFF only. It rounds/clamps some note values and trims/deduplicates clips. Those transformations were not adopted here. |
| [MyDAW importer at ee232374](https://github.com/omeriko9/MyDAW/blob/ee232374b9ce2e6e243a3d29e7afefcfd17f0354/engine/src/import/CprImportProvider.h) | GPL-3.0 | Documents compact versus class-record MIDI, inherited serializers, part offsets, controller families, mixer and pool relationships | Broad author-reported SX–13 empirical coverage; still has unimplemented fields. No GPL source was copied or translated into this project. It is an additional format research source, not our validation oracle. |
| [Tony7466/TestDisk RIFF recognizer](https://github.com/Tony7466/testdisk/blob/master/src/file_riff.c) | Source header/license reviewed as recovery-tool research | Recognizes NUND at offset 8 and calculates BE32 size + 12 | A file-carving recognizer, without track/MIDI/plugin semantics. This path does not supply RIF2 field decoding. No code copied. |
| [template.io Cubase assets at ff137b35](https://github.com/wogamusic/template.io/tree/ff137b3539e14b53c6831dcb05f740cd05d0d9c0/assets/for-cubase) | Repository MIT, copyright 2022 JP Wogaman II | Public Cubase 12/15 empty-track CPRs and paired track-archive XML | Useful independent structure/count comparison, without note-content or timing validation. Downloads stay in ignored artifacts and are not redistributed. |
| [fgimian/cubase-project-plugins](https://github.com/fgimian/cubase-project-plugins) | MIT | Supports RIFF/RIF2 metadata and Plugin UID/Name/Original Plugin Name sequences; source has cross-version cases | A plugin-property scanner. This project already has that bounded token strategy. It does not establish MIDI ownership, plugin state or a routing graph. |

The new C# note scanner is independently written from the field descriptions
and local byte observations. No third-party implementation was vendored.

## Bounded implementation and provenance

### Additional evidence from the user's IDA export

The local Cubase15.exe.asm header identifies the same executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`
and image base 0x140000000 as the earlier binary analysis. The export was still
growing while inspected. Bounded, fixed-length mapped snapshots were used to
read complete selected procedures; the full assembly is ignored and is not
redistributed. An IDA header is provenance metadata, not an integrity guarantee
for the exported text.

| Native procedure | Directly observed load behavior |
|---|---|
| MMidiEvent base reader `0x141BB0390` | Reads one f64 and three u8 values, then asks the archive for the active **MMidiEvent** class version. Version > 0 reads flags; version > 1 invokes the attribute reader. |
| Flag primitive `0x1439DF250` | Reads exactly two bytes, with conditional byte swapping. The width was also cross-checked against disassembly of the original executable. |
| Attribute reader `0x141B74340` | Reads a u32 count through `0x1439DF440`, then deserializes the attribute entries. |
| MMidiNote reader `0x141BB4690` | Calls the base reader, reads three f64 values and reads one further byte only when the active **MMidiNote** class version is positive. |
| MMidiNote writer `0x141BB49A0` | Calls the base writer and emits the matching three f64 values plus one byte. |

For the supported modern shape, the fixed record prefix is f64 position,
channel/pitch/velocity bytes, u16 flags, then u32 attribute count: 17 bytes.
The supported scalar attributes consume 14 bytes each. The positive note-class
version has a 25-byte suffix. The scanner now reads the full bounded count and
preserves nonzero flags as `RawFlags`, without assigning mute, expression or
other musical meanings to individual bits. Bounded class-record matching also
accepts a zero attribute count; the weaker packed-stream scan still requires
at least one typed attribute to reduce accidental matches.

This establishes why one application's version string, or the MMidiNote class
version alone, cannot select every historical layout: inherited MMidiEvent
versions alter its prefix independently. The current shape matcher does not
implement all those older branches or every attribute type.

Reproduce targeted extraction without printing a growing full file:

```powershell
python scripts/inspect_cubase15_ida_export.py Cubase15.exe.asm --output artifacts/validation/ida-midi
```

The script validates the input hash declared in the header, fixes the mapped
snapshot length, caps each procedure excerpt at 64 KiB, and reports procedures
not yet present in that snapshot. Excerpts stay under ignored artifacts.

Class scanning remains bounded by the existing archive limits. Packed scanning
adds a 4 MiB per-part and 32 MiB aggregate arrangement budget, at most 4,096
part scan contexts and 10,000 total note candidates. Cancellation and explicit
partial warnings apply. Unknown note encodings and legacy SX/C4/C5 43-byte
streams are not silently routed through the modern grammar.

Each observation keeps its exact file offset, source layout, candidate enclosing
track/part names and offsets, and optional raw channel. Class/part containment
is a possible association, not a validated Cubase owner edge. The historical
`CprRif2MidiNoteCandidate` type name remains for persistence/API compatibility;
`SourceLayout` identifies the actual path. Normalized MIDI, PPQ, clip flattening,
tempo and Standard MIDI export are not inferred from these candidates.

## Read-only evidence

The corpus pass selected the smallest available sample for each of 31 distinct
saved-by version hints in the user-authorized D:\Cubase corpus. Every CLI inspect
completed; all source SHA-256 values before/after matched. These are uncontrolled
project observations, not independent Cubase exports or field-level assertions.

| Saved-by major hint | Samples | Samples with note candidates | Note candidates |
|---|---:|---:|---:|
| 10 | 5 | 4 | 3,891 |
| 11 | 5 | 2 | 95 |
| 12 | 9 | 5 | 5,970 |
| 13 | 7 | 3 | 1,272 |
| 14 | 5 | 2 | 1,911 |
| Total | 31 | 16 | 13,139 |

Zero observations do not establish an empty musical project: unsupported or
unrecognized representations may also produce none. Private paths and full
JSON reports remain under ignored `artifacts/validation/midi-source-review/`.

Public template.io downloads were verified against the Git blob IDs in the
pinned directory listing, their exact lengths, and SHA-256:

| Asset | Bytes | SHA-256 |
|---|---:|---|
| Cubase 12 CPR | 16,710,458 | `05482c5fe8bc98b554c1b40e5c83dab16817ee7d95b38484e60aa6233178d7bf` |
| Cubase 15 CPR | 16,584,061 | `fb364fee2d29a40e743c498f3af1c017fc3889374d2f1042e1df7244c0c2570f` |
| Cubase 12 XML | 92,987,945 | `47a55f21bd249623c6fbf2664c0bfcbec76a1fe1bbdc4b6f7352d33ddc849e8b` |

Both public CPRs inspect successfully and expose zero note candidates, consistent
with their empty-track purpose. Streaming the Cubase 12 XML finds 2,176 declared
MMidiTrackEvent objects; the CPR reader finds 2,176 candidate MIDI-track records.
The XML has 18 folder objects; CPR has 19 folder candidates and one device carrier,
so total visible-track equivalence is deliberately not claimed. This is a useful
independent count cross-check, not full track/owner/plugin verification.

## MIDI page design and acceptance

The user's screenshot showed an empty exported-track pane reserving most of the
page and raw per-note cards below it. The page now chooses native observations
or exported tracks explicitly. Native groups occupy the primary browser, with
a selected-group pitch/position plot, a compact sortable note table, rounded
display numbers and exact source offsets. A single candidate label and an
Evidence & source popup replace repeated paragraphs on every note. Mixed
encodings in one candidate part stay together; duplicate names at different
offsets remain distinct. Narrow layouts replace the group grid with a compact
project/part selector and show the selected group's plot/table at full width.

The plot uses raw stored positions with an explicit unverified axis; it does
not suggest verified clip placement or enable native MIDI export. Older stored
snapshots need a Project Library rescan to acquire new reader results; refreshing
the index alone rebuilds from those saved snapshots.

Current checks: 423 Release tests pass, including the controlled private fixtures,
packed-field bounds/zero values, variable record lengths, class-size/status
ambiguity, zero-attribute records, nonzero raw flags and stable candidate grouping.
CLI and Uno Desktop Release builds pass with zero warnings/errors. Actual Windows
11 dark-theme review at 192 DPI checks wide/narrow populated layouts, filtering,
no results, clearing search, source switching to an empty exported dataset and
restoring native data. Basic Tab focus and UI Automation names/values were checked.
The UIA helper used a keyboard fallback for one source selection; full screen-reader
and keyboard acceptance is still pending. Screenshots remain in ignored artifacts;
other themes/DPI, loading/database failure and eligible export checks remain in PLAN.md.
