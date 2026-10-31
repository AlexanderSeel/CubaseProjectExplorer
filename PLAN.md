# Feature roadmap

Updated: 2026-10-31

This plan lists open product outcomes, not completed-work history or test logs. See [README.md](README.md) for current features, [UX_MANUAL_TEST.md](docs/UX_MANUAL_TEST.md) for hands-on acceptance, and the [2026-10-31 implementation snapshot](docs/research/IMPLEMENTATION_VALIDATION_2026-10-31.md) for dated validation evidence.

## P0 — Reader correctness and evidence

- [ ] **Build a permission-cleared, versioned Cubase corpus.** Compare CPR/BAK observations with Cubase from SX through current releases. Keep the user-supplied corpus as pattern-discovery evidence only unless permission is confirmed; never commit private projects.
- [ ] **Read native projects consistently across generations.** Validate RIFF/RIF2 framing, byte order, chunk bounds, compression, nesting, references and crash-backup behavior. Preserve bounded structural and token evidence for unsupported files, and show malformed, unsupported, unknown and candidate data distinctly.
- [ ] **Establish native project structure field by field.** Verify track identity/hierarchy/order, plugin instances and ownership, MIDI, tempo, mixer, sends, routing and logical I/O against controlled Cubase projects before presenting them as confirmed.
- [ ] **Complete native MIDI as a usable feature only after validation.** Confirm note ownership, clip placement and timebase against Cubase 10–15 exports before normalized CPR timing or Standard MIDI export is enabled.
- [ ] **Harden readers for untrusted and unusually large projects.** Differentially test malformed CPR, ZIP/XML limits, duplicate IDs, cancellation, partial files and large inputs. Keep DAWproject exports scoped to declared fields and verify nested clips, automation, device state and MIDI timing with genuine exports.

## P1 — Core desktop workflows

- [ ] **Finish Windows acceptance for the workspace.** Review the dashboard, project report, movable information/progress windows, saved scan-type settings, navigation and failure/loading states at narrow/wide sizes, 100–200% DPI, light/dark/high-contrast themes, keyboard-only and UI Automation. Follow [the manual checklist](docs/UX_MANUAL_TEST.md).
- [ ] **Make large libraries and scans complete predictably.** Resolve high memory/time during hydration of the existing 3,436-record library (about 416 MB serialized JSON observed), then verify scan/rescan, cancellation, progress and completion for large libraries and large DAWproject archives without tying up the UI.
- [ ] **Keep library browsing responsive at realistic scale.** Measure search, filtering, grouping, sorting, card/detail switching, navigation history and exports on large indexes; preserve selection and row-action behavior.
- [ ] **Complete project-to-media workflows.** Validate Sample Finder playback, WAV previews, media collection/export and project-scoped actions only with confirmed references. Keep ambiguous native CPR path tokens unavailable as playable media.
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
