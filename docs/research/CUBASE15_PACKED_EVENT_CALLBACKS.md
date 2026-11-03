# Cubase 15 packed MIDI event callback boundaries

Investigated 2026-11-02. Static analysis of the user-provided Cubase 15.0.40
executable and IDA export; neither input was executed or modified. This extends
[the counted-stream analysis](CUBASE15_MIDI_STREAM_COMPATIBILITY.md). These are
wire-boundary observations, not verified musical values or release-wide support.

## Source and trace

The executable is 135,534,616 bytes with SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
The export is 1,079,140,046 bytes and identifies that hash in its header. The
executable hash was checked before mapping PE virtual addresses. Seven primary
vtable prefixes were mapped directly from its read-only data; the archive reader
and writer pointers are at `+0x90` and `+0x98`. Thirty-one selected registration,
factory, constructor and callback procedures had complete bounded excerpts in
the export. Local excerpts and their SHA-256 manifest remain under ignored
`artifacts/midi-event-extension/`.

Fifteen callback/helper ranges were also disassembled directly from the hashed
executable with Capstone 5.0.9. Their ordered direct `sub_...` calls matched the
export, including all newly used primitive-reader/writer chains and the shared
attribute helper. Machine-code SHA-256 values and range checks remain in the
local manifest. The score writer spans five adjacent `RUNTIME_FUNCTION` regions
(`0x1427EBC80`–`0x1427EBE55`); its first unwind entry covers only the prologue.
Unwind-region boundaries must not be mistaken for complete procedure boundaries.

The factory `0x141BB14B0` dispatches on exact byte values. The following chains
connect its constructors to named registrations; chord's registration factory
inlines the same primary-vtable setup as its ordinary constructor.

| Discriminator | Registered class / revision | Registration | Registered factory | Constructor |
| --- | --- | --- | --- | --- |
| `0xA0` | `MMidiPolyPressure`, 0 | `0x14157FE30` | `0x141BBE540` | `0x141BB5760` |
| `0xC0` | `MMidiProgramChange`, 0 | `0x14157FE80` | `0x141BBE580` | `0x141BB6170` |
| `0xF0` | `MMidiSysex`, 0 | `0x14157FED0` | `0x141BBE5B0` | `0x141BB70C0` |
| `0x70` | `MChordEvent`, 0 | `0x14157FC60` | `0x141BBE350` | `0x141BB7B10` |
| `0x40` | `MNoteChangeEvent`, 0 | `0x14157FF20` | `0x141BBE5E0` | `0x141BB9DE0` |
| `0x30` | `MScoreEvent`, 1 | `0x1416C4C10` | `0x1427EDDC0` | `0x1427EB240` |

| Class | Primary vtable | Reader `+0x90` | Writer `+0x98` |
| --- | --- | --- | --- |
| `MMidiPolyPressure` | `0x145F41F48` | `0x141BB5AE0` | `0x141BB5B30` |
| `MMidiProgramChange` | `0x145F42AC8` | `0x141BB0390` | `0x141BB05D0` |
| `MMidiSysex` | `0x145F43C08` | `0x141BB7580` | `0x141BB7600` |
| SysEx payload object | `0x145EF6370` | `0x141CF3610` | `0x141CF36B0` |
| `MChordEvent` | `0x145F40958` | `0x141BB7E00` | `0x141BB7E50` |
| `MNoteChangeEvent` | `0x145F44820` | `0x141BBA0E0` | `0x141BBA160` |
| `MScoreEvent` | `0x1460CEE80` | `0x1427EBB00` | `0x1427EBC80` |

## Exact supported subset

Let `n` be a bounded attribute count. With explicit named `MMidiEvent` revision
2, and every attribute having the supported scalar flag exactly 1 or 4, the
shared base occupies `P = 17 + 14*n` bytes. Object, string and container
attributes have different grammar. The subsequent
[nested-attribute extension](CUBASE15_NESTED_ATTRIBUTES.md) consumes two exact
object-valued combinations opaquely in counted streams; other nested cases
remain unsupported. The scalar-only widths below
include the leading event discriminator and apply to the current scalar subset.

| Event class | Callback wire reads after the shared base | Packed bytes |
| --- | --- | --- |
| Poly pressure | None | `18 + 14*n` |
| Program change | None | `18 + 14*n` |
| Chord | Note tail (three f64 and the revision-1 byte), then another u8 | `44 + 14*n` |
| Note change | u16, u32, f64 | `32 + 14*n` |
| SysEx | Two u32 fields, then `N` opaque bytes | `26 + 14*n + N` |

Program change's reader is the base `MMidiEvent` reader itself. Poly pressure's
reader calls that base and then `0x143161B60`, which receives the existing curve
and attribute collection rather than the archive. It looks up/removes loaded
attributes and builds or attaches in-memory curves; it does not consume an
additional wire field. This is the same attribute-conversion helper used by
the already supported controller/aftertouch/pitch-bend subset.

The chord reader explicitly calls `MMidiNote` reader `0x141BB4690`, then reads
one byte to native member `+0x110`; its writer mirrors those calls. Consequently
the named `MMidiNote` revision remains an independent layout dependency. The
current reader requires revision 1 and skips the chord body without treating it
as an ordinary note candidate. Note change calls the base, then reads u16 into
`+0xA0`, u32 into `+0xA4`, and f64 into `+0x68`; the matching writer establishes
the same widths/order. Their musical interpretation is not assigned here.

SysEx first calls the base reader and directly invokes the payload object's
reader through its vtable, without another class header. Constructor
`0x141BBBBF0` installs payload table `0x145EF6370`. The payload reader consumes
two u32 fields, allocates/reads the first field's byte count, and does not use
the second as a separate extent. Its writer emits the same length twice and
then the raw bytes. The inspection reader accepts only that duplicated-length
form, with both fields and the entire payload inside the current byte budget;
it stores no payload and performs no byte searching inside it. Differing counts,
oversized or incomplete payloads stop with explicit partial evidence. The native
SysEx writer also has a null-payload branch that emits one zero u32; this pass
does not establish a safe way to disambiguate that branch from the next packed
event, so a single-count form is not accepted as an empty payload.

## Unsupported score grammar and compatibility limits

Follow-up: the [bounded empty-score extension](SCORE_EVENT_BOUNDARIES_2026-11-02.md)
now handles a null data node and absent/zero inherited attributes with explicit
named revisions. The original unsupported full-grammar findings below remain
relevant for non-null objects and nonempty attributes.

`MScoreEvent` is not another `MMidiEvent` scalar packet. Its reader starts with
u16 and three f64 values, invokes the archive object reader for `MDataNode`,
queries the named `MEvent` revision for attributes, then consumes another f64
and two bytes. It also has an active-revision compatibility conversion. Its
object/reference and inherited-attribute grammar is not decoded, so `0x30`
still stops the counted list with the preceding notes retained as partial.

The reader accepts the new skip boundaries only with a supported, unambiguous
revision-0 declaration recognized before the event, the existing part v1/v2
grammar, named base v2/note v1 context, and the bounded scalar attributes.
Missing/conflicting/later declarations, unsupported attributes and payloads do
not authorize resynchronization past an unknown body. These checks intentionally
support a narrower subset than Cubase's callbacks. Per-class revisions are not
Cubase release numbers. Genuine mixed-event and historical projects still need
field-by-field comparisons with Cubase before normalizing non-note events,
musical timebase, part placement, ownership or asserting full compatibility.

## Reproduction

```powershell
python scripts/inspect_cubase15_serializers.py E:\Cubase15.exe `
  --vtable midi-poly-pressure --vtable midi-program-change `
  --vtable midi-sysex --vtable midi-sysex-payload --vtable midi-chord `
  --vtable midi-note-change --vtable midi-score

# Optional machine-code cross-check, using an analysis-only dependency:
python -m venv artifacts/cubase-static-env
artifacts/cubase-static-env/Scripts/python.exe -m pip install capstone==5.0.9
artifacts/cubase-static-env/Scripts/python.exe scripts/inspect_cubase15_serializers.py E:\Cubase15.exe `
  --function midi-poly-pressure-reader --function midi-sysex-payload-reader `
  --function midi-chord-reader --function midi-note-change-reader `
  --function midi-score-writer --disassemble

python scripts/inspect_cubase15_ida_export.py E:\Cubase15.exe.asm `
  --function midi-poly-pressure-registration --function midi-poly-pressure-factory `
  --function midi-poly-pressure-constructor --function midi-poly-pressure-reader `
  --function midi-poly-pressure-writer --function midi-program-change-registration `
  --function midi-program-change-factory --function midi-program-change-constructor `
  --function midi-event-reader --function midi-event-writer `
  --function midi-sysex-reader --function midi-sysex-writer `
  --function midi-sysex-payload-constructor --function midi-sysex-payload-reader `
  --function midi-sysex-payload-writer --function midi-chord-reader `
  --function midi-chord-writer --function midi-note-change-reader `
  --function midi-note-change-writer --function midi-score-reader `
  --function midi-score-writer --output artifacts/midi-event-extension
```

Synthetic RIFF/RIF2 regression cases verify exact skipping with zero/integer/
floating scalar attributes, note-like bytes inside opaque payloads, earlier and
conflicting class revisions, empty SysEx, differing/huge length fields and byte
budget exhaustion. A malformed event count or trailer after a recognized SysEx
also rejects the counted-list prefix explicitly; it cannot reactivate the
heuristic scanner inside an already skipped payload. These cases validate
reader mechanics, not Cubase musical semantics.

## Validation checkpoint

All **595 Release tests** pass. CLI and Uno Desktop Release builds and the
self-contained Windows x64 app publish pass; five existing xUnit2031 advisories
remain in unrelated tests. No UI behavior was changed by this callback batch.

A deterministic, bounded sample selected 80 local CPR files from 32 KiB to
8 MiB. The same files were inspected with the previous and updated CLI builds;
per-file paths, versions and reports remain only in ignored local artifacts.

| Metric | Before | After |
| --- | ---: | ---: |
| Successful structural reads | 80 | 80 |
| Candidate notes | 116,307 | 116,567 |
| Counted-stream candidates | 116,305 | 116,565 |
| Files with `Arrangement.Partial` | 2 | 0 |
| First unsupported part discriminators | 14 x `0xA0`, 1 x `0xF0` | None in this sample |

One file gained 260 candidate notes; no file lost candidates. All 80 SHA-256
values matched before/after each read and across both phases. Removing an
arrangement-level limitation means the selected structural grammar was traversed;
it does not mean all project fields or Cubase musical state were loaded. This
sample is not a verified release/edition compatibility matrix.
