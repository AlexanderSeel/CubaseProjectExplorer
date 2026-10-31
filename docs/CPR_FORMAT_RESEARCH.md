# CPR structural format research — independent implementation

Updated 2026-10-31

## Source and licensing

- [MyDAW CPR importer](https://github.com/omeriko9/MyDAW/blob/main/engine/src/import/CprImportProvider.cpp)
- [MyDAW mixer format notes](https://github.com/omeriko9/MyDAW/blob/main/docs/CPR_MIXER_FORMAT.md)
- [MyDAW CPR container notes](https://github.com/omeriko9/MyDAW/blob/main/scripts/cpr-container.mjs)

MyDAW uses **GPL-3.0**. We have **not copied or translated its C++ implementation**.
The CPR reader here is original C# written from documented on-disk layout observations.
Those authors' claims of Cubase SX through Cubase 13 compatibility are external and
**not** validation of our implementation. Do not vendor its code or private corpus
without a deliberate licensing decision.

## Implemented — read-only / bounded

1. RIFF or RIF2 signature and NUND form must match. The candidate big-endian
   container length defines the chunk boundary. The two controlled Cubase 15
   fixtures and static chunk-reader path use no standard RIFF even-byte padding;
   this is not a universal per-version format guarantee.
2. Chunk names use 4-byte IDs; lengths use big-endian u32 (RIFF) or u64 (RIF2).
3. ROOT chunks encode two length-prefixed strings identifying the following ARCH.
4. Selected PArrangement ARCH: bounded scan of class headers, interned class IDs,
   nested payload boundaries and counts. Candidate track classes may have nested
   MListNode name payloads; these remain unverified hints.
5. Version/PAppVersion: printable version *candidate* in designated stream only.
6. Safety budgets: max 4,096 chunks; max 4 KiB ROOT; max 128 MiB arrangement scan;
   max 250k record headers; max nesting depth 48; max 4,096 track hints;
   no plugin execution, source mutation, or decompression.
7. Detailed class-header evidence for up to 128 ARCH payloads, including streams
   outside PArrangement: unknown class names, class serialization version, exact
   absolute header/body offsets, declared sizes, prefix classes, class symbol
   source, candidate enclosing record, uninterpreted ranges and explicit limits.
   Aggregate budgets: 128 MiB scanned, 250k record candidates, 250k class symbols,
   10k retained record details and 4,096 retained uninterpreted ranges. References
   resolve previously recognized declarations only. Opaque bytes can resemble
   headers; byte containment does not prove musical ownership. Partial means a
   safety limit was encountered, not a measured percentage of musical coverage.
8. MTempoEvent serialization version 0 / exact 24-byte body exposes anonymous
   f32/f64/f64/u32 big-endian candidates at offsets 0/4/12/20, with original hex
   bits and binary-reader provenance. The layout comes from static reader/writer
   analysis, without a matching controlled native fixture. No BPM, timing units,
   tempo-map entry or normalized project field is inferred.
9. The two controlled fixtures also expose MTempoTrackEvent version 3 / exact
   54-byte body with one raw array item. Seven anonymous fixed-prefix values
   (count, f32/f64/f64/u16 item, f32/byte suffix) retain offsets and raw bits;
   trailing attributes remain opaque. This deliberately narrow layout guard
   does not decode multi-item tempo maps or historical serializer revisions.

CLI: cpr-structure <file.cpr|file.bak> [--json|--markdown]. See the
[executable serializer analysis](research/CUBASE15_SERIALIZER_ANALYSIS.md) for
exact build hashes, routine addresses, reproduction commands and limitations.

## Public donor file: preliminary independent byte sanity check

A *separate* JavaScript byte inspection (not a run of the compiled C# reader)
was performed on the public `engine/assets/cpr-donor-c5.cpr` in MyDAW's repository.
Observed:

- 133,463 total bytes; `RIFF` / `NUND`; declared payload 133,451 bytes.
- 12 chunks, in `ROOT` / `ARCH` pairs: `Version|PAppVersion`,
  `Arrangement1|PArrangement`, `Devices|FAttributes`,
  `WindowLayouts|UWindowLayout`, `ProjectLayouts|FAttributes`,
  `Metadata|StMedia::PAttributes`.
- The `PArrangement` ARCH is 115,772 bytes. A separate scanner following the
  documented header grammar counted 267 candidate class records, including
  seven `MAudioTrackEvent` records with nested names `Track1` ... `Track7`.
- The version archive contains separate text `Cubase` and `Version 5.1.1`;
  this prompted a version-reader regression test.

The donor is not committed, bundled or redistributed. The results support
the format research, **not** proof that our compiled C# reader handles real
Cubase files; version-specific verification is still pending.

Windows CI [40840170071](https://github.com/AlexanderSeel/CubaseProjectExplorer/actions/runs/40840170071)
passed 37 synthetic unit tests, CLI Release and WinUI x64 Release builds.

## Legacy Cubase SX/SL and Cubase 4–10.x inspection

The same read-only RIFF/NUND container reader applies to CPR files from Cubase SX/SL
and later Cubase generations **when the file uses the recognizable on-disk grammar**.
The PAppVersion candidate is classified into SX/SL, mainline Cubase 4–10.x,
11+, Nuendo pre-11, and ambiguous/unknown groups. This helps locate legacy
sessions and inspect archive chunk structure or candidate track/property markers
without claiming complete decoding.

- Project Library shows a **saved-by version hint**, descriptive generation and
  original-creation-version caveat.
- The existing 5.1.1 public donor has a compiled Windows CLI smoke check
  (CI 40841213626); version parsing for SX 1–3, SL 2 and Cubase 4–10.5
  has synthetic regression tests only.
- Do not interpret an unlabeled "Cubase 3.x" string as *Cubase SX 3*;
  the old Cubase VST line also used low release numbers.
- Old Cubase VST song/arrangement formats `.all`/`.arr` predate the
  CPR container and are **not** directly decoded. Steinberg documents
  conversion via compatible historical Cubase software.

This is **candidate-level legacy reading**, not verified track/plugin/routing
reconstruction for Cubase SX. Legal non-sensitive real SX1–3, Cubase 4–10 and
Nuendo samples are required to validate archived class layouts before
promoting any fields to Confirmed. The original files are never changed.

## Explicitly NOT implemented

- Confirmed CPR track hierarchy, volume/pan, MIDI note/CC data, tempo map.
- Per-channel plugin UID/class, insert order, installed compatibility status.
- Physical audio/MIDI ports, sends, FX/groups, VST state.
- CPR writer or Safe Copy that disables plugins.
- Complete per-version semantic validation / differential testing. The controlled
  Cubase 15 fixtures validate bounded framing and candidate byte observations only.

## Validation process to reach confirmed data

1. Obtain legal non-sensitive CPR fixtures for the historical and current Cubase
   versions being targeted, with independent MIDI, XML or DAWproject exports
   wherever the corresponding Cubase version supports them.
2. For each fixture compare chunk/root/ARCH index and candidate track names
   against Cubase UI, record discrepancies and ambiguous encodings.
3. Add version-specific adapters for track identity, metadata, and MIDI/tempo,
   with exact offsets and matching regression fixtures.
4. Only move fields into ProjectSnapshot.Tracks or Plugins when verified.
5. Never write CPR bytes until extensive before/after Cubase round-trip tests.

All synthetic test fixtures are code-generated; no third-party binary projects
or copyrighted corpora are committed.

## Verified real-file CLI integration check

The compiled standalone C# CLI was exercised in Windows GitHub CI run [40841213626](https://github.com/AlexanderSeel/CubaseProjectExplorer/actions/runs/40841213626) against the public Cubase 5 donor downloaded at runtime: 12 chunks, 7 `MAudioTrackEvent` candidate names `Track1` … `Track7`, and version 5.1.1 were observed. This supersedes the earlier note that only a separate JavaScript byte sanity check had been conducted; it does not establish parity for unsupported fields or newer Cubase files. The external donor is not stored in our repository or binaries.

## Cubase 15 native executable: 64-bit envelope evidence (2026-10-31)

The user-owned executable was inspected statically without executing it.
It provides independently observed code paths for `RIFF/NUND` (32-bit
big-endian size-like field), `RIF2/NUND` (64-bit big-endian size-like field),
and the `ROOT`/`ARCH` serialization identifiers. This is **not** a complete
project specification or verified full CPR decoder.

- [Comprehensive project-format research, revision 3](research/CUBASE_PROJECT_FORMATS_DEEP_DIVE_V3.md)
- [Executable disassembly evidence and native function addresses](research/CUBASE15_EXECUTABLE_FINDINGS.md)
- [Candidate project type-name catalog](research/CUBASE15_PROJECT_TYPE_CATALOG.tsv)
- [Static evidence verification script](../scripts/verify_cubase15_binary_evidence.py)

The infrastructure probe recognizes both header variants, including RIF2 backups.
Two genuine Cubase 15.0.40 user-owned examples now validate the 64-bit
RIF2 envelope and ROOT/ARCH chunk boundaries. `CprStructureInspector`
traverses the 64-bit class-record grammar and extracts a version-scoped,
bounded set of `MMidiNote` candidate values (note, onset, duration,
velocity) from the observed 112-byte event bodies. Track ownership and
other Cubase releases remain unverified; no values are promoted to confirmed
project MIDI. See [controlled MIDI fixtures](CPR_RIF2_TEST_FIXTURES.md).
The original projects remain read-only and no proprietary binaries are committed.

## User-supplied D:\Cubase corpus observations (2026-10-31)

The directory inventory contains 83,123 files: 1,159 `.cpr`, 2,368 `.bak`,
65,492 `.wav`, and 83.85 GiB overall. A bounded read-only structural pass of
3,527 CPR/BAK files parsed 3,515, returned 10 `NOT_RECOGNIZED`, and 2
`UNSUPPORTED`. Across those files it observed 19,193 `MAudioTrackEvent`
records, 23,964 `MMidiTrackEvent` records, and 59,705 native mixer
observations, 59,605 of which were volume anchors. These are parser candidates,
not unique track counts or verified Cubase fields.

A subsequent focused review of three CPR projects found common audio path
tokens inside `MAudioEvent` bodies structurally nested under candidate
`MAudioTrackEvent` records. The reader now surfaces these values as explicitly
unverified sample-path/track-name candidates. It does not add them to confirmed
media-use records: path resolution, clip ownership, offline state and actual
playback use are not established by this containment alone. This focused
reader validation does not yet constitute a complete rerun of the large corpus
with the newer candidate extraction enabled.

The existing local library database contains 3,436 project records and 19,068
candidate Audio-track records, plus 59,705 mixer observations. Its prior CPR
asset indexes contain no normalized native CPR media references or MIDI notes;
these values are not retroactively synthesized by the read-only audit. The
user's corpus remains local and is not included in the repository.
