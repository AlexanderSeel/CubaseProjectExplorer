# MScoreEvent: bounded empty-value wire support

Date: 2026-11-02. Read-only continuation of the user-provided Cubase 15 ASM export
and demo corpus. No source project, plug-in or executable was modified/executed.

## ASM findings

Six complete procedures were extracted with `inspect_cubase15_ida_export.py` into
ignored `artifacts/ui-consistency/asm/`; offsets and hashes are in
`asm-manifest.json`. The export header identifies executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
`E:\Cubase15.exe` remains absent, so no new machine-code verification is claimed.

| Procedure | Address | Observed role |
| --- | --- | --- |
| Score registration | `0x1416C4C10` | Registers `MScoreEvent` at revision 1 |
| Score reader | `0x1427EBB00` | Distinct flags/scalars/object/inherited-attributes/suffix grammar |
| Score writer | `0x1427EBC80` | Corresponding flags/scalars, null object, attribute count/items and suffix |
| Post-attribute reader helper | `0x141BB2C10` | Attribute/articulation state lookups and conversions; no additional archive reads observed in this excerpt |
| Pre-write state helper | `0x141BB2E10` | Prepares attribute state before serialization |
| Post-write state helper | `0x141BB2EB0` | Restores/updates attribute state after serialization |

The score reader does not call the `MMidiEvent` base callback. It reads u16
flags, three f64 values, an archive object requested as `MDataNode`, and queries
the **named `MEvent` revision**. Positive revisions call the existing attribute
list reader. After the state helper, it reads one f64 and two bytes. A later
active-revision gate below 1 performs compatibility conversion; no final score
or articulation semantics are reconstructed here.

The writer supplies a null object to the archive object writer and writes a
filtered attribute count/list. This corroborates the empty/null subset without
requiring a speculative `MDataNode` body codec. Existing archive evidence
establishes mode-width null tokens; see [the nested-attribute report](CUBASE15_NESTED_ATTRIBUTES.md).

## Implemented subset

Counted `MMidiPart` streams can consume discriminator `0x30` only when:

- An unambiguous named `MScoreEvent` revision **1** is available before the event.
- An unambiguous named `MEvent` revision **0 or 1** is available before it.
- The `MDataNode` is null or an exact, bounded class-0 `MDataNode` object with
  a valid earlier reference, using the archive's 4- or 8-byte width. A complete
  object body is skipped opaquely; its contents are not decoded.
- With `MEvent` revision 1, its explicit u32 attribute count is zero.
- The entire prefix and ten-byte suffix fit inside the bounded part scan.

| Wire field | Bytes |
| --- | ---: |
| Flags | 2 |
| Three opaque f64 values | 24 |
| Null data node | 4 (RIFF) or 8 (RIF2) |
| Attribute count for named MEvent v1 | 4; absent for v0 |
| Opaque f64 and two bytes | 10 |

For null nodes, the body is 40/44 bytes with `MEvent` v0 or 44/48 bytes with
v1, excluding the discriminator. A framed MDataNode adds its exact archive
header and declared body extent. No MIDI-note position/channel/velocity layout
is applied to score bytes. The reader resumes at the next exact event boundary;
the whole counted stream must still satisfy its count and ending checks.

Other node classes/revisions, unresolved or forward references, nonzero
attribute counts, missing/conflicting/later revision evidence, unknown revisions
and truncation produce an explicit partial stop. The preceding candidates can
remain as partial evidence; the reader never searches an unsupported value for
a later plausible event.

This is wire-boundary support. It does not return normalized score events,
decode MDataNode bodies, apply attributes, or return notation, expressions,
articulation targets or historical-version guarantees.

## Exact MDataNode boundary follow-up (2026-11-03)

The user-provided `E:\ReverseEng\Cubase15.exe.c` decompile was read as untrusted
text and not executed. Its SHA-256 is
`35b8643f6f5211cc4e5bf0ccd5efd5f7541f9cc289b09d28a4f9dd1d0a444cf8`. At
`FUN_1427ebb00`, the score callback requests an archive object as `MDataNode`,
then queries the **named** `MEvent` revision before the inherited attributes and
suffix. The app now accepts null nodes and exact class-0 `MDataNode` objects or
valid earlier references whose framed body boundary is already bounded by the
archive scanner. Object contents are skipped without interpretation. Unknown
classes/revisions and unresolved/forward references still stop the event list.

Synthetic RIFF and RIF2 tests verify that an opaque object containing note-like
bytes cannot produce notes and that a following actual note remains at its exact
boundary. This does not validate derived MDataNode classes or score semantics
against a Cubase round trip. The remaining MScoreEvent work stays open in
`PLAN.md`.

Release validation after this follow-up: **665 tests passed, 2 optional private
fixture tests skipped**; CLI Release build passed with no warnings; Uno Desktop
Release self-contained `win-x64` publish passed. No UI behavior changed, and live
visual acceptance was not part of this reader change.
The separate modern note/base requirements for the containing counted stream
remain unchanged.

## Tests, corpus and test retirement

Eight new cases cover both widths and both supported named base revisions,
following-note offsets, untouched opaque scalar bits, missing/conflicting/future
declarations, unknown revisions, non-null data nodes, nonzero attributes and
truncation. Existing unsupported-event and no-resynchronization tests remain.

Five more obsolete presentation tests and their dead helpers were removed:
`RoutingDiagramTests` (3) and `CprIoObservationPresentationTests` (2). Production
reference searches found no callers of their old diagram builder or debug-text
formatter. Current routing uses `RoutingFocusService` and
`CprSignalFlowPresentation`; saved I/O uses `StudioIoDiagramBuilder` and
`ProjectIoAssignments`. Their evidence, ambiguity, cycle and endpoint tests were
retained. This extends [the earlier relevance audit](TYPED_NOTE_ATTRIBUTES_TEST_AUDIT_2026-11-02.md).

- Release: **639 passed, 2 private-fixture skips, 641 total**; no analyzer warnings.
- Self-contained Windows CLI and isolated Uno Desktop publishes pass.
- Both CLI inspections were rerun over all 21 demo entries: 13 genuine CPR/BAK
  files remain readable, eight AppleDouble files remain rejected, and every
  source hash matches before/after and the retained baseline.
- All **28,564 note candidates** retain their earlier values, flags/channel and
  track/part context. No prior candidates were added, removed or changed.
- The new score subset is tested with independent synthetic wire fixtures. The
  demo comparison does not establish genuine score semantics or a Cubase round trip.

Remaining reader work includes non-null score objects, inherited score attributes,
other event/attribute grammars, automation targets, bus identity, musical timing
and independently verified ownership. See [PLAN.md](../../PLAN.md).
