# Typed MIDI-note attributes and test relevance audit

Date: 2026-11-02. Read-only continuation using `E:\Cubase15.exe.asm` and
`E:\Demos`. Original projects and disassembly remain unchanged and untracked.

## Reader extension

Separately framed `MMidiNote` records now share the counted-stream attribute
reader. With earlier, unambiguous named `MMidiEvent` v2 / `MMidiNote` v1 evidence,
the reader locates the suffix after supported scalar or opaque object values:

| Attribute | Framing requirement | Treatment |
| --- | --- | --- |
| `ScAr`, flags `0x0022` | `CmAttribCollection` rev0 | Opaque framed bytes, null or supported earlier reference |
| `bOuc`, flags `0x0002` | `MParamEventCurveRationalBezier` rev0 and named `MParamEventCurveBase` rev0 | Opaque framed bytes with the supported base prefix/reference rules |

The context is prepared once per archive/pass, rather than rebuilding object
and declaration dictionaries per note. Span-only public helpers retain their
scalar-only behavior because they lack an archive reference context.

Both the arrangement candidates and anonymous inventory fields use the shared
boundary reader. The suffix must end exactly at the record boundary: three f64
values plus the revision-1 byte. Raw NaN payloads and signed zero remain in the
anonymous fields; finite-value/range checks still gate musical-field candidates.
No attribute is applied to the base fields, and native normalized MIDI/export
remains unavailable. Legacy layouts still yield only the previously supported
anonymous observations.

Unknown classes/flags/revisions, unresolved/forward references, missing curve
base evidence and invalid lengths remain opaque/partial. Diagnostics retain the
attribute ID, flags and offset or explain the suffix mismatch. Zero-body class
declarations are not treated as truncated event instances.

The arrangement pass no longer scans inside a typed note for further events.
An embedded note-shaped record inside a score/curve value cannot become a
standalone arrangement note. The structural inventory still retains nested raw
records; this change affects interpretation, not preservation of evidence.

## ASM inspection

Five complete procedures were re-extracted with the existing hash-gated script:

| Procedure | Address | Export byte offset | Excerpt bytes |
| --- | --- | ---: | ---: |
| MMidiEvent reader | `0x141BB0390` | 173277965 | 3687 |
| MMidiNote reader | `0x141BB4690` | 173395216 | 5154 |
| MMidiNote writer | `0x141BB49A0` | 173400408 | 3366 |
| Attribute value reader | `0x1445749D0` | 497438624 | 3372 |
| Archive object reader | `0x1445E9990` | 500973950 | 8580 |

The base callback queries named `MMidiEvent` revision, reads the flags for
positive revisions and calls the attribute-list reader above revision 1. The
note reader calls that base callback before its three f64 fields and queries
named `MMidiNote` revision before its extra byte. Subsequent attribute lookups
and conversions show why locating raw fields does not establish final Cubase
state. The writer corroborates base-before-tail ordering. Object-valued
attributes use the archive object path, whose framed extent is shared with
counted events; there is no new fixed-stride assumption for nested values.

Manifest and excerpt hashes are local in
`artifacts/reader-test-audit/asm-manifest.json`. The ASM header identifies build
SHA-256 `b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`;
this does not authenticate the export itself. `E:\Cubase15.exe` remains absent,
so no new machine-code disassembly/recheck is claimed. Previously established
binary framing evidence is in [the earlier report](CUBASE15_NESTED_ATTRIBUTES.md).

## Test relevance audit

Inventoried all 95 original `*Tests.cs` files, checked project references and
searched production consumers of the old presentation APIs. This is a relevance
audit and targeted source review, not a claim of exhaustive assertion or UI
coverage. The test project references Infrastructure/Core; it does not execute
Uno pages or replace live visual/keyboard/accessibility acceptance.

| Decision | Tests / production evidence |
| --- | --- |
| Remove 4 retired track-row tests | `TrackListPresentationTests` exercised `TrackListPresentation`, which had no runtime callers; current Uno rows use `TrackDisplayRow` |
| Remove 2 retired project-chart tests | `ProjectChartTests` exercised unused `ProjectChartService.Build`; current pages use different view-model data |
| Remove corresponding dead helpers | Deleted the unused row helper and chart builder/summary; retained `ProjectChartBar`, which library analytics still uses |
| Keep CLI I/O preflight coverage | `StudioIoPreflightService` remains the implementation of CLI `io`, even though the Uno I/O page now uses `StudioIoDiagramBuilder` / `ProjectIoAssignments` |
| Keep legacy reader/storage tests | Historical serializers, scalar-only public helpers, legacy indexes and persisted settings are compatibility contracts, not obsolete screens |
| Keep bounds/evidence/value tests | Unknown versus zero, candidate versus confirmed, byte/count/revision/reference limits and source preservation remain required behavior |
| Correct private-fixture reporting | Two opt-in facts now report skipped at discovery when `CPE_CPR_FIXTURE_DIR` is absent, instead of returning as apparently passed |
| Clean analyzer warnings | Replaced five `Assert.Single(items.Where(...))` calls with the predicate overload; assertions retain their behavior |

Twelve new theory cases cover both archive widths, supported inline/null/earlier
score references, Bézier base evidence, exact suffix bits, malformed/unsupported
values and prevention of phantom notes inside typed attributes. A further
candidate-budget test verifies that 10,000 retained notes do not prevent later
track discovery. Once that budget is full, the arrangement pass skips subsequent
typed-note decoding instead of growing its attribute-object context. Existing
counted-stream tests caught an initial zero-body-declaration diagnostic regression;
the reader was corrected and those tests were retained.

Other backend helpers can have independent persistence/reporting or staged-feature
contracts; lack of a current menu entry alone is not a reason to delete their
tests. Dormant APIs need a deliberate feature-retirement decision. The remaining
Uno interaction coverage is still open in [PLAN.md](../../PLAN.md).

## Validation

- Release test run: **636 passed, 2 explicitly skipped, 638 total**, with no
  warnings on recompilation. The two controlled private fixture files were not
  configured for this run; their expected-note checks were not executed.
- Self-contained Windows CLI and isolated Uno Desktop Release publishes pass.
  Outputs/logs are ignored under `artifacts/reader-test-audit/`.
- Re-ran both `cpr-structure` and `inspect` over all 21 demo entries. Thirteen
  genuine CPR/BAK files remain readable and eight AppleDouble files remain
  rejected. All source hashes match before/after and the retained baseline.
- All **28,564 note candidates** retain their earlier values, channel/flags and
  track/part context. No candidates were added or removed in this corpus; all
  thirteen arrangement reports remain non-partial within their supported scan.
  The new object-bearing typed forms are covered by synthetic wire fixtures,
  not by a newly discovered genuine sample or Cubase round trip.
- Automation remains **1,046 candidate-associated lanes** across these samples.
  This work does not verify targets, parameter values or physical I/O availability.

Next reader work remains the unsupported string/container/object cases,
`MScoreEvent`, independent note timing/value/ownership comparisons, automation
targets and complete channel/bus identity. Universal backward compatibility and
whole-project completeness remain unverified.
