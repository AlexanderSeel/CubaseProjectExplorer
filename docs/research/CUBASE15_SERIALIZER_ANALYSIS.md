# Cubase 15 serializer analysis: framing, class versions and raw fields

Date: 2026-10-31. This extends [the initial executable findings](CUBASE15_EXECUTABLE_FINDINGS.md) with a further read-only static pass. Cubase was not executed, debugged, patched or unpacked. No project or executable was changed. Addresses apply to this exact executable only.

| Input property | Observed value |
|---|---|
| Local input | User-provided `Cubase15.exe` |
| Bytes | 135,534,616 |
| SHA-256 | `b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0` |
| Windows version resource | File `15.0.40.324`, product `15.0.40` |
| PE image base | `0x140000000` |
| Analysis | PE section/address mapping; `.pdata` routine boundaries; Capstone 5.0.9 x64 disassembly; direct call and RIP-relative string references |

The version resource is declared executable metadata. It does not validate any CPR field. A modern reader's compatibility code does not establish the complete format of earlier Cubase versions.

## Reproduce the observations

The standard-library script [inspect_cubase15_serializers.py](../../scripts/inspect_cubase15_serializers.py) verifies the input hash before applying any known address, maps virtual addresses into PE sections, and prints routine offsets, lengths and SHA-256 values:

```powershell
python scripts/inspect_cubase15_serializers.py Cubase15.exe
python scripts/inspect_cubase15_serializers.py Cubase15.exe --function archive-class-reader --function archive-body-size
python scripts/inspect_cubase15_ida_export.py Cubase15.exe.asm --function midi-track-registration --function midi-track-reader --function audio-track-registration --function audio-track-reader --function instrument-track-registration --function plugin-slot-registration --function plugin-slot-reader --function plugin-slot-loader --output artifacts/cubase15-track-plugin
```

Optional disassembly uses Capstone; it is not a product dependency:

```powershell
python -m venv artifacts/cubase-static-env
artifacts/cubase-static-env/Scripts/python -m pip install capstone==5.0.9
artifacts/cubase-static-env/Scripts/python scripts/inspect_cubase15_serializers.py Cubase15.exe --function tempo-reader --function tempo-writer --disassemble
```

`artifacts/` is ignored. Keep executable copies, extracted bytes, full disassemblies, private project paths and corpus dumps there. Do not commit them. The script refuses a different binary hash; new builds require independently recalculated addresses.

## Container and ROOT/ARCH framing

These are directly observed instructions and control-flow relationships in this executable:

| Routine/address | Observation |
|---|---|
| `0x1446154a0` | Reads RIFF/RIF2, sets mode 0/1 from the signature, reads a 4/8-byte length and then a four-byte form identifier. |
| `0x1446150b0` | Reads a four-byte chunk identifier. Mode 1 reads an eight-byte length; otherwise it reads four bytes. The 64-bit result must be nonnegative. |
| `0x144603e06`–`0x144603eac` | ROOT payload reads two four-byte lengths with `0x1439df440`, each followed by exactly that many raw bytes; each gets a temporary terminating NUL in memory. |
| `0x144604970` | ROOT writer emits the matching pair of length-prefixed byte strings. These lengths describe raw string bytes, unlike the NUL-inclusive class strings below. |
| `0x144603f15`–`0x144603f88` | Iteration accounts for payload length plus an 8-byte header in mode 0 or 12-byte header in mode 1. There is no ordinary RIFF even-byte padding step on this path. |
| `0x144603f25`–`0x144603f65` | ARCH information is attached to the most recent ROOT entry. Unknown chunk identifiers follow the declared-length skip path. |
| `0x1446042a4`–`0x1446042c2` | The archive constructor receives its 32/64-bit mode from the container mode. |

The integer primitives conditionally reverse byte order according to the underlying stream flag. The envelope sets that flag to 1, consistent with the existing big-endian strategy and controlled RIF2 fixtures. This pass has not independently traced flag initialization for every archive input path or historical file. ROOT/ARCH findings support the current reader for this build; they do not establish arbitrary recursive payload grammar.

## Compact class headers and versions

The class-header reader is `0x1445ea890`; the matching writer is `0x1445ea6e0`. The archive constructor records the mode at object offset `+0x68`.

1. The next discriminator occupies four bytes in mode 0 or eight bytes in mode 1. The 32-bit reader sign-extends it for sentinel comparisons.
2. Discriminator `-2` declares a class symbol: a **four-byte length**, that many bytes of class-name text, then a **two-byte class serialization version**. The reader records the symbol at its offset relative to archive start and continues with another mode-width discriminator. The writer walks base-class registrations for these declarations.
3. Discriminator `-1` introduces a complete class header with the same string and two-byte version, followed by the body length.
4. Other MSB-set values reference a class declaration at an archive-relative offset after masking the sign bit. The class and its version are recovered together. The native reader can seek back to a declaration when it is absent from its lookup table (`0x1445eac47`–`0x1445eac97`).
5. Zero and positive discriminator values participate in null/existing-object handling. They are **not** independent full class records. A class inventory must not invent bodies for them.

The class-string helper `0x1439e04a0` reads a u32 length and raw bytes. The matching writer `0x1439e03d0` emits `strlen + 1`, including the terminating NUL. Version reader `0x1439df070` consumes two bytes. The native string helper has a `0x40000` length guard; the product reader may deliberately impose smaller limits.

**A correction to a permissive older scanner:** after a `-2` declaration marker, this native path reads a full length-prefixed string directly. It does not accept a special four-byte class-reference alternative there. Final class references use the mode width (32 or 64). A mixed-width prefix-reference fallback would need separate fixture evidence.

Class versions are per-class serializer revisions, not application versions, bit depth, or a general project version. Preserve them with exact declaration offsets; do not report a class version of 3 as Cubase 3.

## Body lengths and object consumption

`0x1445eb010` reads the body length independently of class version: eight bytes in mode 1 (`0x1439df880`), four bytes otherwise (`0x1439df650`), with a nonnegative result required. Both new class headers and reused class references call this helper. No version-specific width branch was found in this routine.

The object reader `0x1445e9990` constructs the registered type, installs its class version in the active archive, and invokes object vtable offset `+0x90` to deserialize. On completion it restores the previous active version and seeks to body start plus declared length if the callback did not finish there. The writer invokes vtable `+0x98` and backpatches the mode-width body length.

Consequences for the inspection reader:

- Retain class version, archive identity, declaration/reference offsets and body boundaries independently of semantic fields.
- Bound every record by its containing archive, preserve unconsumed byte spans and report limits explicitly.
- A plausible class header found by byte scanning remains a structural candidate. Native object callbacks know where their fields and child objects are; scanning arbitrary opaque bodies cannot reproduce that knowledge.
- Do not map duplicate names to one track or treat class proximity as plugin identity, track ownership or routing.

## Registration-to-reader chains

The registration helper `0x1439c6ec0` receives a class string, constructor callback, base registration and u16 serializer version. It stores the constructor at registration `+0x28`, base registration at `+0x30`, and version at `+0x38`.

| Class | Registration routine | Declared class version |
|---|---:|---:|
| `MMidiNote` | `0x14157fd90` | 1 |
| `MMidiPartEvent` | `0x141580cf0` | 2 |
| `MMidiTrackEvent` | `0x141581220` | 4 |
| `MAudioEvent` | `0x1415c7bd0` | 6 |
| `MAudioTrackEvent` | `0x1415ced60` | 1 |
| `MMarkerEvent` | `0x1416a6470` | 0 |
| `MTimeSignatureEvent` | `0x1417dceb0` | 2 |
| `MTempoEvent` | `0x1417de1e0` | 0 |
| `MTempoTrackEvent` | `0x1417de230` | 3 |

These are registrations in one executable, not proof that every class occurs in every CPR. Older saved files may legitimately declare earlier serializer revisions.

Two fully followed chains:

- `MMidiNote`: registration `0x14157fd90` → factory `0x141bbe4c0` → constructor `0x141bb3ba0` → primary vtable `0x145f41908` → reader entry `0x141bb4690`, writer `0x141bb49a0`. The reader first calls base reader `0x141bb0390`, which itself consults serializer versions and attributes. The note reader has split `.pdata` fragments, so a single first-fragment boundary is not the complete callback. **Do not extrapolate the existing 112-byte controlled-note candidate to every MIDI-note version.**
- `MTempoEvent`: registration `0x1417de1e0` → factory `0x1431e83e0` → constructor `0x1431e0570` → primary vtable `0x1461e30e0` → reader `0x1431e0870`, writer `0x1431e08f0`.

## Track/lane class markers and plug-in load properties

The IDA export provides stronger class-level evidence for native track records
than byte proximity or inferred names:

| Cubase 15.0.40 observation | Meaning supported by the executable | Limit |
|---|---|---|
| `MMidiTrackEvent` registration at `0x141581220`, class version 4 | A named serializable track-event class exists in the current binary. | Does not prove that every instance is a visible lane or that its nested name extraction is complete. |
| `MAudioTrackEvent` registration at `0x1415ced60`, class version 1 | Same, for the audio track-event class. | Version is this class's serializer revision, not Cubase version or a guarantee for older files. |
| `MInstrumentTrackEvent` registration at `0x14168cf60`, class version 4 | Same, for the instrument track-event class. | Ownership of the associated instrument slot is not established by the class name alone. |
| Direct class-name comparisons/references for `MDeviceTrackEvent`, `MFolderTrackEvent`, `MMarkerTrackEvent`, `MTempoTrackEvent`, `MSignatureTrackEvent`, `MAutomationTrackEvent`, `MChordTrackEvent`, `MDrumTrackEvent`, `MVideoTrackEvent`, `MPlayRangeTrackEvent`, and `MLoudnessTrackEvent` | These exact Cubase class identifiers are useful classifications when a bounded `PArrangement` record has the matching class header. | We have not traced every class's registration, serializer version, visibility rules, or full body reader. The app does not infer group/FX/VCA classes where this export did not provide a matching native identifier. |

The reader now maps these exact identifiers to useful lane kinds when they are
encountered inside `PArrangement`. It displays the native class type directly;
the extracted name, visible-track status, hierarchy, and channel ownership stay
separate observations. This includes `.bak` inputs when the bytes contain the
same RIFF/NUND or RIF2/NUND project container. Steinberg documents `.bak` as
auto-saved project copies and warns that project features absent from the
opening Cubase version may be lost on save ([Auto Save](https://www.steinberg.help/r/cubase-pro/15.0/en/cubase_nuendo/topics/project_handling/project_handling_about_the_auto_save_option_c.html),
[Opening Project Files](https://www.steinberg.help/r/cubase-pro/15.0/en/cubase_nuendo/topics/project_handling/project_handling_opening_project_files_t.html));
the app therefore keeps BAK reads read-only and does not attempt crash recovery
or repair.

The plug-in path has useful loader-level anchors:

- `APluginSlot` is passed to the serializer registration helper at
  `0x1415A4B10`.
- In `sub_141D49EA0`, the code gets the `Plugin` child, requests `Plugin UID`
  from it, and later requests `Original Plugin Name` (IDA lines 7,387,883–
  7,388,315). Other exact references include `Plugin Name` and `InsertFolder`.
- Separate code paths compare the typed fields `fxPluginUID` and
  `instrumentPluginUID` (lines 9,051,604–9,051,641). They are additional UID
  properties used in native code; they are not interchangeable with the
  `Plugin UID` property without an on-file object-context match.

This confirms Cubase's runtime field vocabulary and that a slot loader looks up
these properties. It does **not** disclose the complete on-disk property tree,
prove VST2 versus VST3 from the UID's shape, recover vendor state, or identify a
track owner. The existing token reader therefore exposes UID/name values with
their offsets and clearer "stored property" descriptions while leaving format,
slot, owner, enabled/bypass and state as unresolved until an `APluginSlot`
record-boundary parser is validated against controlled CPR and BAK files.

Public cross-checks support the scope, not the proprietary grammar: the
[BandPass parser](https://github.com/bandpassrecords/daw-project-manager/blob/6d5d82ac352ddb83212e1240612e24a252d03d5c/lib/services/daw_parsers/cubase_project_parser.dart)
also searches `Plugin Name`/`Original Plugin Name` and describes `FNPath`
pool paths, while the [MIT Cubase Project Plugins reader](https://github.com/fgimian/cubase-project-plugins)
reports plug-in UIDs from Cubase 4 onward and explicitly notes older SX3
projects may not expose plug-ins. Neither is a Steinberg format specification.

## Track serializer dispatch and plug-in slot load path

A second pass followed class registration through factories and the object vtables
in the same hash-identified Cubase 15.0.40 IDA export. Under the native archive
object reader's virtual dispatch at `+0x90` (writer at `+0x98`), the paths are:

| Serialized class | Registration → factory | Object reader (`+0x90`) | Object writer (`+0x98`) |
|---|---|---|---|
| `MMidiTrackEvent` v4 | `0x141581220` → `0x141BEFE80`; factory allocates `0x208` bytes and initializes through `0x141BD2740` | `0x141BCCFB0` | `0x141BCD590` |
| `MAudioTrackEvent` v1 | `0x1415CED60` → `0x141E8C6B0`; factory allocates `0x1D8` bytes | `0x141E85B90` | `0x141E85F50` |
| `MInstrumentTrackEvent` v4 | `0x14168CF60` → `0x142615CA0`; factory allocates `0x210` bytes and initializes through `0x141BD2740` | `0x141BCCFB0` | `0x141BCD590` |
| `APluginSlot` v0 | `0x1415A4B10` → `0x141D4FDA0`; factory allocates `0x678` bytes | `0x141D49320` | `0x141D49BC0` |

The three track-event entries therefore have actual registered serializers and
dedicated or shared callbacks; they are not merely strings found in the binary.
When the CPR reader has parsed a bounded class header in an ARCH stream and its
class identifier matches one of these names, it can report the serialized class
as recognized. This does not make a byte-window-extracted name a proven visible
track, prove parentage/order, or establish that the same class contract held in
every historical Cubase release. The reader/UI now separates **recognized
serialized class** from those unresolved relationships instead of labelling
the whole record “Unverified CPR record.”

For plug-ins, registration confirms `APluginSlot` itself has a native serializer.
The separately traced runtime load function `sub_141D49EA0` is called by
`sub_141D704A0`; it obtains a `Plugin` child and asks for `Plugin UID`. A later
branch obtains `Original Plugin Name`. The serialized slot callback
`sub_141D49320` is not itself proof that the token reader found a complete slot
boundary around every marker. Consequently the UI now says stored UID/name
fields were found when the typed property pattern matched, while keeping
plugin identity, format, slot/track ownership, state, and loadability unresolved.

### Track callback follow-up and reader exposure

A bounded extraction from the hash-matched IDA export also inspected the complete
`MMidiTrackEvent`/`MInstrumentTrackEvent` callback (`sub_141BCCFB0`, excerpt
SHA-256 `4a401572bce24948f9c42b648f02c9682a53ff0fb56cdc207a9946c309711892`)
and `MAudioTrackEvent` callback (`sub_141E85B90`, excerpt SHA-256
`d3333cf5201e5b34ba549111808c7b6835bb4af4d07045bcffdf9c34336dcc70`). The
MIDI/instrument callback contains version-gated branches for
`MMidiTrackEvent`, reads the named `solo` and `soloDefeat` properties, checks
`isGrooveTrack`, and processes `FAttributes` in a later version branch. The
audio callback selects nested `MAudioTrackEvent` or `MAutomationTrackEvent`
objects and has an `MAutoFadeSetting` branch. These are useful search leads for
the next reader work; they do not define the byte grammar of those property
objects, and the native callback also performs in-memory object wiring.

The inspected track callbacks do not by themselves show the saved-file grammar
for these properties, nor do they establish mute or fader decoding. Other
`Mute` and `Volume` strings in the export have references in unrelated
control/parameter code, so string presence alone is not a track-serializer lead.

The app now joins each arrangement track hint to a separately scanned class
header only when both scanners agree on the exact absolute header offset and
class name. When available, the technical details view exposes its stored class
serializer version, body byte count, header encoding and file offsets. These
values are still structural-reader interpretations; class version is not the
Cubase application release. The normal track view keeps those low-level fields
behind its `(i)` control. Separately, the bounded named-record reader can expose
literal `Volume/AnchorValue`, `Mute`, and `Solo` values when a mixer record is
wholly contained by exactly one scanned track byte range. The track page
presents those as saved mixer settings and keeps the record source behind `(i)`.
This is a structural join plus named-field observation; the Cubase 15 callback
does not validate the field encoding or cross-version behavior. Pan,
solo-defeat, groove, automation/fade state, and mixer field grammar remain open
until the property reader and controlled saves are traced.

These exact addresses apply only to the inspected executable hash above. The
work improves the evidence label for Cubase 15 class/property observations; it
does not certify cross-version field semantics or normalize inferred hierarchy.

## MTempoEvent: raw typed body candidate

The registered version is zero. Its reader consumes the following sequence directly, with **no base-class deserialize call**; the writer emits the same sequence. This supports an exact 24-byte, class-version-zero raw-field candidate:

| Body offset | Width | Primitive | Destination in native object | Reader call |
|---:|---:|---|---:|---:|
| 0 | 4 | float32 | `+0x48` | `0x1439dfda0` |
| 4 | 8 | float64 | `+0x50` | `0x1439dfff0` |
| 12 | 8 | float64 | `+0x58` | `0x1439dfff0` |
| 20 | 4 | uint32 raw bits | `+0x60` | `0x1439df440` |

The layout callback does not branch on archive width. Primitive byte swapping still depends on stream byte-order state. Preserve raw bytes and the chosen byte-order interpretation if exposing these values.

**No semantic promotion:** this pass does not establish which field is BPM, period, time, scaling, flags or interpolation. A constructor performs a reciprocal-like conversion, but its operand units have not been established; it is insufficient to label a persisted field as BPM. No full version-zero/24-byte `MTempoEvent` definition was found in either controlled Cubase 15 fixture or the bounded local corpus pass (1,366 CPR/BAK files visited; files above 128 MiB skipped). This negative result does not prove absence of tempo data: it may use references, raw arrays, another representation or files outside the pass. A synthetic test can verify the candidate decoder's mechanics, never native semantic validity.

## MTempoTrackEvent: raw array verified against the controlled fixture bytes

Further tracing explains one way tempo objects are persisted without standalone `MTempoEvent` headers. Registration `0x1417de230` → factory `0x1431e8420` → constructor `0x1431e3e20` → primary vtable `0x1461e35c0` → reader `0x1431e2e00`, writer `0x1431e30f0`.

The reader begins directly with a u32 item count, rejects counts below one, and loops over raw values. It constructs `MTempoEvent` objects in memory from these items. The array item width is **22 bytes**, which differs from the standalone class body's 24 bytes:

| Body position | Width | Raw primitive |
|---|---:|---|
| `+0` | 4 | count, uint32 |
| `+4 + 22*i` | 4 | item field 0, float32 |
| `+8 + 22*i` | 8 | item field 1, float64 |
| `+16 + 22*i` | 8 | item field 2, float64 |
| `+24 + 22*i` | 2 | item field 3, uint16 bits (native reader sign-extends to its internal int32) |
| `+4 + 22*count` | 4 | suffix field 0, float32, only when active class version > 0 |
| `+8 + 22*count` | 1 | suffix field 1, byte, only when active class version > 0 |

Calls establishing item widths occur at `0x1431e2e90`, `0x1431e2ea2`, `0x1431e2eb4` and `0x1431e2ec9`. Suffix reads occur at `0x1431e2f75` and `0x1431e2f89`. Version > 1 reads named attributes; version > 2 also reads attributes for each item. These trailing attribute records are not part of the fixed array and must remain accounted for separately. The writer's item loop and suffix match these widths (`0x1431e318c`–`0x1431e3210`).

Both existing controlled Cubase 15 fixtures expose this exact class version 3, declared body size 54, body file offset 879. Reading only the fixed portion as big-endian yields:

| Fixture | Count | Item raw values | Suffix raw values | Remaining attribute bytes |
|---|---:|---|---|---:|
| `CPETestEmpty.cpr` | 1 | `0.5, 0.0, 0.0, 0` | `120.0, 0` | 23 |
| `CPETestMidiC1_UpTo16Steps.cpr` | 1 | `0.5, 0.0, 0.0, 0` | `120.0, 0` | 23 |

Their SHA-256 values remain respectively `cf0d6f5f1828d7da6857506b5973c5dcd7c6c1712ec60683862077be22a1bcdf` and `77fa9d77ea911ed161eea13c7b099e3943f6753b42c81b5c41f31be79a1b1804`, matching [the private fixture manifest](../CPR_RIF2_TEST_FIXTURES.md). This verifies native bytes and read-width agreement, **not** the meaning of the values: no deliberate tempo-change pair or exported tempo map has been compared. In particular, do not promote the suffix value `120.0` to confirmed project BPM or assume the item value `0.5` always represents seconds per beat.

Any implementation should preserve anonymous field names and source offsets, enforce count/byte budgets, preserve nonfinite values as raw-bit evidence and label this exact-layout observation separately from normalized tempo data. The active version branches in this modern executable support targeted future legacy tests, not an all-version compatibility claim.

## Additional reversing targets, not implemented field decoders

- `MTimeSignatureEvent`: factory `0x1431df570` assigns primary vtable `0x1461e11e0`; callbacks are reader `0x1431d3b70` and writer `0x1431d3c40`. The reader consumes one 64-bit integer followed by **six 16-bit integers**, then reads named attributes when its class version exceeds one. Its registered version is two. This is a 20-byte fixed prefix plus variable attributes, not an established denominator/numerator/time mapping. The current fixtures contain a signature type name in named attributes; a string hit does not prove a compact class boundary or validate these prefix semantics.
- `MMarkerEvent`: factory `0x1426fd360` → constructor `0x1426fbb90` → primary vtable `0x1460a7098`; reader `0x1426fc400`, writer `0x1426fc470`. They call string/container helpers and then base-event callbacks (`0x1431abd30` / `0x1431abe90`). Trace those helpers and obtain marker-change fixtures before defining field offsets or claiming marker position/name recovery.
- For MIDI and audio tracks, follow the registration factories from the table above to their primary vtables and base callbacks. Different serializer revisions and attribute branches make fixed byte-offset extrapolation unsafe. The existing candidate track names, audio path tokens and MIDI notes need independent owner/placement validation even when a class name is confirmed in the executable.

### Follow-up: time-signature prefix and marker boundary

A bounded read of the supplied `Cubase15.exe.asm` snapshot confirmed the `MTimeSignatureEvent` callback's actual primitive sequence: the reader calls the u64 helper at object offset `+0x48`, then the u16 helper six times at `+0x50` through `+0x5A`. It then asks the archive for the `MTimeSignatureEvent` class version and calls the named-attribute reader when the version exceeds one. The writer mirrors one u64 and six u16 writes before its attribute writer. The candidate decoder now retains just this fixed 20-byte prefix for class version 2, with per-field file offsets and raw bytes; attributes remain opaque. It does not call these values numerator, denominator, bar, or time, and the archive's byte-order interpretation remains candidate evidence. Tests cover the field widths, offsets, attribute tail, wrong versions and truncated prefixes.

The same snapshot shows the marker reader first invokes a container/string helper (`0x14458acf0`), reads a u32 at object `+0x70`, then invokes its base-event reader. A separate helper at `0x1426fcb20` is conditional. The writer mirrors this order with the matching helpers. Without tracing the helper's bounded byte consumption and comparing controlled marker additions/deletions, the class body cannot be split into reliable offsets; no marker field decoder was added. Likewise, the tempo-track callback's per-item version>2 attributes make a generic `count × 22-byte stride` unsafe even though the fixed item primitives are known. Its existing exact one-item candidate remains deliberately narrow.

These additions are static evidence from the same Cubase 15.0.40 executable, not backward-version validation. `MMidiNote`, `MTempoTrackEvent` and `MTimeSignatureEvent` class serialization versions are not application-version selectors. Add older-version layouts only after real files establish the class header, body boundary and independent value comparison.

The optional script includes these signature/marker callbacks so follow-up analysis can start at verified addresses without repeating string searches. They are research targets, not a claim of reader coverage.

## Validation and next evidence needed

This analysis verified script address mapping and hash checks against the local executable. Default inventory uses Python's standard library; optional Capstone output was checked against the independent local PE/`.pdata` mapping. The executable hash remained unchanged. Original CPRs were read only.

Implementation verification on 2026-10-31: all 408 Release tests pass with the
private-fixture environment enabled; Uno Desktop and CLI Release builds have
zero warnings/errors. The actual CLI inventories seven ARCH streams in each
controlled fixture and retains 225/342 candidate headers respectively, with
seven anonymous tempo-track raw fields each and unchanged source hashes.
The new scanner reports no reached safety limits on these examples; that does
not establish complete object or musical decoding. Five existing test-analyzer
warnings and the outstanding live desktop acceptance work remain in PLAN.md.

Additional read-only CLI smoke checks sampled eight uncontrolled projects from
the already user-authorized local corpus. Selection read 21 bounded file prefixes
and chose distinct saved-by hints; it did not rerun the entire corpus. All samples
used RIFF. Private names, paths and reports stay in ignored local artifacts.

| Saved-by candidate | ARCH inventories | Header candidates | Retained details | Reached inventory limits |
|---|---:|---:|---:|---|
| Cubase SX 3.1.1 | 5 | 2,838 | 2,838 | None |
| Cubase 6.5.0 | 6 | 316 | 316 | None |
| Cubase 6.5.2 | 6 | 605 | 605 | None |
| Cubase 7.0.0 | 6 | 4,096 | 4,096 | None |
| Cubase 7.0.6 | 6 | 231 | 231 | None |
| Cubase Artist 7.0.1 | 6 | 11,917 | 10,000 | Record-detail and range retention |
| Cubase 8.5.30 | 7 | 761 | 761 | None |
| Cubase 11.0.10 | 7 | 5,532 | 5,532 | Range retention |

All eight CLI commands succeeded; SHA-256 values before and after inspection
were unchanged. These are unvalidated project contents and application-version
hints, not independent Cubase comparisons. Successful bounded structural reading
does not prove correct musical-field interpretation for those releases. In the
two limit cases, aggregate counts remain available while retained detail/ranges
are explicitly partial.

Highest-value next samples are one-change tempo/signature pairs with independent tempo/MIDI exports, a class-version matrix from older RIFF and current RIF2 projects, and owner-validated track/part/note/media pairs. Each field still needs a Cubase comparison before becoming normalized project data. No plugin deactivation, native write path, complete object reconstruction or universal version support follows from these observations.

## Native routing display from typed field observations

The 2026-10-31 ASM follow-up traces `OwnInputBus` at assembly line 16,379,355 through `sub_14370F840`; that helper at line 17,122,524 reads the `Bus UID` key through `sub_1445D33A0`. `OutputBus` read/write references occur at lines 15,256,152 and 15,256,289, while `SendFolder` is used in the neighbouring mixer routines. These anchors support researching bus identities and per-slot destinations. The `OutputBus` references have additional native conversion calls; their presence does not establish one universal scalar representation across versions.

The desktop routing view now retains the bounded named-record fields already decoded by the reader and interprets numeric `OwnInputBus/Bus UID`, `OutputBus/Value`, and per-slot `SendFolder/.../Output/Value` values. It also accepts the explicit `Bus UID` child variants documented by the presentation code. Values must fit unsigned 32-bit decimal IDs; opaque blobs and unsupported representations remain available as raw fields. A destination joins a mixer record only when exactly one record in the same source stream declares that bus ID. Duplicate identities and missing targets stay as unresolved bus-ID endpoints. Names, byte proximity and record order do not supply edges.

This is a read-only presentation projection. It preserves candidate evidence on the channel records and edges, source field paths and offsets, and never assigns native dB anchors to the normalized DAWproject linear-volume field. Synthetic tests cover named-field extraction, ID-based output/send links, duplicate names/IDs, cross-stream isolation and cycle traversal. Cubase playback/route comparison and the full per-version native field grammar remain open; the UI displays the supported interpretation without requiring those follow-up checks before inspection.

A read-only check of the locally available Cubase 12 template found two named mixer records but initially no retained routing fields: the 128-leaf limit stopped inside `StripFolder` before the bus descriptors. The bounded limit is now 1,024 leaves (the existing 256 KiB record window, 4,096-node and depth limits remain). A second top-level `Name/String` anchor ends the current record instead of merging adjacent named records. Routing fields are prioritized within the retained 512 mixer fields. Re-reading the same template exposes 56 routing/bus fields, including `OwnInputBus/Name`, `OwnInputBus/Bus UID`, `OutputBus/Bus UID`, child-bus identities, channel arrangements and saved driver/port strings. Those saved strings describe the file's stored configuration and are not a probe of hardware connected to this machine. The controlled empty Cubase 15 file still has no decoded named mixer records; its track observations remain available as standalone nodes.

## Composite MIDI/audio → processing → bus/output projection

The current bounded scan retains `InputBusValue`, `OutputBusValue`, nested `OwnInputBus`, `OutputBus`, `SendFolder`, `InsertFolder`, `EQ` and `ChannelEQ` paths when the named-record walker reaches them. In the supplied ASM, the mixer reader at RVA `0x143513190` requests the exact property names `InputBusValue` (line 16,379,297) and `OutputBusValue` (line 16,379,326), then requests `OwnInputBus` (line 16,379,355) and calls its structured reader `sub_14370F840`. The neighboring routine passes `SendFolder` and `directRoutingFolder` to a folder reader. These calls support looking for those stored properties and nested folders; they do not alone define their meanings for every archive version or prove the audible signal tap order.

The current diagram joins only on explicit saved bus IDs within the same source stream. It also associates a named mixer observation with a CPR track record only when its full bounded field extent has one unique smallest containing track extent. That containment edge stays `Candidate`; a track name or nearby byte offset is never sufficient. Native insert name fields are shown in stored slot order when a slot index is present, followed by a synthetic channel-output stage so saved output/send references remain connected in the view. DAWproject plug-ins continue to use declared `TrackId`, `ChannelId` and slot ordering. Native MIDI/audio observations connect to track nodes only by exact candidate source offsets.

Typed `EQBandGainValue`, `EQBandFrequencyValue`, `EQBandQualityValue`, `EQBandOnValue`, `EQBandFilterTypeValue` and `ChannelEQBand` identifiers occur in the executable's property registrations (for example gain/frequency/quality at lines 13,509,177–13,509,293 and band at line 13,510,221). These registrations identify useful properties to search for, but are not proof of where/how they persist, their defaults, or their mapping to a DSP curve. Accordingly, the view displays exact nested values as candidate saved EQ settings attached to a channel, without drawing a response curve or placing EQ before/after inserts. Native insert state blobs are not interpreted as verified plug-in instances. The full-chain window follows only stored/project relationships and labels candidate links; the order between sends, EQ taps, inserts and channel outputs is not independently verified.

A further ASM pass found a useful insert-position property. In the channel reader around line 16,508,727, Cubase first reads `InsertFolder`, then looks up `postInsertsPreFader` (line 16,508,746) and writes the decoded integer through the channel object's virtual setter at line 16,508,752. The bounded named-record reader now retains that exact field and the diagram displays its raw value as an insert placement setting. The property name is a strong semantic hint, but this pass has not established whether the setting applies to every insert, how the encoded integer maps to UI choices, or how it changes sends/output tap positions. The view therefore does not use it to reorder the graph. Zero-initialized temporary registration storage near the EQ property-name registrations is not treated as proof of default EQ values.

The composite model test checks a two-slot native insert chain → channel-output stage → bus link and a saved EQ value, while asserting candidate evidence and the absence of a direct pre-insert route. Remaining high-value validation is a controlled Cubase pair that changes one connection/insert/EQ at a time, saved in representative versions and independently compared with Cubase's Mixer and exported audio/MIDI. The bandpassrecords parser's `_readChannel` and MyDAW's import provider are useful cross-checks for `OwnInputBus`, `OutputBus`, `InsertFolder` and plug-in slot patterns, but are third-party interpretations with acknowledged gaps rather than format specifications: [bandpassrecords/daw-project-manager](https://github.com/bandpassrecords/daw-project-manager/blob/6d5d82ac352ddb83212e1240612e24a252d03d5c/lib/services/daw_parsers/cubase_project_parser.dart), [MyDAW CPR import provider](https://github.com/omeriko9/MyDAW/blob/ee232374b9ce2e6e243a3d29e7afefcfd17f0354/engine/src/import/CprImportProvider.h).
