# Cubase 15 MIDI part stream compatibility

Investigated 2026-11-01 from the local static IDA export. This extends
[the inherited revision investigation](CUBASE15_BACKWARD_COMPATIBILITY.md).
It describes bounded wire grammar, not a complete loader, verified musical
timing, track ownership or release-wide compatibility.

## Input and reproduction

The export identifies executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`,
image base `0x140000000`, previously inspected as Cubase 15.0.40.
Addresses apply only to that executable. Line numbers refer to the local
1,079,140,046-byte export snapshot; procedure names are more stable anchors.

```powershell
python scripts/inspect_cubase15_ida_export.py Cubase15.exe.asm `
  --function midi-part-registration --function midi-part-factory `
  --function midi-part-reader --function midi-part-writer `
  --function midi-packed-event-factory --function part-eventlist-legacy-reader `
  --function part-base-reader --function part-timebase-reader `
  --function part-name-reader --function archive-object-reader `
  --function archive-declaration-reader --function archive-declaration-writer `
  --output artifacts/midi-stream-asm
```

The script checks the input hash declared in the export header and reports
bounded procedure offsets, lengths and SHA-256 values. The header does not
authenticate the export itself. Original projects and executable were not
modified or executed. Keep full ASM, procedure excerpts and private project
reports untracked.

## Registration and callback chain

| Step | Address | Observed evidence |
| --- | --- | --- |
| `MMidiPart` registration | `0x141580CA0` | Revision 2, factory `0x141BCA170`, base registration pointer `0x14776B970` (ASM line 4,605,327 onward). |
| Factory | `0x141BCA170` | Allocates object, calls base constructor `0x1431C0C00`, installs primary vtable `0x145F45E78` (line 6,831,857 onward). |
| Vtable reader/writer | `0x145F45E78` | Entries `+0x90` and `+0x98` are `0x141BC6E90` and `0x141BC7070` (line 34,688,845 onward). |
| Part reader | `0x141BC6E90` | Selects old object-list callback or discriminator stream, then attaches loaded events (line 6,826,796 onward). |
| Packed event factory | `0x141BB14B0` | Exact discriminator switch; `0x80` and `0x90` construct the same `MMidiNote` class (lines 6,794,173 and 6,794,194 onward). |
| Note constructor | `0x141BB3BA0` | Installs primary vtable `0x145F41908`; its reader is the previously traced `0x141BB4690`. |

The part reader checks in-memory object `+0x50` bit 11 first. When that bit is
clear and the archive's **named** `MMidiPart` revision is below 1, it delegates
to `0x1431BDE30`. Otherwise it takes the discriminator-stream path.
Revisions 1 and 2 share that path in this binary. An inspector cannot infer
the in-memory bit from body length or saved-by text; unknown part revisions
must remain distinct from an explicitly supported stream shape.

The older callback calls the same common part-base reader, reads a 32-bit
event count and then invokes the archive object reader expecting `MEvent` for
each event. It does **not** consume the discriminator/declaration preamble.
This is an independent older representation, not a fixed 43-byte alternative.

## Common part prefix and event-list boundary

Both paths first call `0x143192630` (ASM line 15,056,413 onward). That callback
reads a `CmString` through `0x14458ACF0` / `0x14458AAC0`, then a separate value
at object `+0x38`. The string reader (line 22,806,164 onward) consumes a 32-bit
byte length followed by that many raw bytes; it recognizes a UTF-8 BOM at the
beginning or end and normalizes termination in memory. The native helper also
has permissive legacy-string behavior; strict UTF-8/NUL handling in the
inspector remains a narrower supported subset.

The subsequent value reader consults feature flags. The following boundaries
are directly visible; the numeric tag's musical meaning is unverified:

| Wire tag | Following bytes | Evidence / limit |
| --- | --- | --- |
| 1 through 15 | One 8-byte floating-point primitive | Both `0x143192630` feature-flag branches agree. |
| 0 or `0x11` | Two archive object reads, or two raw 8-byte values on an alternate feature-flag path | `0x143438E50` (line 16,073,544 onward) calls archive `+0x20` twice; `0x143192630` has an alternate raw-width path. The feature context must be known to select the representation. |
| Other tags | Feature-dependent | `0x143438E50` can coerce tags at least `0x10` to the object path; no general fixed skip is established. |

For the modern part path, after that common prefix the callback consumes:

1. A 32-bit event count; zero returns successfully without a declaration-count
   byte or any events.
2. For nonzero count, one signed byte containing the number of class
   declarations, followed by that many archive `+0x48` calls when positive.
3. Exactly the saved number of events, each beginning with one byte discriminator.
4. No additional wire fields after the event loop in this callback.

The declaration reader at archive `+0x48` is `0x1445EA6A0`
(ASM line 22,954,402 onward), which calls the class-header reader
`0x1445EA890`. The corresponding writer at `+0x40` is `0x1445EA650`
(line 22,954,364 onward): it writes the ordinary class declaration/reference
grammar, then a **mode-width zero body length**. This explains zero-body
`MMidiEvent`, `MMidiNote` and other class records in the beginning of a part:
they can establish serializer revisions before any packed event is read.
An empty declaration is not itself a MIDI note.

### Object reference boundaries in the common prefix

The archive object reader is `0x1445E9990`. It first calls the existing class
reader, whose leading token has the container's width: four bytes for RIFF,
eight for RIF2. A zero token is null. A positive token selects an existing
object from archive table `+0x50`; it does not consume another body length.
New object/class tokens require their own complete bounded grammar.

At `0x1445E9C33`, a new object is inserted in that table with an identifier
computed as current stream position minus the mode width minus archive base:
the archive-relative offset of its body-size field. That is different from
the class-symbol declaration offset. A positive number alone does not prove
that its target exists or has the expected interface. Keep unresolved reference
tokens as structural evidence rather than inventing a timebase or graph edge.

In RIFF, two existing-object/null tokens therefore occupy eight bytes. In
RIF2 they occupy sixteen bytes. The alternate feature-flag path reads sixteen
raw bytes regardless of container mode. These paths cannot be selected merely
from a desired event-count alignment.

## Packed event discriminator and note width

After reading each discriminator, part callback `0x141BC6E90` branches:

- Zero: calls archive object reader `+0x20` expecting `MEvent`.
- Nonzero: calls `0x141BB14B0`, then that object's reader at vtable `+0x90`
  directly, without a per-event class header or body length.

The factory accepts **exactly** `0x80` and `0x90` for `MMidiNote`. It does not
mask a MIDI channel nibble: `0x91` through `0x9F` take the default unsupported
branch. Channel is separately consumed by the note's base callback. Other
exact discriminator values construct other event types whose widths must be
traced before traversing a mixed stream. A reader must not search past an
unsupported event for a plausible later note and call that a complete list.

The direct note callback is the same `0x141BB4690` already traced for class-body
notes. It calls the `MMidiEvent` reader, asks for the archive's named
`MMidiNote` revision and reads the revision-1 terminal byte. The leading
discriminator does not replace that terminal byte.

For explicit `MMidiEvent` revision 2 and `MMidiNote` revision 1, with every
attribute in the supported scalar subset, a packed note occupies:

`1 discriminator + 17 base-prefix bytes + 14 × attribute-count + 25 tail bytes`

That is **43 bytes when attribute count is zero**, 57 with one attribute,
113 with five attributes, and so on. Zero attributes are allowed by the native
attribute-count reader. The prior heuristic packed scan required at least one
attribute and advanced over only 24 tail bytes, which excluded this 43-byte
shape and left the terminal byte to subsequent byte searching.

This establishes a concrete route to inspecting old projects without pretending
that zero attributes imply an older base revision. The complete part prefix,
declaration context, event count, exact event widths and containing body limit
must all agree before a zero-attribute note stream is accepted. A body-size
match or run of 43-byte patterns alone is insufficient. MIDI timing units,
effective values after attribute conversion and ownership still require
independent Cubase comparison.

## Read-only pre-10 corpus cross-check

A bounded follow-up inspected 27 previously hashed pre-10 files and retained
private paths and bytes only in ignored local artifacts. All 27 source SHA-256
values matched before and after the read. It found 1,720 candidate `MMidiPart`
revision-1/2 records. Of those, 1,563 nonempty note-only streams traversed their
declared event count and ended exactly at the containing body boundary, yielding
27,774 packet observations. The selected wire grammar uses the actual named
base/note revisions and bounded zero-body class declarations, rather than
accepting arbitrary runs at a fixed stride.

| Saved-by family | Complete note-only part lists | Packet observations |
| --- | ---: | ---: |
| Cubase SX 1 | 566 | 4,818 |
| Cubase SX 2 | 63 | 600 |
| Cubase SX 3 | 526 | 8,695 |
| Cubase 6 | 336 | 9,003 |
| Cubase 7 | 30 | 4,278 |
| Cubase 8 | 42 | 380 |

There were **14,113 zero-attribute 43-byte packets across 14 SX files**. Their
part bodies use the existing `MMidiEvent` revision-2 / `MMidiNote` revision-1
grammar; this is evidence against treating 43-byte packets as intrinsically
older base-class revisions. In the examined kind-0 prefixes, positive object
tokens resolve structurally to earlier `MTempoTrackEvent` and
`MSignatureTrackEvent` records using the body's size-field offset. This supports
the object-reference branch for those files without establishing the references'
musical timebase or effective project state.

Another 152 lists stopped at their first unsupported event discriminator; 561
preceding packet observations were retained separately as partial evidence.
Five record candidates had unsupported/invalid prefixes. Four have 20-byte
bodies and one has 279 bytes; they may be incidental class-reference matches
inside opaque bytes rather than actual parts. These are probe outcomes, not counts of verified Cubase
parts, tracks or notes. Per-field comparison with Cubase is still pending.

### Mixed streams: bounded scalar event shapes

A follow-up traced the three most frequent non-note discriminators in the
partial sample. The exact factory cases, constructor-installed primary vtables
and reader slots establish these chains:

| Discriminator | Class / registered revision | Constructor | Primary vtable | Reader at `+0x90` |
| --- | --- | --- | --- | --- |
| `0xB0` | `MMidiController`, 0 (`0x14157FD00`) | `0x141BB63A0` | `0x145F43088` | `0x141BB68B0` |
| `0xD0` | `MMidiAfterTouch`, 0 (`0x14157FCB0`) | `0x141BB5D20` | `0x145F42508` | `0x141BB5FF0` |
| `0xE0` | `MMidiPitchBend`, 0 (`0x14157FDE0`) | `0x141BB6B90` | `0x145F43648` | `0x141BB6F40` |

All three readers call `MMidiEvent` reader `0x141BB0390`, then use
`0x143161B60` on their already loaded attribute collection and check archive
status. That helper receives the curve object, attribute collection and a
flag, not an archive stream. It looks up/removes attributes and builds or
attaches in-memory curve objects. Controller reader additionally calls
`0x141BB6AC0`, which again looks up/removes a loaded attribute. None of these
callbacks consumes an additional wire primitive or a note's 25-byte suffix.

For explicit base revision 2 and supported scalar attributes only, each of
these packed events therefore occupies **`18 + 14 × attribute-count` bytes**,
including the discriminator. A zero-attribute event occupies 18 bytes.
Their own callbacks do not query a derived class revision; retaining the
observed revision-0 declaration is a conservative inspection boundary.
Unknown attribute types still prevent determining a scalar-only stride.

These shapes can permit bounded traversal through mixed streams to later note
observations without assigning controller, pitch bend, pressure or automation
semantics to the skipped bytes. They do not authorize seeking past other
unknown discriminator values. The initial partial-list counts above describe
the note-only probe before these additional callback shapes were traced.

## Implemented reader and validation

`CprMidiPartEventStreamReader` accepts the supported counted grammar before
enabling zero-attribute observations. It requires named part revisions 1/2,
base revision 2 and note revision 1, recognized before the events; exact
zero-body declaration boundaries; and exact event-count/body-end agreement.
Tags 1–15 use the eight-byte scalar boundary. Tags 0/17 require two null or
positive mode-width references to earlier nonempty inventoried object bodies.
Zero-body class-only declarations cannot establish an object-table entry.
No timebase, effective note state or owner relationship is inferred from these
references. Unsupported feature-dependent prefixes remain opaque.

Exact `0x80`/`0x90` packets produce note candidates. Supported `0xB0`/`0xD0`/`0xE0`
packets are skipped by their verified scalar extent when their revision-0 class
declarations are available. The [2026-11-02 callback extension](CUBASE15_PACKED_EVENT_CALLBACKS.md)
adds bounded skipping for `0xA0`/`0xC0` base-only events, `0x70` chord and `0x40`
note-change suffixes, and duplicated-length `0xF0` SysEx payloads under their own
revision-0 gates. Score events (`0x30`) remain unsupported. A zero discriminator can skip a bounded class object
or a supported existing-object/null token; typed note bodies are still inspected
once through arrangement traversal. Unsupported attributes/discriminators or
malformed complete lists do not permit searching forward for zero-attribute
notes. Existing bounded weak scanning remains available for attributed patterns
and has a distinct `SourceLayout`; its exact discriminators and complete 25-byte
suffix were corrected as well. Counted streams identify themselves as
`RIFF MMidiPart/counted-events` or the matching RIF2 label.

The reader shares existing 4 MiB per-part / 32 MiB aggregate byte budgets and
10,000-note limit. A budget boundary can retain a verified prefix with an explicit
partial result; 10,000 notes ending the declared list complete without a false
partial flag. Cancellation uses iteration counts rather than relying on byte
alignment. Inventory and arrangement now share one transactional class-header
reader, including its declaration availability and symbol-budget rules.

The shared class reader remains an inspection subset. In particular, its lazy
`-2` recovery identifies the exact target base declaration; Cubase's native
missing-table fallback recursively walks the declaration chain. They are not
claimed to emulate identical object state. Full object-table reconstruction and
native fallback equivalence remain separate validation work.

The additional callbacks preserve earlier notes and return an explicit partial
result when an event has unsupported class metadata, attributes or SysEx bounds;
no part-level heuristic scan resumes inside that payload. New synthetic tests
and a paired 80-file local sample are recorded in the callback-extension report.
The following 2026-11-01 results remain the historical checkpoint for the earlier
subset.

Final checks on 2026-11-01:

- **559 Release tests passed**, including the two hash-pinned controlled Cubase
  15 files and RIFF/RIF2 counted-stream, mixed-event, object-reference, revision,
  malformed-input and exact/partial note-budget regressions.
- Uno Desktop and CLI Release production builds passed with zero warnings/errors.
  Five existing xUnit2031 advisories remain in unrelated test files.
- The updated CLI passed read-only verification on **27 pre-10 files**: all
  **27,774 independently anchored packets** matched exact source offsets, raw
  fields and candidate part context, with zero missing packets, duplicates or
  field mismatches. All source SHA-256 values remained unchanged.
- The reader returned **30,781 counted observations**, including **15,737
  zero-attribute packets in SX files**. The extra 3,007 counted observations come
  from mixed streams outside the initial note-only oracle. One file additionally
  retains 1,017 weak-scan observations; they remain distinguishable by layout.
- Selected Cubase 10.0.60, 11.0.30, 13.0.40 and 14.0.40 files retained the same
  **5,651 candidate offsets and raw fields** as the pre-stream reader. Their
  source hashes also remained unchanged.
- All **62** current ASM extraction targets produced complete bounded procedure
  inventories. Executable bytes, full ASM, private paths and corpus reports remain
  untracked.

## Validation still needed

Compare controlled note lists against Cubase and exported MIDI, including
zero events, zero attributes, both exact note discriminators, mixed event
types, flags, declaration reuse and both container widths. Keep source hashes
and test malformed declarations, missing/conflicting revisions, truncated
terminal bytes, event-count mismatches and scan budgets. Genuine SX observations
can validate that a wire shape occurs; they do not establish musical semantics
or complete SX support. No source project or executable should be modified.
