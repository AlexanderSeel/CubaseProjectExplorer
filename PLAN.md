# Feature roadmap

Updated: 2026-10-31

This plan lists open product outcomes, not completed-work history or test logs. See [README.md](README.md) for current features, [UX_MANUAL_TEST.md](docs/UX_MANUAL_TEST.md) for hands-on acceptance, and the [2026-10-31 implementation snapshot](docs/research/IMPLEMENTATION_VALIDATION_2026-10-31.md) for dated validation evidence.

## P0 — Reader correctness and evidence

- [ ] **Build a permission-cleared, versioned Cubase corpus.** Compare CPR/BAK observations with Cubase from SX through current releases. Keep the user-supplied corpus as pattern-discovery evidence only unless permission is confirmed; never commit private projects.
- [ ] **Read native projects consistently across generations.** Validate RIFF/RIF2 framing, byte order, chunk bounds, compression, nesting, references and crash-backup behavior. Project open always re-runs the reader; supported CPR note, audio-path, mixer and typed-field observations are shown in MIDI, Sample Finder and Audio / MIDI I/O workflows with source offsets. Track ownership, timebase and signal routing remain unverified until independently validated. Preserve bounded structural and token evidence for unsupported files, and show malformed, unsupported, unknown and candidate data distinctly.
- [ ] **Establish native project structure field by field.** Verify track identity/hierarchy/order, plugin instances and ownership, MIDI, tempo, mixer, sends, routing and logical I/O against controlled Cubase projects before presenting them as confirmed.
- [ ] **Complete native MIDI as a usable feature only after validation.** Confirm note ownership, clip placement and timebase against Cubase 10–15 exports before normalized CPR timing or Standard MIDI export is enabled.
- [ ] **Harden readers for untrusted and unusually large projects.** Differentially test malformed CPR, ZIP/XML limits, duplicate IDs, cancellation, partial files and large inputs. DAWproject `project.xml` and `metadata.xml` now use forward-only `XmlReader` parsing, avoiding a retained `XDocument` plus normalized records; XML remains capped at 16 MiB, depth at 128, archive entries at 5,000, and normalized collections have explicit item limits. A synthetic 100,000-note archive passes. The reader still retains normalized results in memory, and no user archive matching the reported 10–14 GB process growth was available; profile a representative archive and trace the owning allocation before declaring the memory issue fully resolved. Keep exports scoped to declared fields and verify nested clips, automation, device state and MIDI timing with genuine exports.

## P1 — Core desktop workflows

- [ ] **Finish Windows acceptance for the workspace.** Review visible startup with large/unavailable databases, the dashboard, project report, movable information/progress windows, saved scan-type settings, navigation and failure/loading states at narrow/wide sizes, 100–200% DPI, light/dark/high-contrast themes, keyboard-only and UI Automation. Follow [the manual checklist](docs/UX_MANUAL_TEST.md).
- [ ] **Make large libraries and scans complete predictably.** Startup project hydration now filters saved rows by the same CPR/BAK/DAWproject selection used for discovery; disabling a type retains its database records but avoids hydrating them into the workspace. Desktop scans process one file at a time and retain compact browser summaries plus separate asset indexes rather than all full snapshots/routing arrays; exact checksums are skipped for files above 1 GiB. Scan document/index upserts are split into 32-record batches; cancellation is honored between batches, and an idle end-of-scan `wal_checkpoint(TRUNCATE)` reclaims the journal while a concurrent reader safely defers truncation. On 2026-10-31, the configured `D:\Cubase\cubase-library.db` measured 3.46 GB with a 3.73 GB WAL while the existing app process reached about 20 GiB working set and became unresponsive; its selected scan types were CPR and BAK, with DAWproject disabled. This points to a large-library hydration or scan-save path, not proof that excluded DAWproject source files were opened. These changes need an isolated run against that database to verify actual memory and WAL behavior. Prior read-only measurements against a different 3,436-record database (423 MB SQLite file) measured browser loading at 12–15 s / 484–516 MiB peak working-set growth and plugin statistics at 5.5–6.5 s / 98–139 MiB growth. The earlier database contained 954,828 serialized plugin references (about 323 MiB); compacting/search-indexing the plugin corpus is still required. Verify scan/rescan, cancellation and completion for large libraries and projects without tying up the UI.
- [ ] **Keep library browsing responsive at realistic scale.** Measure search, filtering, grouping, sorting, card/detail switching, navigation history and exports on large indexes; preserve selection and row-action behavior.
- [ ] **Complete project-to-media workflows.** Validate Sample Finder playback, WAV previews, media collection/export and project-scoped actions only with confirmed references. Native CPR path observations are surfaced in the expanded Sample Finder section; keep ambiguous paths unavailable as export/collect references until their ownership is validated.
- [ ] **Finish calendar and database administration workflows.** Validate unknown/stale file dates, transfer/merge behavior, cleanup, compact, locked/unwritable failures, paging and keyboard use.

## P2 — Studio inventory and comparison

- [ ] **Improve plug-in inventory and catalog trust.** Validate local VST2/VST3 metadata, CID associations, artwork and dependency preflight without executing plug-ins or claiming metadata proves loadability. Confirm catalog source terms, attribution, aliases, vendor conflicts, offline imports and duplicate handling; keep matches advisory.
- [ ] **Strengthen DAWproject comparison.** Validate moved/duplicate tracks and plug-in instances, missing IDs, ambiguous ownership, sends, routing and enabled-state differences against genuine exports.
- [ ] **Improve studio diagnostics from verifiable data.** Research sidechains, group/FX nesting, send levels and physical I/O only from independent evidence; keep hardware state unknown until confirmed.
- [ ] **Add a safe missing-media locator.** Search only user-selected folders, rank exact hashes above weaker matches, explain ambiguity and export proposed mappings without editing projects.
- [ ] **Keep reports consistent and useful.** Preserve provenance, candidate labels, timestamps, privacy, logical disk size and shared-bundle accounting. PDF output is a later extension.

## P3 — Distribution and scope

- [ ] **Prepare a supported Windows release.** Assess signed MSIX/MSI packaging, prerequisites, upgrades, uninstall, clean-machine smoke tests and checksums. Current distribution is unsigned.
- [ ] **Complete security, privacy and accessibility review.** Cover untrusted CPR/ZIP/XML input, catalog networking/caches, logging, exported paths/UIDs, assistive technology and large-library performance.
- [ ] **Consider pre-CPR `.arr`/`.all` only as a separate format feature.** Require a separately documented grammar and licensed fixtures; do not route these files through the CPR reader.

## Gated — Native CPR writing

No native CPR writer or plugin-deactivation feature is approved. Keep this gate closed until the reader correctness work above is complete and independent Cubase round-trip validation covers each supported version.

- [ ] Verify per-version plug-in instance selection, state encoding, offsets, checksums and preservation of unrelated/unknown bytes using independent fixtures.
- [ ] Validate dry-run diffs, destination safety, rollback/rejection, cancellation and unchanged-source guarantees.
- [ ] Round-trip copies through Cubase across versions; independently verify plug-in state, project integrity, playback, reopen and crash behavior.
- [ ] Only then consider a separate opt-in copy workflow with preview and precise outcome reporting. A byte-identical safety copy is not a plugin-disabled project.
