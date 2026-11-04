# Implementation plan

Updated: 2026-11-04. Work on `main`. This file contains open work only.
Implemented reader capabilities and exact limits live in [READER_FEATURES.md](docs/READER_FEATURES.md).
The wider product inventory is in [FEATURE_MATRIX.md](docs/FEATURE_MATRIX.md).
Latest integration repair and verification: [reader integration](docs/research/READER_INTEGRATION_2026-11-04.md).
Current decompile leads and unresolved reader candidates: [Cubase 15 gap map](docs/research/CUBASE15_DECOMPILE_GAP_MAP_2026-11-03.md).

## P0 — Reader correctness and completeness

- [ ] Decode automation parameter targets/modes and independently validate owner
  relationships beyond the implemented candidate byte-containment association.
  The [automation-prefix follow-up](docs/research/CUBASE15_C_AUTOMATION_PREFIXES.md)
  retains anonymous `MAutomationTrack` revision-0/1 counted prefixes and revision-2
  words with bounded raw-field provenance. Genuine legacy fixtures, target codecs,
  mode meanings and the inherited object grammar remain open; no new owner IDs
  or normalized parameter values are inferred.
- [ ] Resolve remaining cross-archive I/O bus identities and unclassified port
  strings; validate actual hardware mappings without inferring IDs from names.
  Foldback-send writer fields are retained raw, while their reader/slot grammar
  and route semantics remain open. Current serializer findings and validation:
  [automation/I/O research](docs/research/AUTOMATION_IO_2026-11-02.md) and the
  [Cubase 15 gap map](docs/research/CUBASE15_DECOMPILE_GAP_MAP_2026-11-03.md).

- [ ] **Validate counted native MIDI streams across Cubase generations.** The [counted-stream ASM investigation](docs/research/CUBASE15_MIDI_STREAM_COMPATIBILITY.md) traces `MMidiPart` revisions 1/2, its count/declaration preamble, exact event factory discriminators, and the shared base grammar. A bounded reader now accepts complete supported lists, including zero-attribute 43-byte note packets observed in SX1–3, and skips supported scalar controller/aftertouch/pitch-bend packets without assigning them musical values. The [C-export follow-up](docs/research/CUBASE15_C_MIXED_MIDI.md) adds exact-boundary poly-pressure, program-change and length-delimited SysEx traversal with revision checks; SysEx bytes are skipped without payload allocation. Trailing supported non-note events no longer falsely consume the note budget, and archives without MIDI parts avoid MIDI lookup-table allocation. Full-object escapes keep their separate structural evidence. Unsupported grammar cannot create zero-attribute candidates through byte resynchronization; normal note/byte budgets retain partial candidate results explicitly. Inventory and arrangement traversal now share one transactional class-header reader. Independently verify the new candidate values against Cubase/exports, obtain genuine poly-pressure/SysEx mixed-stream fixtures, expand legacy revision-0 coverage, and validate remaining timebase/object/string codecs before normalized timing or native MIDI export is enabled.
- [ ] Cover remaining event-attribute object, string and container branches,
  reference forms and revision gates beyond the shared typed/count-stream subset.
  The [container boundary follow-up](docs/research/CUBASE15_ATTRIBUTE_CONTAINERS.md)
  skips a bounded primitive-property subset for exact flags `0x0100`, without
  instantiating the type or applying values. Object/array/recursive entries,
  other flags and genuine controlled container fixtures remain open. Recognized
  counted parts now retain typed-note observations only at explicit object escapes.
  Current implementation and test cleanup: [typed-note research](docs/research/TYPED_NOTE_ATTRIBUTES_TEST_AUDIT_2026-11-02.md).
- [ ] Extend MScoreEvent beyond its null or exact MDataNode object/reference
  boundary and supported inherited-attribute subset; complete the remaining mixed-event bodies.
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
  The [marker prefix follow-up](docs/research/CUBASE15_C_MARKER_PREFIX.md)
  retains bounded string/scalar observations for `MMarkerEvent` revision 0;
  inherited event data, IDs, positions and ownership remain undecoded. The existing
  33-file sample contained no compact marker records; obtain genuine controlled
  marker changes before promoting any normalized marker state.
  The [signature revision follow-up](docs/research/CUBASE15_C_SIGNATURE_REVISIONS.md)
  retains the shared anonymous 20-byte prefix for explicit class revisions 0–2;
  genuine revision-0/1 fixtures, attribute semantics and musical mapping remain
  unverified. Raw-field formatting now honors the remaining retention budget.
- [ ] Expand versioned SX/Cubase 5–15 project/backup fixtures and independent
  semantic comparisons; fill release/feature gaps without committing source projects.
- [ ] Add malformed/reference/cycle/count/length tests and measure large-file
  memory, time, cancellation and scan coverage for each new codec.

Reader acceptance: every supported field has provenance and independent comparison
where musical meaning is asserted. Unknown data stays unknown. Successful bounded
scans do not establish full loading or universal backward compatibility.

- [ ] Build a versioned read-only Cubase validation corpus spanning SX and Cubase
  5–15 CPR/BAK releases, including genuine mixed-event and legacy-revision bodies.
  Compare interpreted fields with Cubase-visible state and paired exports;
  bounded samples and correlated backups do not establish release-wide schemas.
- [ ] Validate native framing, byte order, chunk bounds, compression, nesting,
  references and crash backups across generations. Select RIFF/RIF2 from the
  signature rather than the saved-by version. Preserve unsupported structural
  evidence and keep malformed, unknown and candidate fields distinct.
- [ ] Validate native track/media/plug-in identities, hierarchy, ownership, slots,
  state boundaries, mixer/sends/EQ, logical I/O and automation relationships
  independently. Complete native MIDI only after note ownership, clip placement,
  timebase and effective values agree with controlled Cubase 10–15 exports.
  Native MIDI export remains gated on that validation.
- [ ] Validate counted tempo-table rows and revision-0/1 fixtures against Cubase
  and exports; trace suffix attributes before deriving BPM or normalized tempo.
  Current raw-field evidence: [tempo-table follow-up](docs/research/CUBASE15_C_TEMPO_TABLES.md).
- [ ] Validate native destinations, child-bus membership, slot ownership, cue sends,
  direct-routing enable state and tap semantics with Cubase/exports. Bus joins
  require source archive/stream scope and a unique explicit ID; saved names do
  not establish hardware availability. Refresh older unscoped observations by
  read-only analysis. Evidence: [audio-routing follow-up](docs/research/CUBASE15_C_AUDIO_ROUTING.md).
- [ ] Harden CPR and DAWproject against malformed/reference/cycle/count/length
  inputs, duplicate IDs, cancellation and partial files. Profile a representative
  large archive to identify the allocation behind reported 10–14 GB process
  growth; bounded forward-only XML and a synthetic 100,000-note case do not
  resolve that report. Verify nested clips, automation, device state and MIDI
  timing against genuine exports; preserve current explicit resource limits.

## P1 — Validation, performance and persistence

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
