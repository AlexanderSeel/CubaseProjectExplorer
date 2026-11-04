# Implementation plan

<<<<<<< main
Updated: 2026-11-04
=======
Updated: 2026-11-03. Work on `main`. This file contains open work only.
Implemented reader capabilities and exact limits live in [READER_FEATURES.md](docs/READER_FEATURES.md).
The wider product inventory is in [FEATURE_MATRIX.md](docs/FEATURE_MATRIX.md).
Current decompile leads and unresolved reader candidates: [Cubase 15 gap map](docs/research/CUBASE15_DECOMPILE_GAP_MAP_2026-11-03.md).
>>>>>>> origin/main

## P0 — Reader correctness and completeness

- [ ] Decode automation parameter targets/modes and independently validate owner
  relationships beyond the implemented candidate byte-containment association.
- [ ] Resolve remaining cross-archive I/O bus identities and unclassified port
  strings; validate actual hardware mappings without inferring IDs from names.
  Foldback-send writer fields are retained raw, while their reader/slot grammar
  and route semantics remain open. Current serializer findings and validation:
  [automation/I/O research](docs/research/AUTOMATION_IO_2026-11-02.md) and the
  [Cubase 15 gap map](docs/research/CUBASE15_DECOMPILE_GAP_MAP_2026-11-03.md).

<<<<<<< main
- [ ] **Validate counted native MIDI streams across Cubase generations.** The [counted-stream ASM investigation](docs/research/CUBASE15_MIDI_STREAM_COMPATIBILITY.md) traces `MMidiPart` revisions 1/2, its count/declaration preamble, exact event factory discriminators, and the shared base grammar. A bounded reader now accepts complete supported lists, including zero-attribute 43-byte note packets observed in SX1–3, and skips supported scalar controller/aftertouch/pitch-bend packets without assigning them musical values. The [C-export follow-up](docs/research/CUBASE15_C_MIXED_MIDI.md) adds exact-boundary poly-pressure, program-change and length-delimited SysEx traversal with revision checks; SysEx bytes are skipped without payload allocation. Trailing supported non-note events no longer falsely consume the note budget, and archives without MIDI parts avoid MIDI lookup-table allocation. Full-object escapes keep their separate structural evidence. Unsupported grammar cannot create zero-attribute candidates through byte resynchronization; normal note/byte budgets retain partial candidate results explicitly. Inventory and arrangement traversal now share one transactional class-header reader. Independently verify the new candidate values against Cubase/exports, obtain genuine poly-pressure/SysEx mixed-stream fixtures, expand legacy revision-0 coverage, and validate remaining timebase/object/string codecs before normalized timing or native MIDI export is enabled.
=======
- [ ] Cover remaining event-attribute object, string and container branches,
  reference forms and revision gates beyond the shared typed/count-stream subset.
  Current implementation and test cleanup: [typed-note research](docs/research/TYPED_NOTE_ATTRIBUTES_TEST_AUDIT_2026-11-02.md).
- [ ] Extend MScoreEvent beyond its null or exact MDataNode object/reference
  boundary and empty-attribute subset; complete the remaining mixed-event bodies.
  Keep unsupported payloads opaque; never resynchronize through unknown bytes.
  Current evidence: [score boundary research](docs/research/SCORE_EVENT_BOUNDARIES_2026-11-02.md).
- [ ] Verify note values after attribute overrides, timebase, part placement,
  clip offsets/loops and track ownership against controlled Cubase/export pairs.
- [ ] Validate genuine MMidiEvent v0/v1 and MMidiNote v0 fixtures before interpreting
  their raw fields; complete active/named inherited serializer handling.
- [ ] Finish track IDs/hierarchy/order, audio event/part/pool relationships,
  plug-in instance/slot/state references, routing/sends/EQ and automation targets.
  Native `MTrackList` membership and recognized-child sequence are now surfaced
  as candidates, including exact-containment `MFolderTrack` list-owner offsets;
  visible order, verified parent/folder relationships and stable IDs remain
  open. Detailed gaps and C-export anchors are in the [decompile gap map](docs/research/CUBASE15_DECOMPILE_GAP_MAP_2026-11-03.md).
- [ ] Complete tempo/signature maps, markers and shared musical time domains.
- [ ] Expand versioned SX/Cubase 5–15 project/backup fixtures and independent
  semantic comparisons; fill release/feature gaps without committing source projects.
- [ ] Add malformed/reference/cycle/count/length tests and measure large-file
  memory, time, cancellation and scan coverage for each new codec.
>>>>>>> origin/main

Reader acceptance: every supported field has provenance and independent comparison
where musical meaning is asserted. Unknown data stays unknown. Successful bounded
scans do not establish full loading or universal backward compatibility.

<<<<<<< main
- [ ] **Build a versioned Cubase validation corpus.** The user authorized read-only analysis of the local library and song collection; no originals were changed and no private project data was added to the repository. A bounded metadata sample contains Cubase SX and Cubase 5–15 saved-by text, including CPR/BAK records from Cubase 10–15. Size-bounded CPR/BAK samples were read and compared with the v15 ASM: observed `MMidiTrackEvent` revision 3 in Cubase 10–13 examples and revision 4 in 14/15 examples; `MMidiNote` revision 1 in selected 11–15 backups/projects; and older `MAudioTrackEvent`/`MAudioTrack` revisions in selected 10/11 files. CPRs labeled Cubase 14.0.10 appeared in both RIFF and RIF2, so framing must follow the file signature rather than the release string. A Cubase 13.0.40 sample reached the 10,000-note bound and was marked partial. This confirms cross-generation records reach the reader and v15 callback gates are relevant; bounded samples do not establish a release-wide schema. Continue stratifying representative files and compare interpreted fields with Cubase-visible state. Do not commit private projects.
- [ ] **Read native projects consistently across generations.** Validate RIFF/RIF2 framing, byte order, chunk bounds, compression, nesting, references and crash-backup behavior. Project Analyze re-runs the reader and refreshes the selected project’s audio/MIDI views; supported CPR note, audio-path, mixer, stored plug-in UID/name, and Cubase-15-ASM-backed track/lane class observations are shown in their relevant workflows with source offsets. Track serializer version, body size and header encoding are now attached only when the bounded arrangement scanner and archive inventory agree on exact class name and header offset, and are revealed in technical details. The reader follows Cubase 15 registrations through MMidiTrackEvent, MAudioTrackEvent, MInstrumentTrackEvent and APluginSlot serializer callbacks; matching bounded track class headers are presented as recognized serialized classes, not the blanket “Unverified CPR record.” A deeper callback pass confirmed that the v15 binary contains backward-compatible class-revision branches: MMidiTrackEvent gates fields across revisions 0–4, MMidiNote revision 0 omits a byte added by revision 1, MAudioTrackEvent revision 0 omits its later auto-fade child, and the PAppVersion path has a legacy fallback when version metadata is missing. The arrangement scanner now preserves class revision through note-record parsing and uses the known revision 1 MMidiNote layout only; other revisions remain raw and produce a reader limitation. Further revision-specific semantics and mapping to Cubase 10–14 releases still require implementation and comparison with known-version project fixtures. Typed UID/name property matches are shown as stored plug-in fields found. Extracted names/visibility/hierarchy, plug-in identity/format/owner/state, MIDI timebase and signal routing remain unresolved until independently validated across versions. Preserve bounded structural and token evidence for unsupported files, and show malformed, unsupported, unknown and field-specific candidate data distinctly.
- [ ] **Establish native project structure field by field.** Verify track identity/hierarchy/order, plugin instances and ownership, MIDI, tempo, mixer, sends, routing and logical I/O against controlled Cubase projects before presenting them as confirmed. Native signal flow reads supported OwnInputBus/Bus UID, InputBusValue, OutputBus/Value, OutputBusValue, per-slot send destinations, distinct direct-routing and RecorderBus references, child-bus IDs, root bus definitions (`Bus UID` + `Bus Type`), `Parent Bus UID`, and the ASM-backed `postInsertsPreFader` property. Unique bus IDs resolve only inside the same source stream; duplicates and absent targets remain unresolved. Bus Type and arrangements are shown as saved values without guessed enum labels. Exact byte containment now labels mixer channel nodes with their owning arrangement track, and Tracks & Channels always attempts the saved native output route even when CPR has no declared `DestinationChannelId`. The named-record reader accepts both nested `Name/String` and scalar `Name` records and prioritizes bus definitions plus saved arrangement and track/device port fields. Exact-containment Input Device Name/ID and Input Port Name values now appear as a saved track input in Tracks & Channels and the Studio map; no current hardware availability is inferred. The diagram links indexed insert slots to a channel output stage, then traces resolved bus/FX/output endpoints; insert-position values and saved EQ values remain individually inspectable without inventing EQ defaults, curve shape, or send tap order. Source paths, offsets, evidence and unresolved bus IDs remain visible. ASM follow-up shows an automation event targeting an `MAutomationTrack`/`VstCtrlEffect` control path, but the serialized target grammar is not decoded; keep automation lanes distinct from mixer channels until an explicit reference can be read. This is a reader interpretation from bounded CPR fields and Cubase 15 ASM leads, not a version-independent signal model. The ASM contains old-version compatibility branches and a `PAppVersion` fallback, but neither proves complete pre-10 `.cpr` reader coverage. Validate field grammar and each relationship across Cubase generations, including actual pre-10 fixtures.
- [ ] **Complete native MIDI as a usable feature only after validation.** Confirm note ownership, clip placement and timebase against Cubase 10–15 exports before normalized CPR timing or Standard MIDI export is enabled.
- [ ] **Validate counted tempo-table observations.** The [C-export tempo-table follow-up](docs/research/CUBASE15_C_TEMPO_TABLES.md) traces the shared counted 22-byte prefix in `MTempoTrackEvent` revisions 0–3 and the active-revision suffix. Bounded anonymous fields now cover multi-entry tables and earlier serializer revisions, preserving original bits and absolute offsets without assigning BPM, timebase or effective tempo-map semantics. Per-record and aggregate raw-field budgets disclose omitted fields in archive limits. Unsupported records retain their original inventory instances instead of being copied solely to attach empty field lists. Obtain genuine multi-entry and revision-0/1 fixtures, compare interpreted musical state with Cubase/paired exports, and trace suffix attributes before promoting native tempo to normalized project data.
- [ ] **Validate audio routing against controlled sessions.** The [C-export routing follow-up](docs/research/CUBASE15_C_AUDIO_ROUTING.md) traces nested `InputBusValue/Value` and `OutputBusValue/Value` selections and Cubase's exclusion of zero destinations. The reader retains these selections, raw `outputBusEnable` and `foldbackSendFolder` fields, indexed class-instance slots and corrected scalar-array source offsets. New named-record and mixer observations retain their source ARCH payload offset, so equal bus IDs in repeated stream names do not join across archives; older saved observations without that offset keep their previous stream-scoped behavior until freshly analyzed. Conflicting duplicate property values cannot create edges, and array-shaped child-bus memberships remain distinct. Named-record boundary checks now inspect only the current position instead of searching the remaining archive for every field. Validate destinations, child-bus membership, slot ownership, cue sends, direct-routing enable state and tap semantics with Cubase/paired exports. Routing UI review remains pending under the Uno acceptance checks; model tests and builds do not establish visual approval.
- [ ] **Harden readers for untrusted and unusually large projects.** Differentially test malformed CPR, ZIP/XML limits, duplicate IDs, cancellation, partial files and large inputs. DAWproject `project.xml` and `metadata.xml` now use forward-only `XmlReader` parsing, avoiding a retained `XDocument` plus normalized records; XML remains capped at 16 MiB, depth at 128, archive entries at 5,000, and normalized collections have explicit item limits. A synthetic 100,000-note archive passes. The reader still retains normalized results in memory, and no user archive matching the reported 10–14 GB process growth was available; profile a representative archive and trace the owning allocation before declaring the memory issue fully resolved. Keep exports scoped to declared fields and verify nested clips, automation, device state and MIDI timing with genuine exports.
=======
## P1 — Validation, performance and persistence
>>>>>>> origin/main

- [ ] Finish uncovered page states and interactions after the shared spacing/menu
  consistency pass; per-page results are in [the page/menu review](docs/UI_PAGE_MENU_REVIEW_2026-11-02.md).

- [ ] Finish live Uno acceptance: populated/empty/loading/filtered/partial/error/
  cancelled states; narrow/wide windows; light/dark/system/high contrast;
  100%, 125%, 150%, 200% DPI; keyboard, UIA, dialogs, focus and back/forward.
  Track details now list captured routes, saved input ports, linked audio paths
  and candidate MTrackList/folder-list provenance; live visual and interaction acceptance
  for these details remains pending.
- [ ] Verify project activation on cold startup, slow/missing/corrupt sources,
  repeated selection, busy/cancel/retry and analysis refresh across all views.
- [ ] Validate audio playback/WAV seeking, note/group browsing, graph interactions
  and source-detail windows. Use [UI_REDESIGN.md](docs/UI_REDESIGN.md) and
  [UX_MANUAL_TEST.md](docs/UX_MANUAL_TEST.md); builds alone are not visual approval.
- [ ] Benchmark large-library scan/startup/open/filter/scroll latency, peak memory,
  multi-root reconciliation, duplicates, stale data, cancellation and Unicode paths.
- [ ] Add cache reader/schema versions and source-identity invalidation; reduce
  duplicated snapshot/index arrays through selective project/part loading and
  measured SQL projections. See [STORAGE_FORMAT.md](docs/STORAGE_FORMAT.md).
- [ ] Validate legacy/v2 DB merge/transfer/backup/rollback, corruption, locked or
  unwritable storage, typed editor paging, cleanup and compaction.

## P2 — Producer workflows

- [ ] Validate declared media audit/copy/collect/duplicate workflows and redacted
  manifests; verify native ownership before enabling native media export.
- [ ] Add a missing-media locator over user-selected folders with hash-first
  matching, explicit ambiguity and mapping export without project modification.
- [ ] Verify DAWproject MIDI export, nested clips/timebases and comparison cases
  involving duplicate/moved/missing IDs, sends and enabled states.
- [ ] Improve local VST2/VST3 metadata/CID/artwork validation and catalog aliases,
  attribution, offline imports and conflicts without executing plug-ins.
- [ ] Validate studio-map endpoints, sidechains/taps and physical I/O from explicit
  evidence; align report counts, provenance, dates, privacy and bundle accounting.
- [ ] Review Calendar unknown/stale dates, Search navigation and all report pages.
  PDF reporting remains a later feature.

## P3 — Release and scope

- [ ] Complete untrusted-input, network/cache/log/export privacy and accessibility review.
- [ ] Prepare signed Windows packaging, prerequisites, clean-machine smoke tests,
  upgrades/uninstall, checksums and supported distribution guidance.
- [ ] Treat pre-CPR `.arr`/`.all` as separate future codecs with their own fixtures.

## Required for each implementation batch

Run regression tests and Release/production builds; record failures and unresolved
validation explicitly. Hash genuine fixtures before/after read-only analysis.
Keep binaries, private projects, media and disassembly out of commits.
Research and verification results belong in `docs/research/`, not this task list.

## Native writing gate

No native CPR writer, plug-in disabling, repair or state reconstruction is approved.
Require separate independent Cubase round-trip validation for every intended edit
and supported version before opening that gate.
