# Cubase 15 C-export follow-up: audio routing

Investigated 2026-11-04 from the local Ghidra C export. This extends the
[earlier routing observations](CUBASE15_SERIALIZER_ANALYSIS.md#native-routing-display-from-typed-field-observations).
The results remain bounded, read-only candidate evidence. Stored connections
do not establish current hardware availability or a complete Cubase signal graph.

## Input and callback evidence

The input is the same 590,827,789-byte C snapshot documented in the
[mixed MIDI investigation](CUBASE15_C_MIXED_MIDI.md), SHA-256
`35b8643f6f5211cc4e5bf0ccd5efd5f7541f9cc289b09d28a4f9dd1d0a444cf8`.
Addresses refer to the associated, previously hash-checked executable
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
The export is decompiler evidence, with incomplete recovered signatures;
its text hash does not authenticate its derivation from that executable.

```powershell
python scripts/inspect_cubase15_c_export.py D:\ReverseEng\Cubase15.exe.c `
  --function audio-routing-inventory --function audio-routing-slot-inventory `
  --function audio-bus-selection-reader --function audio-bus-selection-writer `
  --function audio-channel-reader --function audio-channel-writer `
  --function audio-child-bus-reader --function audio-bus-parent-reader `
  --function audio-bus-writer --output artifacts/routing-2026-11-04/callbacks
```

The extractor now handles multiline function signatures, needed for the slot
inventory callback. It still rejects unknown snapshot hashes and bounds each
definition to 64 KiB. Full excerpts and private reports remain ignored locally.

| Callback | Observed behavior |
| --- | --- |
| `0x143513190` | Fetches `InputBusValue` and `OutputBusValue` objects, reads their selections through `0x14355FB90`, and adds only nonzero values to its connection inventory. Also reads own/recorder buses and inventories send/direct-routing folders. |
| `0x14355FB90` | Reads the integer property `Value` from a selection object. |
| `0x14355FBB0` | Writes the integer property `Value`, mirroring that boundary. |
| `0x143518A00` | Iterates a folder's slots, fetches each `Output`, reads its `Value`, and excludes zero destinations. Called separately for `SendFolder` and `directRoutingFolder`. |
| `0x14356E550` / `0x14356EC30` | Channel save/load paths use `InputBusValue`, `OutputBusValue`, `OwnInputBus`, `OutputBus`, `RecorderBus`, `SendFolder`, `foldbackSendFolder`, `directRoutingFolder` and `outputBusEnable`. |
| `0x14370F880` | Traverses named child-bus objects and their saved UIDs. |
| `0x14370FFF0` / `0x143710170` | Read/write `Parent Bus UID`; the writer also records `Bus UID`, `Bus Type` and arrangements. |

These callbacks establish a useful omission in the inspector: the nested
`InputBusValue/Value` and `OutputBusValue/Value` representation was discarded
by the mixer-field filter and not accepted by the routing projection. The
existing synthetic scalar forms alone did not test this native shape.

## Reader changes

- Retain nested selections and project their exact nonzero typed values into
  candidate input/output connections. Other fields, such as `AnchorValue`,
  cannot supply a destination merely because they contain a number.
- Retain raw `outputBusEnable` and `foldbackSendFolder` values for inspection.
  No enable mask, cue connection, level, tap position or physical endpoint is
  inferred from those fields in this batch.
- Leave zero selections visible in stored fields, without producing routing
  edges to a fabricated bus-zero endpoint. Conflicting duplicate property paths
  cannot produce edges; identical duplicates use the first source observation.
- Attach the containing ARCH payload's absolute offset to named records and
  normalized mixer observations. Match buses using that offset, stream name and
  UID. Repeated stream names cannot join different archives. Unresolved IDs
  also include archive scope, keeping unrelated endpoints separate.
- Keep the existing stream-only behavior for older saved observations without
  archive provenance. They do not join newly scoped records. Analyze the original
  project read-only to refresh those observations; no database migration or
  source-project write is performed by this batch.
- Preserve each index in class-instance arrays, including null-slot gaps.
  Previously their child fields were flattened into one shared path. Destination
  paths now retain `Slot[1]`, `Slot[2]`, etc. Class metadata words remain opaque.
- Accept the scanner's actual `Child Bus[0]/Bus UID` path alongside the older
  slash-separated candidate form. Child membership remains a separate edge;
  it does not establish processing order or reconstruct unknown ownership.
- Point scalar-array source offsets at the stored value's beginning. A string
  array points at its length prefix; an integer array points at its eight bytes.
  Previously both pointed after the value. Class-array class-name offsets also
  point at their own length prefix.
- Prioritize explicit destination fields, bus descriptors and saved connection
  controls, then insert/EQ fields, ahead of bulky send/cue panner settings within
  the existing 512-field normalized mixer limit. Raw named-record decoding keeps
  its existing 1,024-leaf, 4,096-node, depth and byte limits.

No native source project, executable, plug-in state or database is modified.
No routing relationship is established by a matching channel name.

## Parser optimization

The scanner previously searched the full remaining archive twice before every
top-level property, only to determine whether the next record started at the
current position. It now tests the two bounded anchor prefixes at that position.
The separate search used to locate the next candidate anchor is unchanged.

A local synthetic probe used a 32 MiB archive buffer with one scalar name,
900 integer properties and an opaque zero-filled remainder. Three direct scanner
runs returned the same 901 values. Before: **4,011 / 3,784 / 3,693 ms**.
After: **12.8 / 9.7 / 11.3 ms**. This isolates removal of repeated suffix scans;
it is not an end-to-end application performance measurement.

## Validation

The full suite passed **599 Release tests**, including the two hash-pinned
controlled Cubase 15 files. Five existing xUnit2031 advisories remain in unrelated
tests. The new RIFF/RIF2 coverage includes nested selections, source archive
scope, zero IDs, duplicate properties, null class-array slots, source offsets,
truncated arrays and large panner blocks competing with insert metadata.
CLI and Uno Desktop Release production builds passed with zero warnings/errors.

Read-only before/after checks covered **33 selected CPR/BAK files** with saved-by
hints spanning Cubase 10–15, including the two controlled Cubase 15 fixtures.
All source SHA-256 values were unchanged during each inspection. Both readers
returned **207 mixer observations**. The old projection had **2,112 edges**:
**1,986 zero-destination slots** plus **126 nonzero connections**. The updated
projection retains those 126 connections and adds **147 nested output** and
**30 nested input** observations, for **303 nonzero connections**.

The added selection values were checked against their original integer bytes
at the reported `Value` record offsets. Every new source-archive offset points
at an ARCH payload, and all joins between resolved mixer records use the same
archive offset and stream. The bounded selection retained 368 input/output
selection fields, 423 output-enable fields and 17,319 cue-folder fields. Insert
fields increased from 7,430 to 10,111; EQ fields from 717 to 3,297. These are
stored field observations, not counts of verified inserts, EQ bands or cue
connections. Private paths and complete reports remain local.

All 15 selected audio targets (including the existing audio-track targets)
produce bounded C excerpts. The complete 88-target inventory finds 87 definitions;
the archive-current-version name remains absent in this export. Extractor checks
covered multiline signatures, calls/prototypes, absent labels, hash rejection
and oversized definitions.

The projection feeds Studio map and routing tables without introducing a new
control or layout. Following the repository UI skills, populated/empty/error
screenshots, narrow/wide layouts, 100–200% DPI, light/dark/high-contrast themes
and keyboard/UI Automation acceptance remain **pending**. Model tests and builds
do not constitute visual approval.

Genuine controlled projects and paired exports are still needed to verify
per-release routing semantics, cue-send behavior, enabled states, sidechains,
speaker/channel mappings and effective playback connections.
