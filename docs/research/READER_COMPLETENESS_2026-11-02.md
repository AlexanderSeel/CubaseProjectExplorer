# Native reader completeness: Cubase 15 static pass

Date: 2026-11-02. This is a read-only static-analysis pass over the user-provided
`E:\Cubase15.exe.asm`. Neither the executable nor any project file was run or
modified. Procedure excerpts were written only under the ignored `artifacts/`
directory.

Continuation: [nested attribute/object framing](CUBASE15_NESTED_ATTRIBUTES.md)
extends counted streams with two opaque object-valued cases. The current full
implemented/open inventory is [READER_FEATURES.md](../READER_FEATURES.md).

## Input and reproducibility

The assembly export is 1,079,140,046 bytes. The repository's
`inspect_cubase15_ida_export.py` accepted its header's executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0` and
returned complete, bounded excerpts for 17 selected routines: archive
construction, active/named revision lookup, class/object/declaration readers,
MIDI event/note/part readers, packed-event factory, tempo/time-signature/marker
readers, MIDI/audio track readers, and the plug-in slot reader/loader. Each local
excerpt has a byte range and SHA-256 in the script output. The header identifies
the executable but does not authenticate the assembly export; all addresses and
observations remain specific to this Cubase 15.0.40 build.

Re-run the selected pass with:

```powershell
python scripts/inspect_cubase15_ida_export.py E:\Cubase15.exe.asm `
  --function archive-constructor --function archive-current-version `
  --function archive-named-version --function archive-class-reader `
  --function archive-object-reader --function archive-declaration-reader `
  --function midi-event-reader --function midi-note-reader `
  --function midi-part-reader --function midi-packed-event-factory `
  --function tempo-track-reader --function time-signature-reader `
  --function marker-reader --function midi-track-reader `
  --function audio-track-reader --function plugin-slot-reader `
  --function plugin-slot-loader --output artifacts/cubase15-deep-reader-pass
```

## What the pass supports

The direct callbacks confirm that the reader needs independent class revisions
for inherited objects. `MMidiEvent` revisions 0–2 change the base body from
`f64,u8,u8,u8` to an optional `u16`, then a counted typed-attribute collection.
`MMidiNote` revisions 0–1 independently control its final byte after three
`f64` values. `MMidiTrackEvent` also has several distinct active-versus-named
revision gates, while `MAudioTrackEvent` has a legacy conversion path and adds
an auto-fade child only on its later path. A single project-version switch or a
single derived-class revision cannot safely select all these layouts.

The current reader already applies those limits conservatively: it retains
class declarations and raw note primitives when the exact revisions and body
shape are available, and only emits musical-field note candidates for the
fixture-backed `MMidiEvent` v2 / `MMidiNote` v1 combination. Counted MIDI part
scanning is bounded to supported part revisions and known event discriminators;
unsupported event bodies remain uninterpreted. A subsequent
[factory/vtable/callback pass](CUBASE15_PACKED_EVENT_CALLBACKS.md) traced 31
additional export procedures and seven vtables in the hashed executable, with
15 callback/helper ranges independently disassembled from that binary. It
establishes bounded skip grammar for poly pressure, program change, SysEx, chord
and note-change events with revision-0 declarations and supported scalar
attributes. This lets later note candidates remain reachable without inventing
values for the skipped event bodies. SysEx accepts only duplicated bounded
lengths and retains no payload. Score events use a different object/reference
grammar and still stop with preceding notes retained as partial; unsupported
class revisions, attributes and payloads also remain explicit. In a paired
80-file read-only sample, candidate notes rose by 260 and arrangement-level
partial flags fell from two files to zero, with all source hashes unchanged.
Those structural results do not verify musical values or full project loading.

Tempo and time-signature work similarly exposes bounded raw fields without
promoting callback offsets to BPM, bar positions, or normalized values without
independent evidence.

The archive callbacks distinguish active revision lookup from named-class
revision lookup. The latter searches archive declarations by exact class name;
it is not equivalent to taking the revision on the nearest object header. The
reader's bounded inventory therefore preserves declaration location, ambiguity,
and scan limits instead of assigning missing metadata revision zero.

## Reader coverage gaps

| Area | Current evidence in the app | Work needed before calling it fully loaded |
| --- | --- | --- |
| Container and archive | RIFF/RIF2 framing probes, bounded ROOT/ARCH inventories, class-header and declaration candidates | Validate all nesting, references, compression and historical variants against versioned fixtures; inventory limits can omit evidence. |
| Track objects | Candidate class, stored-name, bus, port and selected mixer observations | Implement inherited serializer gates and object-reference traversal; verify IDs, names, hierarchy, order and visible track kinds against Cubase. |
| MIDI | Bounded part/event scans, raw class-revision fields, fixture-backed note candidates | Validate legacy bodies and mixed events, object/timebase references, clip placement and ownership. No candidate timing becomes a normalized Cubase timeline yet. |
| Plug-ins | Stored UID/name and selected slot fields as candidates | Decode slot relationships, identity, format, enabled state and state handles independently; opaque plug-in state must stay opaque, and plugin DLLs must never be executed. |
| Audio/media | Selected path-like observations and export-backed references | Trace event/part/media references and generations; prove ownership before associating a CPR path with a track or exporting/collecting it. |
| Routing and automation | Saved bus/port values and candidate relationships; ASM target-object leads | Decode reference identity and automation target grammar; verify buses, sends, sidechains and tap order against controlled projects. |
| Compatibility | A Cubase 15.0.40 callback map plus a bounded local sample spanning several generations | Map each class revision and framing path to genuine versioned CPR/BAK fixtures. One Cubase 15 executable cannot prove SX–14 or future-version compatibility. |

Static reverse engineering identifies serialization branches; it does not
establish what every field means in a saved project or guarantee Cubase's full
import behavior. The next implementation should be selected from a field-by-field
gap with matching fixtures, not by broadening the parser from nearby bytes or
class names. Keep originals unchanged and never serialize or rewrite CPR files
without independent Cubase round-trip validation.
