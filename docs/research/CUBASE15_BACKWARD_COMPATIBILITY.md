# Cubase 15 inherited serializer compatibility

Investigated 2026-11-01 from the local IDA export. This is static reader evidence,
not a complete Cubase loader or a release-wide compatibility guarantee. Original
projects and the executable were not modified or executed for this investigation.

## Source and reproduction

The executable identified by the export has SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
The export header identifies AMD64 and image base `0x140000000`; prior executable
resource inspection reports Cubase 15.0.40. These addresses apply to that exact
binary. Line numbers below refer to the local 1,079,140,046-byte export snapshot;
procedure names and addresses are more stable anchors than line numbers.

Reproduce procedure inventories and optional local excerpts with the standard
library script:

```powershell
python scripts/inspect_cubase15_ida_export.py Cubase15.exe.asm `
  --function midi-event-registration --function midi-note-registration `
  --function archive-current-version --function archive-named-version `
  --function midi-event-reader --function midi-note-reader `
  --function event-attribute-reader --function event-attribute-item-reader `
  --function event-attribute-value-reader `
  --output artifacts/asm-backward-compatibility
```

The script checks the IDA header's input hash and reports each complete procedure's
byte offset, byte count and excerpt SHA-256. The header does not authenticate the
export itself. For independent executable-byte checks, use the existing
`scripts/inspect_cubase15_serializers.py` and PE address mapping. Keep the
executable, full disassembly, excerpts and private project reports untracked.
The updated inventory was run against this snapshot: all 39 registered procedure
targets had complete bounded excerpts, including the nine selections above.

## Named and active class revisions are different inputs

The constructor at `0x1445E9500` installs vtable `0x1465AA0B8`
(line 22,952,835). Its entries at lines 36,808,392–36,808,401 establish:

| Archive vtable entry | Procedure | Observed behavior |
| --- | --- | --- |
| `+0x28` | `0x1445E78A0` | Returns the active revision at archive object `+0x14` (lines 22,950,278–22,950,280). |
| `+0x30` | `0x1445E9E20` | Searches the archive's declaration entries for an exact class name, returns that entry's sign-extended 16-bit revision, and returns zero when no name matches (lines 22,953,668–22,953,740). |

Consequently, a derived object's final class revision cannot substitute for its
base class revision. Name lookup uses the archive's declaration list, so a base
declaration can precede the current object. Absence from the object's immediately
adjacent prefixes does not establish absence from the archive. Missing metadata
and an explicit revision zero both return zero inside Cubase, but the inspection
reader should preserve the distinction in its evidence. A bounded or incomplete
inventory cannot prove that a declaration is missing.

The sign extension also matters for malformed input: a stored `0x8000` is negative
in this getter, not a positive future revision. The inspector should retain raw
revision bits and reject unsupported layouts rather than emulate permissive
native behavior on arbitrary input.

## MMidiEvent and MMidiNote have independent revisions

Registration `0x14157FD50` declares `MMidiEvent` revision **2** at lines
4,603,986–4,604,003. Registration `0x14157FD90` declares `MMidiNote` revision
**1** and its base registration pointer at lines 4,604,008–4,604,025.

`MMidiNote` reader `0x141BB4690` first calls `MMidiEvent` reader `0x141BB0390`
(line 6,798,597). The base reader consumes these primitives:

| Sequence | Object destination | Wire width | Revision condition | ASM lines |
| --- | --- | --- | --- | --- |
| 1 | `+0x58` | 8-byte floating point | Always | 6,792,785–6,792,788 |
| 2 | `+0x60` | 1 byte | Always | 6,792,789–6,792,792 |
| 3 | temporary, then `+0x62` | 1 byte | Always | 6,792,793–6,792,799 |
| 4 | temporary, then conversion | 1 byte | Always | 6,792,800–6,792,807 |
| 5 | temporary, OR into `+0x30` | 2 bytes | Named `MMidiEvent` revision `>0` | 6,792,849–6,792,861 |
| 6 | attribute collection at `+0x28` | Variable | Named `MMidiEvent` revision `>1` | 6,792,863–6,792,871 |

The remaining base-reader work examines/converts the already read attributes and
checks archive status; it does not append another fixed wire primitive. The
attribute values can override or augment object values, so raw bytes alone do not
establish the final musical state.

After that base callback, the note reader consumes three consecutive 8-byte
floating point values into object `+0xB8`, `+0xC0` and `+0xC8` (lines
6,798,600–6,798,617). It explicitly asks for named `MMidiNote` revision and reads
one additional byte into object `+0x70` only when that revision is positive
(lines 6,798,618–6,798,628). Remaining operations examine already loaded
attributes; no further note-body primitive is read. The current note writer
`0x141BB49A0` mirrors the base callback, three values and final byte at lines
6,798,899–6,798,922.

The 8-byte helper `0x1439DFFF0` reads exactly eight bytes and conditionally reverses
their order according to the stream's byte-order flag (lines
18,186,755–18,186,803). Byte reader `0x1439DEFF0` reads one byte (lines
18,185,120–18,185,129); flags helper `0x1439DF250` reads two with the same
conditional swap pattern (lines 18,185,368–18,185,404). The callback evidence
therefore establishes widths and ordering. It does not, on its own, establish a
universal big-endian CPR rule. Existing bounded candidates use big-endian
interpretation supported by separately observed files and retain raw bytes.

### Bounded layout matrix

Here `B` is the separately declared `MMidiEvent` revision and `N` is the
`MMidiNote` revision. Offsets are relative to the note body. For base revision 2,
`n` is the bounded attribute count and **every** attribute must have the exact
supported scalar flag value 1 or 4 described below.

| `B` | `N` | Base portion | Three 8-byte values begin at | Final byte | Exact total bytes |
| --- | --- | --- | --- | --- | --- |
| 0 | 0 | `f64, u8, u8, u8` | 11 | Absent | 35 |
| 0 | 1 | Same | 11 | 35 | 36 |
| 1 | 0 | Previous plus `u16` at 11 | 13 | Absent | 37 |
| 1 | 1 | Same | 13 | 37 | 38 |
| 2 | 0 | Previous plus count at 13 and scalar attributes at 17 | `17 + 14n` | Absent | `41 + 14n` |
| 2 | 1 | Same | `17 + 14n` | `41 + 14n` | `42 + 14n` |

For example, the previously observed 112-byte modern body is only the
`B=2, N=1, n=5` case. A 111-byte body can be `B=2, N=0, n=5`; the note class's
revision alone cannot determine the base prefix. These are candidate byte shapes,
not proof that every combination was written by a released Cubase version.
Unknown, conflicting, incomplete or unsupported base declarations must not select
a legacy layout by matching body length alone. Legacy primitives remain anonymous
raw observations until independently compared with known-version Cubase state.

## Attribute stride is conditional on type flags

`0x141B74340` reads a 32-bit count at lines 6,705,699–6,705,704 and invokes each
allocated item's reader through its vtable `+0x90` at lines
6,705,771–6,705,775. The item vtable `0x145ED8C00` points that entry to
`0x144574E30` (lines 34,629,425–34,629,443). The item reader consumes a 4-byte
ID using `0x1439DF650`, then calls `0x1445749D0` for its typed value (lines
22,771,578–22,771,597).

The value reader reads two bytes of **type flags**, then dispatches by these bits
in this order (lines 22,771,170–22,771,336):

| First matching flag test | Following wire data |
| --- | --- |
| `0x0002` | Nested `CmObject` through archive object reader |
| `0x0100` | A container/helper path, not fixed scalar bytes |
| `0x0008` | Nested `CmString` through archive object reader |
| `0x0001` | Eight bytes through `0x1439DF880` |
| `0x0004` | Eight bytes through `0x1439DFFF0` |

An exact flag value **1** or **4** therefore permits the bounded
`4-byte ID + 2-byte flags + 8-byte scalar = 14-byte` shape. Flags are a bitmask,
not a closed enum: a value with bit 2 or bit 8 can select nested data, and a
generic `count × 14` skip would lose alignment. The existing printable-ASCII ID
check and count budget are conservative inspector restrictions, not native
grammar requirements. Unsupported attributes must retain raw evidence rather
than inventing a scalar value or silently skipping nested content.

## Other track callback gates

The MIDI track reader `0x141BCCFB0` has a subtle distinction that matters for
subclasses such as instrument tracks:

| Gate | Lookup kind | Additional behavior | ASM lines |
| --- | --- | --- | --- |
| Revision `>0` | Active revision, archive `+0x28` | Two 32-bit values | 6,836,375–6,836,394 |
| Revision `==2` | Named `MMidiTrackEvent`, archive `+0x30` | Sets an internal compatibility flag | 6,836,396–6,836,406 |
| Revision `>1` | Named `MMidiTrackEvent`, archive `+0x30` | One 32-bit value, one byte, and a nested archive containing `CmObject` only when that byte equals 1 | 6,836,408–6,836,461 |
| Revision `>=4` | Active revision, archive `+0x28` | Reads and applies nested `FAttributes` | 6,836,509–6,836,599 |

The first and last gates are not explicitly the named `MMidiTrackEvent` lookup.
Do not substitute one revision for both without tracking how the archive sets the
active revision while loading the actual derived object. The common base callback
and the embedded archive/object still require their own bounded grammars; these
gates do not establish fixed absolute offsets for a full track body.

The audio track reader `0x141E85B90` first handles a legacy conversion path when
an already read property is present (lines 7,851,979–7,852,223). Its ordinary path
calls the common track base reader and asks for the **active** revision through
archive `+0x28`; only revision `>0` reads a nested `MAutoFadeSetting` (lines
7,852,225–7,852,253). Revision zero omits that child. The property-driven
conversion path, inherited fields and fade object remain distinct research work.

## Compatibility boundary and next fixtures

The executable contains real backward-reading branches. None maps a per-class
revision to an application release. Prior read-only samples associate MIDI track
revision 3 with selected Cubase 10–13 projects, revision 4 with selected 14/15
projects, and note revision 1 with selected 11–15 files. Those observations do not
establish the `MMidiEvent` base revision in every release, or verify the legacy
matrix against visible Cubase values.

An additional bounded corpus pass read 80 private files of at most 8 MiB each,
94,396,634 bytes in total: 77 RIFF and three RIF2. Every source's SHA-256 was
unchanged before and after inspection. Saved-by text grouped 27 files before
Cubase 10: SX 1.03 (3), SX 2.0.1 (1), SX 3.0.2 (3), SX 3.1.1 (7), Cubase 6.x
(6), 7.x (5) and 8.x (2). Remaining groups were 10.x (9), 11.x (13), 12.x (8),
13.x (20) and 14.x (3). These are file metadata hints, not independent application
version verification.

The pass observed 67 explicit `MMidiEvent` revision-2 declarations and 67 explicit
`MMidiNote` revision-1 declarations, including 26 of each in the pre-10 sample.
It found no actual base revision 0/1 or note revision 0. All 26 pre-10 inline note
declarations had zero-length bodies and were catalog-like declarations; they do
not validate the note-body grammar. Pre-10 track-event declarations included
`MMidiTrackEvent` revision 2 (14) and revision 3 (12), plus
`MAudioTrackEvent` revision 1 (7) and `MAudioTrack` revision 0 (7).

The pre-change Release CLI built from commit `2520caf` and the updated Release
CLI both read 11 separate smallest-per-major samples with saved-by text SX 1.03,
SX 2.0.1, SX 3.0.2, Cubase 6.5.0, 7.0.6, 8.5.0, 10.5.20, 11.0.30, 12.0.40,
13.0.30 and 14.0.10. All source hashes remained unchanged. Chunk counts (8–14),
arrangement class counts, track candidate counts and MIDI candidate counts
matched before and after. The updated reports additionally retained 1,400 class
declarations across these files' archives. This establishes bounded structural
reach and regression coverage, not musical interpretation or legacy body-layout
validation. An earlier locally staged CLI predates `2520caf` and is not used as
the comparison baseline. Private paths, manifests, project bytes and reports
remain in ignored local artifacts.

## Implemented reader behavior and validation

Archive JSON and Markdown now retain class name, raw u16 revision, archive-relative
symbol offset, absolute declaration offset and the point at which a successful
header made the declaration available. This last offset differs from the physical
declaration offset when a later reference recovers an earlier skipped declaration.
Recoveries accept bounded earlier direct `-1`/`-2` declarations; they do not chase
reference chains, accept future/unread offsets or create additional object bodies.
Symbol updates and recognition points commit only after the referencing header's
body bounds validate. These are deliberately narrower rules than Cubase's seek
fallback, with unsupported targets left opaque.

Named `MMidiEvent` and `MMidiNote` revisions are each resolved once per archive.
Conflicting revisions stay unresolved. A declaration recognized after the current
body cannot supply that body's revision. Arrangement bodies outside the detailed
inventory's scanned prefix also remain without musical interpretation. Supported
older shapes expose only anonymous `body+offset` scalar observations, original
bits and source provenance; unsupported attributes and incomplete bodies expose
no such fields. Existing modern musical-field candidates require explicit base
revision 2 and note revision 1. Normalized CPR MIDI and native MIDI export remain
unavailable. The arrangement parser also now requires full length-prefixed names
after `-2`, matching the archive inventory rather than accepting the previously
unverified mixed-width prefix-reference alternative.

Validation on 2026-11-01:

- `./build.ps1 -Configuration Release` with `CPE_CPR_FIXTURE_DIR` set to the local
  controlled-fixture directory: **523 tests passed**, including both hash-pinned
  Cubase 15 projects. Five existing xUnit2031 analyzer advisories remain in other
  test files.
- Uno Desktop and CLI Release production builds: zero warnings and errors.
- RIFF/RIF2 synthetic regressions cover all six raw note shapes, nonfinite bits,
  missing/conflicting/later revision evidence, unknown revisions and attribute
  flags, malformed reference targets, lazy recognition and shared symbol budgets.
- The 11-file pre-change/updated comparison above passed; the 80-file declaration
  probe and every smoke-read source retained their original SHA-256 values.
- All 39 ASM extraction targets produced complete bounded procedure inventories.
  No original project was modified, native writer added, or plug-in executed.

The next independent fixtures should preserve class declaration order and exact
header/body offsets; include base revisions 0, 1 and 2; compare isolated note,
flags and attribute changes with Cubase/exported MIDI; and exercise RIFF and RIF2
independently of saved-by text. Malformed input needs explicit tests for truncated
bodies, duplicate/conflicting declarations, unsupported type flags and revision
bits, out-of-range references, exhausted scan budgets and nonfinite raw values.

No native writer, plug-in deactivation, reconstructed Cubase state, normalized
legacy MIDI, complete musical decoding or universal backward support follows
from this static evidence. See [the serializer analysis](CUBASE15_SERIALIZER_ANALYSIS.md)
and [PLAN.md](../../PLAN.md) for the remaining reader and Cubase round-trip gates.
