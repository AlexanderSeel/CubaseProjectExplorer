# Cubase 15 decompile gap map

Date: 2026-11-03. Static review of the user-provided
`E:\ReverseEng\Cubase15.exe.c` decompilation. Its SHA-256 was recorded as
`35B8643F6F5211CC4E5BF0CCD5EFD5F7541F9CC289B09D28A4F9DD1D0A444CF8` before
review; the file was not executed or modified. C line numbers below refer to
that snapshot. A named serializer operation is evidence that this executable
reads/writes a property or class; it is not by itself proof of project-wide
meaning, version coverage, or runtime availability.

This note maps the remaining highest-value reader gaps to decompile candidates.
It supplements [the serializer analysis](CUBASE15_SERIALIZER_ANALYSIS.md) and
[automation/I/O findings](AUTOMATION_IO_2026-11-02.md). Confidence levels:

- **Direct serializer evidence**: class callback, revision gate, named key, or
  primitive read/write is visible in the decompile.
- **Candidate interpretation**: a plausible field/relationship supported by
  names, object wiring, or existing bounded observations, but not independently
  validated as persisted meaning.
- **Unknown**: grammar, owner, enum mapping, or semantics still require tracing
  and controlled Cubase saves/exports.

## Gap inventory

| Area | Direct evidence in the C export | Candidate information to pursue | Still unresolved / safe boundary |
| --- | --- | --- | --- |
| Track records and hierarchy | `MTrack` is registered at serializer revision 2 (`FUN_1431ed1d0`, registration near line 5,176,649); `MTrackEvent` rev 2 is also registered nearby. `MTrackList` is separately registered rev 0 (line 5,179,242); its reader `FUN_1431fd9a0` (lines 9,816,048–9,816,060) repeatedly reads `Tracks` children as `MEvent` and appends them to an internal list. The writer `FUN_1431fd900` emits list entries under `Tracks` (lines 9,816,021–9,816,044). Specialized `MMidiTrackEvent` rev 4, `MAudioTrackEvent` rev 1, and `MInstrumentTrackEvent` rev 4 registrations occur near lines 4,698,897, 4,754,741, and 4,920,775. | Trace `MTrack` and concrete `MEvent` readers for explicit IDs, parent/container IDs, visibility, folder membership, and track-kind-specific nesting. The repeated `Tracks` sequence is a candidate source of serialized order because the reader appends in iteration order; compare it with arrangement order in Cubase, including nested folders. Candidate class names also include folder, group, FX, VCA, chord, marker, tempo, signature, and video tracks, but each needs registration and callback tracing. | `MTrackList` confirms a serialized collection boundary and sequence of event objects, but the export examined so far does not establish a stable track ID or prove serialized sequence always equals visible order. A class header or arrangement containment is not enough to prove a visible track or hierarchy. Do not synthesize group/FX/VCA categories from names or nearby bytes. |
| Track input and physical I/O | Separate read callbacks request `Input Type`, `Input Device Name`/`Input Device ID`, `Input Channel ID`, and `Input Port Name` (for example lines 5,889,628–5,889,641; another path at 7,694,411 onward). Device connection callbacks read `Connection Type`, `Device Name`, `Channel ID`, `Port Name`, `HasInputFromDevice`, `InFrDevDevName`, and `InFrDevPortName` (around lines 5,989,000–5,989,069). | Decode each saved field's type/revision and enclosing object; compare with a saved project where one input assignment is changed. Candidate distinction: track's selected input versus project I/O device-port definitions versus “input from device” connection metadata. | Saved names do not establish device presence, physical patching, channel numbering conventions, or a globally stable port ID. Preserve unknown strings and conflicts; no name-based endpoint joins. |
| Mixer routes, sends, direct routing, buses | Mixer callback `FUN_143513190` (around lines 10,340,990–10,341,189) reads `InputBusValue` and `OutputBusValue`; it also processes `OwnInputBus` and `RecorderBus` using a structured bus helper, then distinct `SendFolder` and `directRoutingFolder` branches with mode values 3 and 2. Bus helper `FUN_14370f840` requests `Bus UID`; bus object reader/writer `FUN_14370f880` handles bus UID/type/arrangements/child buses, and writer-side parent UID is visible around lines 10,686,655 onward. The mixer writer also emits `foldbackSendFolder` (line 10,399,676), but its slot reader has not been traced. | Trace each field's complete property type and nested child object; identify how channel output, sends, direct routes, recorder routes, foldback sends, and bus hierarchy differ. Check route enabled/mute/pre-post state and slot ordering in send/direct folders. Compare one-change saves and Cubase mixer display. | Existing numeric bus references are supported candidate edges only when a unique ID resolves within the same source stream. The traced collector does not read foldback slots; their bounded named fields are retained for source inspection but do not generate route edges. The callback does not prove cross-archive ID scope, zero semantics for every field, enum meanings, or audible signal order. Keep sends, foldback sends, and direct routing distinct. |
| Audio tracks, events, parts, pool/media | Registrations include `AudioFile` rev 1 (`FUN_1421ee8c0`, line 4,836,240), `MAudioEvent` rev 6, `MAudioPart` rev 0, `MAudioTrack` rev 0, and `MAudioTrackEvent` rev 1 (around lines 4,749,342–4,754,741). The `MAudioTrackEvent` callback is `FUN_141e85b90` (line 6,344,416); event factory/constructor references include `FUN_141e29ce0`. | Follow each registered class from factory to its object reader callback. Candidate fields: media identity/path/format, clip start/length/offset, source range, fades, event-to-part and part-to-track links, lanes/comping, warp/time-stretch data, pool identity, and channel count. `MAutoFadeSetting` is an explicit revision-gated lead in the audio-track-event callback (see serializer analysis). | The `AudioFile` factory allocates/constructs an object; it is not its reader. Constructor layout is not serialized layout. Candidate paths or containing audio records do not prove pool identity, clip placement, or visible ownership. Do not reconstruct media state from guessed offsets. |
| Plug-in slots, identity and state | Runtime loader code around `FUN_141D49EA0` obtains a `Plugin` child and requests `Plugin UID` and `Original Plugin Name` (C region around lines 6,130,790–6,130,902); `APluginSlot` registration and callbacks are traced in the serializer analysis. Other plug-in paths read `Plugin Name`, `IDString`, `Active`, and `SideChain enable`. | Locate complete slot boundaries and map slot index/order, owner channel/track, enabled/bypass state, format-specific identity, sidechain connections, and opaque state boundaries. Compare candidate identity tokens with local metadata only after serialized context is proven. | Names/UID shapes/IDString are stored identity candidates, not definitive product or format. Runtime load code is not the slot's complete disk grammar. Do not execute plugins, infer state from opaque blobs, or report candidate markers as definitive identities. |
| Inserts, EQ and channel processing | Mixer/channel callback reads `InsertFolder`, `postInsertsPreFader`, EQ/channel-EQ paths and typed band property names (documented in serializer analysis). | Trace insert child serializer and saved slot identifiers/order; trace EQ band object and units/ranges; connect process stages to a channel only with exact owner evidence. Compare a controlled insert/EQ change against Cubase UI and rendered output. | Property names do not establish dB/frequency units, enum mapping, default values, bypass, exact stage order, or pre/post-fader tap point. Do not reorder the graph from raw `postInsertsPreFader` until its values are validated. |
| Automation lanes, targets and curves | `MAutomationTrack` rev 2 and `MAutomationTrackEvent` rev 1 register around lines 4,841,765–4,841,775. Lane reader `FUN_14224eee0` at lines 7,028,216 onward invokes base event reading; at positive active revision it consumes a u32, f32 and u16 flags, calls `FUN_1422613f0`, and conditionally reads an `MDataNode` under `AUTOMATION_MODES`. Writer is `FUN_14224f030`. Track reader/writer callbacks are `FUN_142251240`/`FUN_1422514d0` (lines 7,029,648 and 7,029,746). `FUN_1422613f0` iterates `MParamEvent` children (lines 7,040,932 onward). | Trace base event and `MParamEvent` reader/writer, automation-node references, target getter, and flag bit meanings. Candidate scalar/flags/modes, parameter target, time/value curve points, and lane owner are distinct questions. Validate with automation edits and exports/parameter behavior. | The u32 is not established as parent ID. `MDataNode` under `AUTOMATION_MODES` is a mode payload candidate, not a decoded mode map. Runtime point normalization does not define disk encoding. Containment can suggest lane owner but cannot identify the automated parameter. |
| MIDI events and attributes | `MMidiTrackEvent` rev 4 and `MInstrumentTrackEvent` rev 4 share a callback in the prior trace; `MScoreEvent` and typed/count-stream attribute branches have narrow readers already. The export confirms revision-gated paths and named properties such as `solo`, `soloDefeat`, and `isGrooveTrack`. | Trace remaining MEvent/attribute branches, legacy revisions, note transforms/overrides, part placement and clip offsets. Build paired MIDI projects and compare with exported MIDI, including moved/duplicate notes and attribute overrides. | Callback presence does not validate inferred notes, timebase, pitch/velocity, ownership, or older layouts. Keep unsupported data bounded; never resynchronize through unknown payload bytes. |
| Tempo, signature, markers and musical time | `MTempoEvent` rev 0 and `MTempoTrackEvent` rev 3 register near lines 5,175,823–5,175,833; signature and marker readers have dedicated paths. Existing work traced fixed tempo/signature prefixes and tempo-track array widths. | Trace marker helper/container bounds; compare deliberate tempo/signature/marker edits and Cubase exports. Determine conversions among ticks, samples, seconds, bars/beats and event-local positions. | Raw fixed-width values and a tempo-track stride are not semantic units. Do not label candidates BPM, numerator/denominator, marker positions, or ticks without paired comparisons. |
| Version and dispatch compatibility | Class registrations carry serializer revisions; callbacks branch on active class version. Project header separately carries application/version hints. | Connect each class revision/property branch to genuine projects saved by older releases. Check class dispatch, archive framing, optional properties, and inherited/base callbacks separately. | Serializer revision is not Cubase release number. Cubase 15 opening an older project does not mean this reader supports its full grammar. No universal Cubase 5–15 coverage follows from the modern executable. |

## High-value next trace order

1. **Track identity and nesting:** trace `MTrack` reader/writer and arrangement/track-list serializer; identify explicit fields and keep hierarchy, order, and class payload separate.
2. **Audio event/part/file readers:** resolve the callback behind each registered audio class; document bounded reads and exact nested references before adding fields to the reader.
3. **Route object grammar:** inspect send/direct-routing child readers and bus ID property types; establish slot order and saved state with controlled single-change saves.
4. **Plug-in slot boundaries:** parse only fully bounded `APluginSlot` records, retaining raw UID/name/state evidence and avoiding plug-in loading.
5. **Automation target chain:** trace `MParamEvent`, base event, and target getter/reference callbacks; do not reinterpret the lane scalar as ownership.
6. **Controlled semantic matrix:** change one UI property at a time, inspect saved bytes and Cubase's UI/export, then repeat at representative class revisions.

## Follow-up trace: routing folders and audio-track children

The callbacks make the routing-folder lead more specific:

- `FUN_143513190` (lines 10,340,990–10,341,189) sends `SendFolder` and
  `directRoutingFolder` to `FUN_143518a00` with mode tags **3** and **2**.
- `FUN_143518a00` (lines 10,344,975–10,345,025) enumerates each child under
  the folder's `Output` key, reads a scalar with `FUN_14355fb90`, discards zero,
  and records the scalar paired with the supplied mode. This directly shows
  per-child saved `Output` values and distinct folder tags in this collection
  callback. It does not establish send slot index, enabled/bypass state, send
  level, pre/post-fader tap, or that this flattened collection is the whole
  serialized slot grammar.
- A separate path `FUN_143518b40` (lines 10,345,029–10,345,081) iterates
  `ASendSlot` objects, looks up `VSTBusValue`, and checks a `VstProcessChannel`
  object before forwarding a value. This is useful evidence that send slots can
  carry a typed bus-value object, but it is a separate callback path; do not
  conflate its `VSTBusValue` with the `Output` scalar without tracing how the
  callbacks are connected to the project serializer.
- The channel writer emits `foldbackSendFolder` (line 10,399,676), but the
  traced `FUN_143513190` reader does not request it. The bounded named-record
  reader now retains fields under that exact path as raw candidate evidence;
  no foldback route or slot semantics are inferred from this writer-only lead.
- `FUN_14370f840` (lines 10,686,225–10,686,230) is a thin helper for the
  `Bus UID` property. `FUN_14370f880` first requires that UID, then processes a
  `Child Bus` collection and device-name/arrangement-related values. Its writer
  side reads `Parent Bus UID`, `Bus Type`, `Input Arrangement`, and
  `Output Arrangement` (lines 10,686,570–10,686,694). `Bus Type` and
  arrangement payloads remain raw candidates; their numeric/string mappings
  still need validation.

The audio-track-event callback also narrows the event nesting picture:

- `FUN_141e85b90` (lines 6,344,416–6,344,551) has a branch that reads an
  `MAutoFadeSetting` child and another branch that obtains children from a
  container at indexes 0 and 1, class-checks them as `MAudioTrackEvent` and
  `MAutomationTrackEvent`, and attaches them through runtime virtual calls.
  Its writer-side `FUN_141e85f50` writes one class child through an object
  writer; `FUN_141e85fb0`/`FUN_141e86020` use the key `Autofade Settings` and
  class `MAutoFadeSetting` (lines 6,344,556–6,344,604).
- This establishes that the callback supports these nested object classes and
  the auto-fade child key. It does not give the nested event bodies' full byte
  grammar, prove the meaning of child positions 0/1 as a stable ordering rule,
  decode fades, or connect an event to a media-pool file. The fallback path
  also invokes the base `MTrackEvent`-style reader and accesses fade-related
  in-memory fields; those are not all direct saved properties.

The `MTrack` registration/factory (`FUN_1431ed1d0`, lines 9,804,554 onward)
confirms a native class revision 2 and object construction, but the local C
export pass has not yet reliably associated its primary vtable callback slots
with disk reader/writer functions. A concrete container path is now visible:
`MTrackList` is rev 0 (registration at line 5,179,242); reader
`FUN_1431fd9a0` repeatedly obtains `Tracks` children as `MEvent` and appends
each to the list at object offset `+0x98`; writer `FUN_1431fd900` enumerates
the internal list and emits `Tracks` children. This is direct evidence for a
typed repeated track-event collection and preserves a serialized traversal
sequence. It is not yet evidence that the sequence is the visible arrangement
order, nor does it expose a universal ID, folder-parent, or all-track-kind
grammar. Concrete child event readers and `MTrack`'s callback remain next.

The bounded reader now associates a recognized track-class record with its
exactly containing `MTrackList` header when that is its immediate class-record
parent. For complete archive inventories, it also retains the zero-based
ordinal and count among recognized direct track-class children in source order.
These fields are exposed as candidate track-list provenance and in technical
details. No `ParentTrackId` is populated from this association, and no visible
track ordering is asserted. Synthetic RIFF/RIF2 tests exercise membership,
ordinal, and snapshot propagation; independent native projects still need to
confirm how this collection order relates to the Cubase project window.

### Folder-owned track-list candidate

The decompile gives a specific runtime association for folders: `MFolderTrack`
class support is registered around line 5,180,797; its runtime getter at vtable
slot `+0x230` is repeatedly used as an `MTrackList` and type-checked at call
sites such as lines 6,906,834–6,906,840 and 10,867,047–10,867,054. Its object
copy/cleanup code also handles that returned list (lines 9,844,271–9,844,298).
This makes a serialized `MFolderTrack` → `MTrackList` → track-record containment
chain a well-motivated candidate for folder membership.

The reader now records `CandidateFolderTrackOffset` only when the immediate
parent of a candidate track is an `MTrackList` and that list's immediate class
record parent is an `MFolderTrack`. It reports the exact header offset and
does not set `ParentTrackId` or assert visible folder membership. The
decompilation sites above establish the runtime list relationship, but do not
by themselves establish that this exact nested class-header chain is how every
saved project revision serializes a folder's children. Synthetic RIFF/RIF2
tests prove that the bounded parser preserves only the exact constructed
containment chain; native folder projects and Cubase UI/export comparison are
still required before promoting it to hierarchy.

## Scope and validation status

This is a static research map, not a complete disassembly audit. The C source is
decompiler output and contains runtime code beside archive callbacks; function
names and line numbers are local labels, and registration/factory strings alone
do not prove serialized grammar. Existing parser behavior and stricter evidence
boundaries remain authoritative until implementation is independently
validated. The exact-containment folder candidate and raw foldback-field
retention changes passed the Release test suite (670 passed, 2 optional
private-fixture skips), the CLI Release build (0 warnings/errors), and a
self-contained Windows x64 Uno production publish. Live visual/interaction
acceptance and native folder-project comparison remain pending. Foldback
reader/slot grammar and native comparison also remain open. No native write
path or plug-in execution is implied.
