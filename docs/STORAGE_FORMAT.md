# Library storage and compact asset-index JSON

Implemented: 2026-11-02. The database remains SQLite with typed Shiny DocumentDb
documents. A dedicated JSON codec optimizes repeated native MIDI evidence inside
`LibraryProjectAssetIndexDocument.Index`. It is an internal cache representation,
not a Cubase replacement format or a verified musical project interchange format.

## Version 2

The document envelope retains its existing `Id`, `ProjectPath` and `IndexedUtc`.
The `Index` object uses these fields:

| Field | Meaning |
| --- | --- |
| `v` | Integer storage version, currently 2 |
| `projectPath`, `projectName`, `evidence` | Index identity and original evidence enum value |
| `mediaUses`, `midiTracks`, `nativeMedia` | Existing typed reference/track arrays, unchanged in meaning |
| `contexts` | Shared nine-value note context rows |
| `notes` | Nine-value note rows, empty or null as originally represented |

Context row, in order:

```text
[projectPath, projectName, evidence, warning, sourceLayout,
 candidateTrackName, candidatePartName, candidateTrackOffset, candidatePartOffset]
```

Note row, in order:

```text
[contextIndex, observation, fileOffset, positionTicks, durationTicks,
 pitch, velocity, channel, rawFlags]
```

Context deduplication compares every value, including nullable fields, paths and
the complete evidence/warning string. A note whose source path differs from the
index retains that source path. Rows retain original ordering, 64-bit offsets,
double round-trip precision (including tested negative zero), nullable channel
and flags. No value is rounded, quantized, normalized or inferred. Historical
`Ticks` property names do not prove musical units for CPR observations.

The property-level converter is used both by DocumentDb's typed serializer and
the explicit System.Text.Json streaming reader. Unknown storage versions,
incorrect row shapes, invalid context references and malformed values fail as
JSON errors. Context and note arrays are capped at 250,000 rows. Null properties
may be omitted by DocumentDb; missing/null notes and native media remain readable.

Legacy `Index` JSON without `v` continues through the original typed model. New
writes use v2; there is no automatic rewrite of the user's existing database.
Re-inspection/rebuild naturally saves v2 indexes. Older app binaries should not
be expected to understand v2 arrays; retain a backup before downgrading.

## Measurement on genuine demos

The same 13 read-only demo inspection snapshots were converted to asset indexes.
Legacy and v2 representations were serialized with equivalent options, round
tripped, and compared after conversion back to the original typed representation.
All comparisons matched. Each deserializer was warmed and measured five times
in the same Release harness. Allocation figures sum per-project mean allocated
bytes; they are not process working set.

| Measurement | Legacy object rows | Compact v2 | Change |
| --- | ---: | ---: | ---: |
| UTF-8 asset-index JSON bytes | 30,587,909 | 2,106,901 | 93.1% fewer |
| Managed bytes allocated while reading | 60,601,230 | 6,126,296 | 89.9% fewer |

These results concern the asset-index documents for this corpus. They do not
measure total SQLite file size, project snapshots, images, page fragmentation,
end-to-end startup or a general speedup. Local timing/allocation details and the
scratch harness remain under ignored `artifacts/demos/storage-benchmark.json`
and `artifacts/demos/Benchmark.csproj`; private snapshots are not committed.
Both measurements include the same document envelope; the legacy DTO has no
compact converter. Regression tests additionally exercise actual SQLite query and streaming paths,
legacy JSON, numeric precision, nullable fields and corruption/version rejection.

## Recommended next storage stages

1. Add explicit cache-schema and reader-build versions alongside source SHA-256,
   size and write time. Invalidate stale parsed results; a timestamp alone is
   not proof of unchanged source bytes. Preserve historical evidence until a
   successful replacement inspection is committed.
2. Keep three concepts distinct: source identity, observed format evidence, and
   verified normalized musical entities. Relationships retain their provenance;
   unsupported classes/fields remain represented in coverage, not guessed.
3. Avoid duplicate large event arrays in full snapshots and search indexes.
   Store a compact session summary separately from per-project/part evidence
   blocks. Load only the selected part's rows. Preserve consistent backup/export
   and legacy document reads while migrating transactionally.
4. Add indexed SQL projections for stable search fields (source hash, version,
   names, evidence, note/track/media counts). Query projections for browsing,
   then decode JSON for details. Do not index every JSON note or image eagerly.
5. Measure cold/warm startup, selected-project open, filtered query latency,
   allocation/working set and database/WAL bytes on small and large libraries.
   Introduce binary blocks/compression only when these measurements justify
   their complexity; JSON v2 already removes most repeated note text here.
6. Validate mixed legacy/v2 databases, transfer/merge, downgrade backups,
   interruption, locked files, corruption and very large blocks before a bulk
   migration. No migration may alter original CPR, BAK or media files.

The normalized models remain the application contract. Persistence compaction
does not change evidence levels or make native CPR state complete.
