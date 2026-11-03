# Automation ownership and saved I/O mapping

Date: 2026-11-02. Read-only analysis of `E:\Cubase15.exe.asm` and the genuine
`E:\Demos` corpus. No project or plug-in binary was modified or executed.

## Automation model and reader behavior

`MAutomationTrackEvent` records now normalize into `AutomationLaneInfo` entries,
separate from `ProjectSnapshot.Tracks`. A lane retains its ID/name, candidate or
declared evidence level, owner ID when available, exact source bounds, serialized
class and relationship provenance. The raw structural inventory still retains
the original lane records and class revisions.

For native CPR, the candidate owner is the unique smallest enclosing non-lane
track byte extent. Equal extents, duplicate owner IDs, invalid bounds, missing
owners and exceeded ownership budgets stay unresolved. No names or record order
establish a relationship. Structured exports can use an explicit declared parent
ID. This normalization is idempotent and legacy snapshots are normalized before
track/graph presentation. A lane is never promoted to a standalone display track.

Track grids show lane counts on their owner; selecting a track shows its lanes
inside the detail pane. Unresolved lanes remain in the separate collection and
reader evidence. Parameter targets and actual automation values remain unknown
until their serializer/reference grammar is decoded and independently compared
with Cubase. Structural containment is not proof of a parameter target.

Full database snapshots preserve the new lane collection and source bounds;
compact scan/browser snapshots omit it like other large detail arrays. Opening
the project re-reads full details. Original project files remain untouched.

## ASM findings

The export is 1,079,140,046 bytes and identifies executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
Twelve complete procedure excerpts are recorded locally in
`artifacts/studio-redesign/asm-complete.json` and `asm/`.

| Evidence | Address / finding |
| --- | --- |
| MAutomationTrack registration | `0x141623B20`, class revision 2, factory `0x14225E800` |
| MAutomationTrackEvent registration | `0x141623B70`, class revision 1, factory `0x14225E920` |
| Lane constructor | `0x14224F630`, installs primary vtable `0x14600B718` |
| Track factory | Installs primary vtable `0x14600BE58` |
| Lane vtable callbacks | Reader `0x14224EEE0`, writer `0x14224F030` |
| Track vtable callbacks | Reader `0x142251240`, writer `0x1422514D0` |
| Lane reader gate | Calls base `0x1431F1380`; positive active revision consumes u32, f32 and u16 flags before additional automation-mode handling |
| Track reader gate | Distinct legacy conversion path for active revisions below 2; not interchangeable with the event reader |
| Track input properties writer | `0x141BDDD80` writes Input Type, Input Device Name, Input Channel ID and Input Port Name |
| Track input properties reader | `0x141BDE190` reads those corresponding named fields; type/channel IDs and device/port strings use distinct helpers |
| I/O reference collector | `0x143513190` reads flat InputBusValue/OutputBusValue; each branch explicitly skips zero before appending a reference |

The event's u32 scalar is **not** treated as a parent track ID. Its meaning and
the base callback's extent remain unverified. No owner IDs are reconstructed
from speculative scalar offsets.

The two vtable prefixes were independently read from the hashed executable while
it was available. Their first 0xA0-byte hashes are
`aef18523540dec6650e1f581d3e815865ef0377195eeac939f82567efccbf071`
(lane) and `692f0d0a1d9ebd843ae69740d4600a6dc1e46906c36e129125db0e0d28da5d97`
(track). Pointer results are in `automation-vtables.txt`.

**Pending binary recheck:** the subsequent disassembly of newly selected callback
ranges could not run because `E:\Cubase15.exe` was no longer present. The ASM
export remained available and all twelve excerpt extractions succeeded. New
binary-range targets have been added for reproduction, but their machine-code
verification is pending; no callback-byte comparison is claimed for that failed run.

## I/O behavior and view

`ProjectIoAssignments` distinguishes named/resolved endpoints from unresolved
references while preserving source/evidence/offsets. The I/O page leads with
saved input/output assignments. Unknown references are available through an
explicit include option instead of inflating the initial mapping list. Saved
device/port names do not prove current hardware availability.

Conflicting Input Device Name values no longer choose the first value; unique
port/device fields can still be displayed and all raw alternatives remain in
source details. If neither device nor port has a unique value, the assignment
stays unresolved; a fallback display label cannot establish an endpoint.
The two flat zero-valued bus properties follow the observed
reference-collector gate. Other UID shapes retain their existing unresolved
behavior: this is not a universal rule that UID zero is invalid.

Routing keeps unresolved references available without drawing them by default.
Graph node buttons use theme-aware foregrounds, and raw record identifiers remain
in source details. Signal-flow construction on the I/O tab is deferred until the
user opens it.

## Genuine corpus validation

All thirteen genuine project/backup files and eight AppleDouble files were
re-inspected. Thirteen remain readable; the resource forks remain rejected.
All 21 source hashes matched before/after. Native note candidates remain 28,564.

| Saved-by sample | Non-lane track records | Automation lanes | Candidate owners found |
| --- | ---: | ---: | ---: |
| Cubase 15 project | 100 | 99 | 99 |
| Cubase 15 backup 01 | 100 | 100 | 100 |
| Cubase 15 backup 02 | 100 | 100 | 100 |
| Cubase 12 mixed | 95 | 85 | 85 |
| Cubase 12 unmixed | 92 | 78 | 78 |
| Cubase 13 | 109 | 103 | 103 |
| Cubase 14 | 86 | 102 | 102 |
| Cubase 9.5 | 55 | 50 | 50 |
| Cubase 8 Jazz Practice | 9 | 5 | 5 |
| Cubase 5.1 Lafzo | 19 | 5 | 5 |
| Cubase 10.5 Sunrise | 43 | 25 | 25 |
| Cubase 5.1 Project Files | 32 | 274 | 274 |
| Cubase 6.5 VCA | 25 | 20 | 20 |
| **Total** | **865** | **1,046** | **1,046** |

Every candidate parent ID was checked against the normalized track collection
and its exact source bounds. Counts are serialized record observations, not
verified visible Cubase track totals. Backups remain correlated fixtures.

## Verification and remaining work

- 631 Release tests pass, with five existing xUnit2031 warnings on recompilation.
  Tests cover ambiguous/missing/invalid owners, declared export parents,
  idempotence, graph exclusion, database round trips, conflicting device names,
  unresolved bus labels and the scoped zero-reference rule.
- CLI and isolated Desktop Release builds pass; self-contained Windows publish
  artifacts are local/ignored under `artifacts/studio-redesign/release/`.
- UI review and interaction scope are in
  [the producer workspace review](../PRODUCER_WORKSPACE_REVIEW.md).
- Remaining reader work includes true automation targets/curves/modes,
  cross-archive bus identity, unclassified port formats, hardware validation,
  missing-version fixtures and independent Cubase comparisons.

The source executable and private fixtures must not be committed. Use the
repository's extraction scripts and local manifests to reproduce available
evidence; retain pending status when an input is absent.
