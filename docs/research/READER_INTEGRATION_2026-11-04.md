# Reader integration checkpoint

Date: 2026-11-04. Reconciles the committed merge of the C-export reader work and
the typed/count-stream attribute work on `main`. No new Cubase serializer grammar
was inferred in this batch. Earlier reports describe their dated checkpoints;
this note describes the combined implementation.

## Repair scope

Conflict markers existed in `PLAN.md`, `README.md`, five production C# files
and the counted-stream tests. The affected modules could not compile. Resolution
preserves the revision-gated mixed-event, score, opaque object/string, tempo-table,
source-archive routing and bounded-field capabilities from both branches.

- Counted MIDI now has one revision table and one SysEx payload traversal.
  The first SysEx word is a nonnegative signed byte count; the second word stays
  opaque, including unequal values, following reader `0x141CF3610` documented in
  [the C-export follow-up](CUBASE15_C_MIXED_MIDI.md). Earlier matching-word-only
  restrictions in [the packed-event report](CUBASE15_PACKED_EVENT_CALLBACKS.md)
  describe its older implementation. Payload bytes are skipped without allocation.
- Score events retain their distinct base fields and named-revision gates, then
  use the shared supported scalar/object/string attribute traversal. Framed
  contents stay opaque. Chord and note-change bodies keep their separate suffixes.
- Another note triggers the note limit. Supported trailing non-note events can
  finish a list at exactly the limit. Unsupported packets and SysEx length failures
  preserve only preceding complete candidates as partial evidence. Invalid list
  counts/trailers still reject the prefix; no opaque-byte resynchronization occurs.
- Typed-note raw-field decoding receives the archive's object-framing context
  through the bounded decoder. Per-record and aggregate raw-field limits,
  cancellation checks and original unsupported-record instances remain intact.
- Bus resolution keeps archive offset, stream and UID scope, rejecting ambiguous
  IDs and zero selections. Zero fields remain available as raw observations;
  no fabricated bus-zero edges are produced. A stale test that expected such an
  edge was aligned with the later routing policy.
- Explicit send/direct/foldback destinations retain priority over bulky panner
  fields. Foldback fields remain raw evidence and do not create cue-send edges.

The score and string subsets were already present in the merged source; this
repair makes them compile with the newer bounded-field and mixed-MIDI work.
No new normalized musical entities, native writer or plug-in execution is enabled.

## Validation

- Full Release regression suite: **745 passed, zero failed or skipped**, with
  `CPE_CPR_FIXTURE_DIR` set to the local directory holding the two controlled,
  hash-pinned Cubase 15 projects. These fixture tests compare source hashes/bytes
  before and after inspection. Neither original was changed or added to Git.
- Final regression run: **763 passed, zero failed or skipped**, including
  [time-signature revision coverage](CUBASE15_C_SIGNATURE_REVISIONS.md).
  Its additional fixed-prefix support is documented separately from the
  integration repair described above.
- Focused counted-stream suite: **138 passed**. Coverage includes exact following
  offsets, opaque note-like payloads, unequal SysEx words, bounded/truncated
  lengths, score attributes, unsupported flags, revision gates and note budgets.
  Truncated length words are tested at the containing part boundary; otherwise
  following bytes cannot independently identify a missing word.
- CLI Release build passed with zero warnings/errors.
- Windows x64 self-contained CLI and Uno Desktop Release production publishes
  passed. The app publish was repeated after the signature changes.
- The published CLI read both controlled projects: 14 chunks each, zero notes in
  the empty project and 16 candidates in the MIDI project. Source SHA-256 hashes
  were identical before and after these reads.

The initial combined regression run exposed conflicting older test expectations
and a foldback-priority regression; all were resolved before the passing run.
Local test results and production outputs stay under ignored
`artifacts/reader-integration-2026-11-04/`.

Synthetic wire fixtures verify parser mechanics. The two genuine fixtures do
not validate every mixed-event class or establish cross-release musical meaning.
No new broad corpus comparison was performed. Independent Cubase/export
comparisons, native folder/ownership/timing verification, large-input profiling
and all live Uno visual, keyboard, accessibility, theme and DPI acceptance remain
open in [PLAN.md](../../PLAN.md). Builds do not establish visual approval.
