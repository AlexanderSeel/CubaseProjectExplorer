# Reader features and open work

Updated: 2026-11-04. This is the reader-specific inventory. Product/UI features
are listed separately in [FEATURE_MATRIX.md](FEATURE_MATRIX.md); execution order
is in [PLAN.md](../PLAN.md).

**Candidate** means observed native bytes/structure, without verified musical
meaning or ownership. **Declared** means a field explicitly present in a
DAWproject export. **Opaque** means only a supported boundary is consumed; its
state is not decoded or applied. **Open** means the feature is not implemented
as a complete semantic reader. Structural success never means a full Cubase load.

## Formats, framing and archive infrastructure

| Feature | Implemented | Open / limits |
| --- | --- | --- |
| CPR recognition | RIFF/32-bit and RIF2/64-bit paths, Cubase/NUND checks and bounded fallback evidence | Other historical envelopes, compression and unsupported container variants |
| BAK recognition | Native-signature validation and read-only inspection; backup provenance warning | Crash recovery, repair and proof that a backup is complete |
| DAWproject recognition | ZIP with a single required `project.xml`; optional `metadata.xml` | Unsupported producers/schema variants require independent export fixtures |
| Saved-by version | PAppVersion/version candidates and bounded printable fallback | Missing/ambiguous/legacy version paths; version text cannot choose a serializer layout |
| ROOT / ARCH chunks | Bounded chunk boundaries, stream/class names and declared/scanned sizes | Complete nested archive semantics, unknown chunks and compressed payloads |
| Class declarations | Inline names, prefixes, interned symbols, raw class revisions and declaration availability offsets | All historical grammar variants and unsupported reference forms |
| Class references | Earlier symbols and bounded direct-declaration recovery; missing/conflicting evidence remains unresolved | General lazy/recursive declaration loading and complete archive object graph |
| Active versus named revisions | ASM distinction documented; modern note paths require separate named base/note revisions | Full inherited-track/audio/automation implementation and every active-revision gate |
| Archive inventory | Recognized class/header/body spans, containment candidates, class counts, raw fields, unknown ranges and explicit budgets | Inventory is a scan, not deserialization; detail retention can omit recognized headers |
| Object references | Null/earlier references in supported counted-part fields; selected nested attribute references | Forward, unresolved, ambiguous and general cyclic object graphs remain unsupported |
| Bounded nested-header reads | Exact-cursor interned headers for supported attribute classes can use earlier declarations when inventory details were omitted | No searching for plausible headers; other omitted header/preamble cases remain limited |
| Evidence / diagnostics | Source offsets/layouts, revision evidence, byte/count limits, explicit unsupported attributes and partial reads | A complete per-field coverage map and independently verified semantics |
| Malformed input | Bounded counts/lengths/depth, cancellation, finite numeric checks and typed failures/warnings | Broader mutation/fuzz corpus, memory/time measurements and every legacy edge case |

## Native MIDI: boundaries and raw observations

| Feature | Implemented | Open / limits |
| --- | --- | --- |
| Counted MMidiPart lists | Part revisions 1/2, supported name/timebase preamble, event count and declarations, exact list ending | Other revisions and full timebase-object semantics |
| Modern note fields | MMidiEvent v2 / MMidiNote v1 candidates: stored position/duration doubles, pitch, velocity, channel and raw flags | Cubase-visible values after attribute overrides; normalized musical units |
| Zero-attribute packets | Supported inside established counted streams | Not enabled as a loose byte-pattern search |
| Scalar attributes | Exact type flags 1/4 and bounded 14-byte item layout with printable IDs | Non-ASCII IDs remain conservatively unsupported; values are not generally applied |
| ScAr object attributes | ID `0x53634172`, flags `0x0022`, framed `CmAttribCollection` rev0; supported null/back references | Collection contents, score/text/articulation meaning and attribute overrides remain opaque |
| bOuc object attributes | ID `0x624F7563`, flags `0x0002`, framed `MParamEventCurveRationalBezier` rev0 with named base rev0; supported base prefix | Curve points, interpolation and target meaning remain opaque |
| String attributes | Printable four-byte ID, exact flags `0x0008`, framed `CmString` rev0 or supported null/earlier reference; body stays opaque | String contents, other flags/revisions and applied meaning |
| Other object/container attributes | First unsupported item reported with ID, flags and exact source offset | General object codecs, container branches, other IDs/flags/classes/revisions |
| Typed MMidiNote records | Modern scalar and supported opaque ScAr/bOuc/CmString attributes share counted-stream framing; exact suffix and named-revision gates; nested note-like values stay out of arrangement candidates | Other objects/strings/containers, attribute overrides and independent genuine typed-object comparisons |
| Legacy base/note layouts | MMidiEvent rev0–2 / MMidiNote rev0–1 callback shapes and anonymous raw fields | Genuine older-revision fixtures and musical interpretation; no guessed v0 fallback |
| Bounded packed-pattern fallback | Existing separate candidate scanner for supported scalar patterns; opaque ranges excluded | Heuristic evidence is weaker than counted framing and cannot establish a complete stream |
| Part / possible-track context | Structural names, enclosing offsets and source-layout grouping | Verified track identity, hierarchy, part ownership and ordering |
| Numeric preservation | Finite original doubles, negative positions, zero velocity, nullable channel/flags and source offsets | No rounding/quantization in evidence; `Ticks` property names do not establish units |
| Clip placement | Stored event-level values retained | Absolute placement, offsets, loops, repeats, transpose and cross-part time domains |
| MIDI export | Native export remains unavailable | Enable only after timing, placement, ownership and values are independently verified |

Supported mixed-event boundaries below require the existing modern base revision
and the event class's supported named revision. Consuming a body does not produce
a normalized controller, chord, expression or score event.

| Discriminator | Class | Boundary behavior | Still open |
| --- | --- | --- | --- |
| `0x80`, `0x90` | MMidiNote | Raw note-field candidates with complete revision-1 suffix | Final semantic state and legacy revisions |
| `0xA0` | MMidiPolyPressure rev0 | Shared base/attribute boundary, opaque values | Normalized pressure/curve values |
| `0xB0` | MMidiController rev0 | Shared base/attribute boundary, opaque values | Controller numbers, values and target semantics |
| `0xC0` | MMidiProgramChange rev0 | Shared base/attribute boundary, opaque values | Program/bank interpretation |
| `0xD0` | MMidiAfterTouch rev0 | Shared base/attribute boundary, opaque values | Normalized aftertouch |
| `0xE0` | MMidiPitchBend rev0 | Shared base/attribute boundary, opaque values | Range, units and normalized bend |
| `0xF0` | MMidiSysex rev0 | Nonnegative bounded signed byte count, opaque second word and payload; unequal words accepted | Single-word forms and exported SysEx meaning |
| `0x70` | MChordEvent rev0 | Note-like suffix plus one byte; opaque | Chord identity, voicing and musical state |
| `0x40` | MNoteChangeEvent rev0 | Shared base plus u16/u32/f64 suffix; opaque | Target identity and change semantics |
| `0x30` | MScoreEvent rev1 | Distinct flags/scalars/null or exact MDataNode object/reference boundary with earlier named MEvent v0/v1; node body stays opaque; v1 uses the shared supported scalar/object/string attribute boundaries; suffix stays opaque | Other node classes/revisions, unsupported attributes, notation/articulation semantics and other score revisions |
| `0x00` | Escaped object/reference | Supported class/body or reference extent; separate typed-note pass avoids duplicate extraction | General object loading and unsupported nested typed notes |
| Other values / revisions | Unknown | Explicit partial stop | Verify factory/callback/revision before adding support |

No counted-stream failure permits resynchronization through an opaque payload.
Unsupported events/attributes and SysEx extent failures retain preceding complete
candidates as explicit partial evidence. Structural count/trailer failures reject
the prefix; byte/note budgets also retain preceding complete observations.

## Native project entities and musical state

| Area | Available today | Open |
| --- | --- | --- |
| Track classes and list context | Candidate Audio, MIDI, Instrument, Sampler, Folder, Device, Marker, Tempo, Signature, Automation, Chord, Drum, Video, Play-range and Loudness labels; exact-contained `MTrackList` parent offset and ordinal among recognized direct track-class children when the archive inventory is complete; exact `MFolderTrack` list-owner offset when present | Actual track identity, visible order, verified folder hierarchy, colors, channel ownership and complete inherited bodies; list membership/order/owner remain candidate byte-containment evidence |
| Names and identifiers | Stored-name/UID/property candidates and class/source offsets | Stable normalized IDs and verified owner relationships |
| Tempo | Selected candidates and raw callback-backed fields | Full tempo map, ramps, time domains, attribute overrides and BPM verification |
| Meter | Selected signature candidates; anonymous 20-byte `MTimeSignatureEvent` prefix for explicit revisions 0–2, with raw bits, offsets and bounded retention | Genuine revision-0/1 validation, attribute meaning, signature changes, bar positions and complete musical mapping |
| Markers / ranges | Recognized classes; bounded `MMarkerEvent` revision-0 string/scalar prefix with exact raw bytes, ASCII/BOM-UTF-8 renderings and explicit retention limits | Genuine marker fixture validation, inherited body, verified marker/cycle/range names, IDs, positions and relationships |
| Session metadata | Selected saved settings such as sample-rate/size or tempo/signature hints when observed | Authoritative complete settings, song metadata and all legacy variants |
| Audio references | Nested path candidates and possible enclosing track context | Verified media-pool IDs, ownership, clip placement, file identity and availability |
| Audio events / parts | Structural records and selected metadata only | Offsets, lengths, fades, envelopes, stretching/warp, lanes, takes, comping and edits |
| Plug-in references | Stored UID/name tokens, selected slot properties and advisory correlations | Verified product/format/instance identity; candidates are not installed VST3 IDs |
| Plug-in ownership / order | Candidate location/slot relationships | Exact channel ownership, insert order, bypass/enabled state and sidechains |
| Plug-in state | State remains opaque | Presets, handles/chunks, parameter semantics and Cubase reconstruction; never execute plug-in binaries |
| Mixer | Selected typed volume/mute/solo, insert, send and EQ observations | Complete gain/pan/channel state, units, defaults and all revision paths |
| Logical routing | Explicit saved bus/port/send reference candidates with source evidence; bounded `foldbackSendFolder` fields retained as raw candidates | Verified endpoint identity, foldback slot grammar, graph completeness, groups/FX and sidechain semantics |
| Sends | Selected saved destinations/fields | Levels, tap points, pre/post behavior, enabled state and ownership |
| EQ | Selected typed band fields | Placement, defaults, complete band semantics and Cubase-visible validation |
| Physical I/O | Saved named assignments separated from unresolved references; ambiguous device names retained; nonzero explicit bus-reference candidates; zero selections retained only as stored fields | Cross-archive identities, unclassified port strings, hardware presence/state and verified identity |
| Automation | Separate lane records with candidate parent IDs, source bounds and provenance; declared export parents supported; anonymous bounded MAutomationTrack revision-0/1 counted prefixes and revision-2 words with exact raw bytes; no standalone automation tracks | Parameter targets, points, interpolation, modes, inherited object grammar, genuine legacy bodies and independent owner validation beyond byte containment |
| Expression / articulations / notation | Some containing objects can be skipped with evidence | Note-expression curves, score, articulation mapping and applied musical state |
| Global project completeness | Supported observations plus explicit unknown/partial evidence | A full Cubase-equivalent project loader remains open |

## Structured DAWproject reader

Confirmation applies to fields declared in the export, not to all Cubase state.

| Feature | Implemented declared subset | Open / limits |
| --- | --- | --- |
| ZIP/XML safety | Forward-only XML, no XML tree/media/plugin loading, duplicate project entry checks, entry/size/ratio/depth/item limits | Additional hostile/archive interoperability fixtures |
| Tracks / channels | IDs, hierarchy/context, names/types, declared channel and mixer fields | Producer-specific nesting and full reconciliation against Cubase |
| Plug-ins | Declared format/class/name/vendor, owner/slot/enabled/state-reference fields where present | Ambiguous/missing IDs, duplicates, arbitrary device/state semantics |
| Routing / sends | Declared logical endpoints, selected volume/enabled values | Complete sidechains, tap semantics and malformed/missing ownership cases |
| Tempo / meter | Declared values; tempo-automation presence detection | Complete tempo-automation interpretation and mixed time domains |
| Audio media | Declared paths, external/embedded flag and available channel/rate/duration fields | Decoding audio content and proving successful DAW playback |
| Timeline | Supported clip/marker records and declared positions/context | Full nested clip transforms, repetitions, warping and arrangement reconstruction |
| MIDI notes | Supported flat declared notes, track references/time units and nested-clip flag | Complete clip flattening, mixed timebases, expression and automation |
| EQ / warp | Selected declared bands, values/units and warp coordinates with provenance | All devices, defaults, nesting and producer variants |
| Song metadata | Supported fields in metadata.xml, independently bounded | Additional fields/schema variants and external metadata conflicts |
| Normalized result | Immutable ProjectSnapshot records shared with reports, indexes and comparison | Full interchange coverage and per-producer semantic fixtures |

## Outputs, compatibility and safety

| Capability | Status / boundary |
| --- | --- |
| Reader API | IProjectReader / ProjectInspector return typed results and immutable snapshots; native evidence remains candidate-level |
| Structural report | CPR chunks, archive inventories, declarations, raw fields, arrangement hints and explicit limits |
| Plug-in diagnostics | Separate bounded streaming UID/name/property observations and source offsets; no definitive native identity claims |
| CLI / reports | inspect, cpr-structure and cpr-plugins expose JSON/Markdown/CSV as supported; field/evidence coverage stays separate from completeness |
| Database indexes | Rebuildable summaries plus compact v2 asset-index JSON; legacy reads preserve provenance and numeric precision |
| Original files | Read-only; source-hash comparison used for genuine-file validation; no native writer/repair/plugin disabling |
| Backward compatibility | Selected SX1–3 and Cubase 5–15 files have structural evidence; no universal release guarantee |
| Test-fixture gaps | Missing representative releases/features and genuine MMidiEvent 0/1 or MMidiNote 0 bodies; backups are correlated fixtures |
| Future versions | Unknown revisions remain raw/partial; no project-version guess selects a new body layout |
| ARR / ALL | Separate unimplemented formats; never sent through the CPR reader |

## Next reader work

1. Complete remaining object/string/container attributes and reference forms
   beyond the shared typed/count-stream subset, with independent framing evidence.
2. Extend MScoreEvent beyond the exact MDataNode/null-node and supported inherited-attribute
   boundary subset and complete other mixed-event bodies without scanning unknown data.
3. Validate raw note values after attribute overrides, musical timebase, placement
   and ownership against controlled Cubase projects/exports.
4. Complete inherited track/audio/plug-in/routing/automation objects field by field.
5. Expand versioned semantic fixtures, malformed-input coverage and large-file
   resource measurements; only then broaden compatibility/completeness claims.

The [marker prefix follow-up](research/CUBASE15_C_MARKER_PREFIX.md) records
string/scalar evidence, literal report output and measured tempo-field allocations.
The [signature revision follow-up](research/CUBASE15_C_SIGNATURE_REVISIONS.md)
records the shared-prefix evidence, retention optimization and validation limits.
The [integration checkpoint](research/READER_INTEGRATION_2026-11-04.md) records
the combined reader validation and resolves earlier SysEx length-policy differences.
The [score boundary research](research/SCORE_EVENT_BOUNDARIES_2026-11-02.md)
documents the original bounded score subset and further test retirement. The
[typed-note extension and test audit](research/TYPED_NOTE_ATTRIBUTES_TEST_AUDIT_2026-11-02.md)
records the latest implementation and validation. Automation/I/O findings are in
[automation and I/O research](research/AUTOMATION_IO_2026-11-02.md) and
[the raw automation-prefix follow-up](research/CUBASE15_C_AUTOMATION_PREFIXES.md); MIDI framing
and note-count deltas are in
[nested-attribute research](research/CUBASE15_NESTED_ATTRIBUTES.md). Foundational
evidence: [inherited serializers](research/CUBASE15_BACKWARD_COMPATIBILITY.md),
[counted MIDI](research/CUBASE15_MIDI_STREAM_COMPATIBILITY.md),
[mixed callbacks](research/CUBASE15_PACKED_EVENT_CALLBACKS.md), and the
[original demo baseline](research/DEMOS_COMPATIBILITY_2026-11-02.md).
