# Cubase Project Explorer

A Windows-first, read-only **Cubase project library and diagnostics app** built with **C# / .NET 10, Uno Platform Desktop (Skia), MVVM and Microsoft DI**. It includes a local SQLite-backed library, a headless CLI and regression tests. The project is independent of Steinberg and third-party CPR Inspector products.

The current application version is **1.0.0.0**. The version is shared by the app, CLI and .NET assemblies.

> **Evidence boundary:** Native `.cpr` and `.bak` support is experimental, bounded, candidate-level inspection. The reader tries RIFF/32-bit and RIF2/64-bit structure paths, then falls back to bounded token evidence when structure is incomplete. Observed plugin UID/name tokens, tentative track records, MIDI markers and mixer values are not confirmed plugin instances, track ownership, usable VST3 IDs, complete mixer/routing state or plugin-use history. `.dawproject` data is confirmed only as fields declared by that export; it does not prove what Cubase retained or whether a plugin loads. The app does not modify project files or execute plugin binaries.

The DAWproject reader processes `project.xml` and `metadata.xml` forward-only, without materializing an XML document tree. Archive, XML size/depth and normalized item-count limits remain in force; parsed normalized records are retained for the open project, so peak memory still depends on how much supported content the archive declares.

Folder scans keep compact browser summaries and asset indexes instead of retaining full routing, track, event and candidate arrays for every project until the scan completes. The database retains plugin references and summary counts; opening a project always re-runs its reader to show current full details. Large source files above 1 GiB are not read end-to-end for exact duplicate checksums.

## Get the app

Open the [Windows CI workflow](https://github.com/AlexanderSeel/CubaseProjectExplorer/actions/workflows/windows-ci.yml) and download artifacts from a successful `main` run:

- **Uno Desktop win-x64:** self-contained desktop app, with an optional unsigned user-local PowerShell installer/uninstaller.
- **CLI win-x64:** standalone command-line inspector.

See [Windows installation](docs/INSTALL_WINDOWS.md) for details. To build on Windows with the .NET 10 SDK:

~~~powershell
./build.ps1 -Configuration Release
dotnet run --project src/CubaseInspector.App/CubaseInspector.App/CubaseInspector.App.csproj
~~~

Cubase is not required to inspect files. A local Cubase installation and genuine projects are needed to independently validate version-specific findings.

## Desktop features

Every workspace page uses the same title, description and contextual action header. Informational and evidence actions open separate resizable windows; filters and action menus use dismissible anchored popups. Major result pages use a shared data browser: **Details** uses Uno's CommunityToolkit DataGrid; switch between **Details** and **Cards**, sort by column headers, group and collapse group headings, and use row context actions. Cards load at most 32 rows per page to bound view creation on large libraries. Available pages include:

- **Project Library:** scan folders and rescan the saved folder; merge project results from multiple roots; use stored raw-file SHA-256 checksums to list byte-identical copies once, even when discovered at different paths; search and filter by file type, evidence, version, folder and warnings; group and sort projects; inspect scan issues in a popup; export visible results as CSV, Markdown or JSON. Legacy entries without a checksum remain path-keyed until rescanned. The checksum proves byte identity only, not semantic or musical equivalence.
- **Search everything:** search saved projects and indexed audio, MIDI and plugin references from one page. Scope by result type and jump to the source project or corresponding media/MIDI browser. Native CPR MIDI observations are searchable as explicitly unassigned candidates.
- **Project Calendar:** browse indexed projects by local project-file modification date. Projects without a readable timestamp remain in a selectable **Date unknown** group. These are file dates, not song creation dates.
- **Project workspaces:** inspect overview, track/channel records, plugin references, declared routing and logical Audio/MIDI I/O for a selected project. Opening a project re-reads it; recovered CPR track, audio-path, mixer and typed-field observations appear with offsets and candidate labels. Confirmed exported fields remain separate from native observations.
- **Sample finder:** search indexed audio references by file type and project/track/path context, or open it scoped to a selected project's known media references from Project Overview. Select a reference to prepare playback when supported by the media backend, inspect a bounded PCM/IEEE-float WAV peak overview, export a copy, or collect supported external media into a separate ZIP with a path-redacted manifest. An explicit scan can find byte-identical external files by SHA-256; it does not infer audible similarity. Source projects and media are not rewritten. The native CPR reader also surfaces structurally nested audio-path candidates with possible track-name context, kept separate from confirmed media uses and labelled unverified; path tokens are not treated as proof of an audio clip or track assignment.
- **MIDI tracks:** choose native CPR observations or exported DAWproject tracks. Native notes are grouped by project and possible part, with a raw-position pitch plot, a compact sortable note table, readable numeric formatting and source evidence on demand. Packed MMidiPart streams and typed-attribute MMidiNote bodies are inspected in RIFF/RIF2; a read-only sample covers saved-by hints from Cubase 10 through 14, alongside the controlled Cubase 15 fixtures. Possible track/part labels remain candidates and native Standard MIDI export stays unavailable. DAWproject tracks retain their declared-timebase preview and eligible Standard MIDI export.
- **Compare projects:** compare supported structured exports and review matches, changes and ambiguities. Native CPR comparisons remain candidate-evidence diagnostics.
- **Plug-ins and catalog:** inspect project plugin references, import source-attributed catalog data into the local database from configured sources, and optionally save source-supplied HTTPS product images. Catalog matches are advisory. The local VST inventory reads declared metadata without loading plugin binaries; metadata does not establish installation or loadability. Plugin cards show available vendor, version, class, path and artwork evidence directly.
- **Dependency preflight:** review confirmed structured plugin references against local inventory evidence. Weak or missing evidence is not automatically called a missing plugin.
- **Statistics, reports and tools:** review project/library summaries, confirmed plugin reference rankings, scan/audit reports and local bundle footprint. Export formats vary by report; CSV output is formula-safe where supported.
- **Studio overview and settings:** view local library counts and recent project files; choose whether future scans include CPR, BAK or DAWproject files. Existing indexed records are retained when a type is disabled.
- **Library database administration:** get fast SQL-backed counts without hydrating catalog images; inspect SQLite tables, columns and row counts; page/search JSON document records; create, update and delete supported project, plug-in, catalog, scan-folder and project asset-index records; and run bounded read-only SQL queries (up to 500 rows) or prepare a table preview from the schema list. The editor writes through the typed document store; direct SQL mutations and edits to unknown document types are not exposed. Also inspect active and next-launch paths/storage, select or copy the database to a new location, export a consistent SQLite backup, merge another compatible library, remove checksum-identical project records and orphaned media/MIDI indexes, and run an integrity check/compact. Cleanup changes indexed metadata only; original project files are untouched.

The app opens into the workspace and reports staged loading of saved local database content. Full Windows visual, playback, accessibility and large-library acceptance is still in progress; see the [manual UX checklist](docs/UX_MANUAL_TEST.md) and [open implementation plan](PLAN.md).

## What the readers establish

| Data | Native `.cpr` / `.bak` | Structured `.dawproject` |
| --- | --- | --- |
| Container/version | Candidate RIFF/32-bit or RIF2/64-bit structure and saved-by hints; bounded token fallback if structure cannot be decoded | Exported producer and metadata |
| Native archive detail | Bounded all-ARCH class-header inventory: unknown classes, serialization versions, absolute offsets/sizes, symbol references, candidate containment and explicit limits; anonymous binary-derived fields retain raw bits | Not applicable |
| Tracks | Candidate structural records; ownership/hierarchy are not generally verified | Confirmed exported track records and declared fields |
| Plugins | UID/name/property markers are unverified observations | Exported plugin class/name and declared owner/slot fields |
| MIDI | Bounded packed MMidiPart and typed MMidiNote candidates in RIFF/RIF2, with source layout and possible track/part context; no normalized CPR MIDI | Supported flat note events when declared timing is sufficient |
| Mixer/routing/I/O | Observations are unverified; no verified native signal graph | Declared logical fields/edges; physical hardware remains unknown |
| Editing | No project editing; safe-copy command makes a byte-identical copy | Read-only inspection and separate media/MIDI exports |

See [CPR testing](docs/TESTING_CPR_ON_WINDOWS.md), [format research](docs/CPR_FORMAT_RESEARCH.md) and [architecture](docs/ARCHITECTURE.md) for evidence details. The app does not disable plugins in a CPR file. Native writing requires Cubase round-trip verification and remains gated.

## CLI examples

~~~powershell
# Inspect project and export a report
dotnet run --project src/CubaseInspector.Cli -- inspect "C:\Projects\Session.cpr" --markdown
dotnet run --project src/CubaseInspector.Cli -- inspect "C:\Exports\Session.dawproject" --coverage-json
dotnet run --project src/CubaseInspector.Cli -- --version

# Inspect native structure/plugin candidate evidence
dotnet run --project src/CubaseInspector.Cli -- cpr-structure "C:\Projects\Session.cpr" --json
dotnet run --project src/CubaseInspector.Cli -- cpr-plugins "C:\Projects\Session.cpr" --csv --unresolved-only

# Scan, audit, compare and check dependencies
dotnet run --project src/CubaseInspector.Cli -- scan "C:\Projects"
dotnet run --project src/CubaseInspector.Cli -- audit "C:\Projects" --json
dotnet run --project src/CubaseInspector.Cli -- diff "C:\Exports\A.dawproject" "C:\Exports\B.dawproject" --json
dotnet run --project src/CubaseInspector.Cli -- preflight "C:\Exports\Session.dawproject" --markdown

# Make an additional byte-identical copy (never a disabled-plugin rewrite)
dotnet run --project src/CubaseInspector.Cli -- safe-copy "C:\Projects\Session.cpr" "C:\Projects\Session-copy.cpr"
~~~

`cpr-structure --json` includes detailed `ArchiveInventories` for both recognizable
container variants, including non-arrangement streams. Their record matches and
anonymous raw fields remain candidates; one Cubase 15 executable cannot establish
correct decoding across every Cubase release.

See CLI help for remaining `io`, `routes`, `plugins`, `footprint` and `footprint-audit` commands.

## Validation and repository

The latest automated verification recorded **439 passing Release tests**; Uno Desktop and CLI Release builds passed with zero warnings/errors. Native MIDI observations remain candidate-level and do not establish full musical decoding. Detailed dated validation, fixture limits and live-review notes are in the [implementation snapshot](docs/research/IMPLEMENTATION_VALIDATION_2026-10-31.md). Large-library performance measurement and full Windows visual, accessibility, playback and scale acceptance remain open in [PLAN.md](PLAN.md). Project binaries and the full disassembly remain untracked.

- `src/CubaseInspector.Core` — normalized models, evidence, filters, comparisons and reports.
- `src/CubaseInspector.Infrastructure` — project readers, scanner, database, catalog and local metadata services.
- `src/CubaseInspector.Cli` — command-line inspection and reporting.
- `src/CubaseInspector.App/CubaseInspector.App` — Uno Desktop application.
- `tests/CubaseInspector.Tests` — regression and safety tests.

[Windows install](docs/INSTALL_WINDOWS.md) · [UX checklist](docs/UX_MANUAL_TEST.md) · [CPR research](docs/CPR_FORMAT_RESEARCH.md) · [Plan](PLAN.md)
