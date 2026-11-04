# Implemented features and open work

Status: 2026-11-02. This inventory describes the current `main` implementation.
**Implemented** means code exists; its validation limits are listed alongside it.
**Partial** means only a bounded subset is supported. **Open** means the product
outcome is unfinished. A native candidate is never a verified Cubase object.

The detailed reader-only inventory is [READER_FEATURES.md](READER_FEATURES.md).

## Reader and project data

| Feature | Status | Implemented behavior | Remaining work |
| --- | --- | --- | --- |
| CPR/BAK file recognition | Partial | RIFF/32-bit and RIF2/64-bit probes; saved-by text; bounded fallback evidence | All historical envelopes, nesting, compression and malformed variants; choose framing from bytes, never version labels |
| Archive inventory | Partial | ROOT/ARCH streams, bounded class headers, revisions, earlier declarations, symbol references, offsets, raw fields and explicit limits | Complete object/reference traversal and field coverage; ambiguous headers remain candidates |
| Inherited class revisions | Partial | Independent named base/note revisions, active-vs-named ASM research; no implicit missing=v0 rule | Verify legacy bodies and all inherited track/audio/automation serializers against genuine projects |
| Native track/channel records | Partial | Recognized serialized class names, stored-name candidates, selected typed fields and serializer provenance | Verified track IDs, hierarchy, order, colors, folder ownership and all track types |
| Native MIDI notes | Partial | Counted MMidiPart rev1/2 with MMidiEvent v2/MMidiNote v1; zero/scalar attributes and opaque framed ScAr/bOuc values; shared counted/typed attribute framing plus a separate bounded pattern fallback; raw precision, channel/flags and possible part/track context | Other object/string/container cases, legacy note/base bodies, full ownership, clip placement, timebase, attribute overrides, controller and expression semantics |
| Mixed packed MIDI lists | Partial | Exact boundaries for supported controller, pressure, program, pitchbend, chord, note-change, empty score and duplicated-length SysEx bodies with supported revisions; non-note payloads stay opaque | Non-null/nonempty MScoreEvent grammar, other revisions/payloads, musical decoding of skipped events and Cubase comparison |
| Partial-read diagnostics | Implemented | Bounds/count/revision failures stay explicit; unsupported attributes include ID, flags and exact offset; no scanning through unknown packed payloads | Consistent per-field coverage and unsupported-object reports across all readers |
| Native tempo/time signature/markers | Partial | Selected metadata candidates and raw callback-backed observations | Full tempo/signature maps, ramps, marker types and verified absolute musical positions |
| Native audio/media | Partial | Structurally nested path candidates and possible track context | Clip/part/media ownership, pool IDs, offsets, fades, stretching/warp, lanes, takes, comping and missing-media identity |
| Native plug-ins | Partial | Stored UID/name/property/slot observations and advisory metadata correlation | Verified product/format/instance identity, ownership, slot order, bypass/enabled state, presets, state references and sidechains; opaque state stays opaque |
| Native mixer/routing | Partial | Typed saved bus/port/send/EQ observations and explicit field-reference links with source offsets | Complete channel identity, gain/pan/mute/solo, sends/taps/levels, groups/FX, sidechains and physical I/O validation |
| Native automation/expression/score | Partial | Separate automation lanes with candidate owner/source bounds; raw serializer and curve evidence retained | Target references, curves/interpolation, event types, note expression, articulations, notation and their version gates |
| Universal backward compatibility | Open | Selected SX1–3 and Cubase 5–15 files reach bounded paths; per-class revision gates documented | Versioned fixture matrix and independent Cubase/export comparisons; directory names and parse success cannot establish full support |
| DAWproject reader | Implemented subset | Forward-only bounded ZIP/XML parsing of declared tracks, plug-ins, media, routing, metadata, timeline, flat MIDI notes, EQ/warp fields | Genuine-export comparisons for unsupported nesting, mixed timebases, automation and ambiguous references; confirmation applies to exported fields only |
| Native CPR editing | Gated | Byte-identical safe-copy utility only | No native writer or plug-in disabling until separately approved Cubase round-trip validation exists |
| Pre-CPR ARR/ALL | Open | Separate scope; not passed through CPR reader | Document separate grammars and obtain appropriate fixtures |

## Producer workflows and desktop application

| Feature | Status | Implemented behavior | Remaining work |
| --- | --- | --- | --- |
| Open/select a project | Implemented | Re-read source on activation, await startup hydration, show failures, navigate only after successful loading | Live mouse/keyboard regression with slow, missing, corrupt and repeatedly selected sources |
| Project library | Implemented | Multiple scan roots; staged progress/cancel; compact summaries; checksums for bounded files; exact-copy reconciliation; search/filter/sort/group; paged cards/details; CSV/Markdown/JSON | Large-library measurement, inaccessible roots, cancellation, stale snapshots and selection acceptance |
| Unified search | Implemented | Projects, audio, MIDI and plug-in references with result scopes/source navigation and bounded results | Large mixed indexes, ambiguous/native context and keyboard/UIA review |
| Calendar | Implemented | Indexed file-modification dates plus selectable unknown-date group | Unknown/stale timestamps, timezone boundaries and keyboard review |
| Session overview/reports | Implemented | Summary, reader coverage/evidence, tracks, plug-in references, media/timeline and connection sections | Full field coverage, populated/empty/error review and consistent candidate labels |
| Tracks, plug-ins and connections | Implemented subset | Searchable records, details/cards, per-track captured inputs/outputs/sends/direct routes/ports/audio paths with evidence and source offsets, source windows, interactive routing and studio map | Native semantics listed above; cross-archive identities, graph scale, unresolved endpoints and accessibility acceptance |
| Audio browser | Implemented subset | Project/library scope, indexed references, explicit playback preparation, bounded PCM/float WAV peaks and seek/transport | Live backend playback/codec behavior, malformed media and large-library review |
| Audio export/collection | Implemented subset | Separate media copies and external-media ZIP with redacted manifest; explicit SHA-256 duplicate scan | Verified native references before export/collection; ambiguity, cancellation and storage-failure review |
| MIDI browser | Implemented subset | Exported tracks separate from project-note observations; part/group selector, plot/table, pitch names, evidence and retained precision; opens project notes when present | Native timing/ownership verification, full mixed-event semantics, large/narrow/keyboard review |
| MIDI export | Implemented subset | Eligible DAWproject flat tracks to Standard MIDI with explicit timebase checks | Native CPR MIDI export remains unavailable; verify exported files in a MIDI editor/Cubase |
| Media/MIDI index activity | Implemented | Saved-index loading, open-project indexing or source rebuild; scope/budget/progress/cancel; background sorting/grouping and bounded rows | Large libraries and low-memory/locked-database stress; restore/search responsiveness |
| Compare | Implemented subset | Structured track/plug-in/routing changes, matches and ambiguities; native candidate diagnostics separate | Controlled exports with moved/duplicated/missing IDs, sends and enabled-state changes |
| Local plug-in inventory | Implemented subset | Reads VST3 declared metadata/CIDs and available artwork without loading DLLs; bundle footprint and duplicate evidence | VST2 coverage, aliases/vendor conflicts, malformed bundles, measured scale; metadata cannot prove loadability |
| Catalog | Implemented | Source-attributed imports, bounded optional HTTPS images, KVR exact-match advisory import/search and product details | Source terms, offline sources, aliases, attribution and ambiguity acceptance |
| Dependency preflight | Implemented subset | Confirmed structured references compared with declared local inventory; weak evidence stays unknown | Better identity proof, disabled/sidechain cases and Cubase-visible reconciliation |
| Statistics and reports | Implemented | Library/project counts, confirmed-use rankings, audit/footprint reports and formula-safe CSV where applicable | Consistent privacy/provenance, large collections, stable report contracts; PDF remains open |
| Missing-media locator | Open | Presence audit exists for explicitly declared external references | User-selected folder search, hash-first ranking, ambiguity and proposed mapping export without source edits |
| Material design system | Implemented | Neutral slate/white surfaces with blue accents, compact session shell, studio-tools hub, responsive summaries and list/detail views | Complete live acceptance below; remaining custom workspaces need individual visual review |
| UI acceptance | Partial | Actual Skia renders reviewed for home, projects, overview, native MIDI, studio map, audio and no-results at selected light/dark sizes | Full page/state matrix, high contrast, 100–200% DPI, keyboard/UIA, dialog focus and playback acceptance; no blanket visual approval |

## Storage, architecture and delivery

| Feature | Status | Implemented behavior | Remaining work |
| --- | --- | --- | --- |
| Architecture | Implemented | .NET 10, immutable normalized core records, evidence provenance, Infrastructure adapters, Uno Desktop/Skia, MVVM, Microsoft DI and CLI | Keep new format codecs separate from normalized semantics and UI |
| SQLite library | Implemented | Typed project/plug-in/catalog/folder/index documents, browser summaries, streaming/batched index reads and SQL-backed counts | Measured cold/warm startup, large-library query latency and bounded memory |
| Compact asset-index JSON v2 | Implemented | Shared note contexts plus numeric rows; full provenance/precision/nulls; legacy reads and version validation; typed-query and streaming paths tested | Snapshot duplication and other large arrays, SQL search projections, corruption budgets and forward-compatible migrations |
| DB administration | Implemented | Schema/record paging/search, typed edits, bounded read-only SQL, backup/merge/path transfer, dedup/orphan cleanup, integrity/compact | Locked/unwritable DBs, interrupted transfers, legacy migration/rollback and live keyboard review |
| Versioned normalized cache | Open beyond v2 index | Existing indexes are rebuildable; source hash and scan metadata retained | Explicit reader/schema versions and invalidation; source/evidence/normalized separation; selective loading and measured projections |
| Automated verification | Implemented | Synthetic malformed/bounded fixtures, reader/model/report/store tests, Release build/publish paths | Extend per-version semantic corpus and fuzz/memory tests without committing private/copyrighted projects |
| Windows distribution | Partial | Release Desktop/CLI and self-contained publish; optional unsigned local installer | Signed MSIX/MSI, clean-machine smoke tests, upgrade/uninstall, checksums and release automation |
| Security/privacy/accessibility | Partial | Read-only originals, parser limits/cancellation, safe exports, no plug-in execution and evidence labels | Complete input/network/cache/log/privacy audit and assistive-technology acceptance |

## Current evidence and next implementation order

The [typed-note continuation and test audit](research/TYPED_NOTE_ATTRIBUTES_TEST_AUDIT_2026-11-02.md)
records the earlier typed-note batch. The [score follow-up](research/SCORE_EVENT_BOUNDARIES_2026-11-02.md)
adds bounded empty score events and records 639 passing tests, two private-fixture
skips and five additional retired-presentation test removals. The [earlier nested-attribute report](research/CUBASE15_NESTED_ATTRIBUTES.md) records
13 readable CPR/BAK files and 28,564 note candidates, up 1,328 from the retained
[baseline](research/DEMOS_COMPATIBILITY_2026-11-02.md), with no prior values/context
changed and arrangement-level partial files reduced from four to zero. All 21
source hashes matched, including eight correctly rejected AppleDouble files.
This does not establish whole-project completeness.
The [storage design](STORAGE_FORMAT.md) records a 93.1% reduction in serialized
asset-index JSON on these 13 results, not a 93.1% reduction of the whole database.

1. Complete remaining event-attribute objects/strings/containers and score/mixed MIDI.
2. Verify note timebase, placement and ownership against Cubase/exports.
3. Finish inherited track/audio/plug-in/routing/automation codecs field by field.
4. Expand genuine per-version comparisons and malformed-input/resource checks.
5. Add cache invalidation/projections, measure real library performance, and finish
   Material keyboard/accessibility/DPI/state acceptance before distribution. The
   [all-page/menu review](UI_PAGE_MENU_REVIEW_2026-11-02.md) records current rendered checks.

The executable roadmap and acceptance gates are in [PLAN.md](../PLAN.md).
