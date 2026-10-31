# CPR Inspector — Product Reference and AI Development Context

> Source: https://courses.domsigalas.com/cpr-inspector  
> Reviewed: 2026-10-28  
> Purpose: Structured context for an AI assistant or coding agent analyzing, designing, or implementing a comparable Cubase project inspection tool.  
> Status: This is an independently written summary of publicly advertised functionality, **not** official technical documentation or a specification of the proprietary implementation.

## 1. Product Overview

**CPR Inspector** is a standalone macOS application from Dom Sigalas / London Loft Productions Ltd. It inspects Steinberg Cubase `.cpr` project files directly, without launching Cubase or loading their audio plugins.

Its purpose is to help music producers, mixing engineers, and collaborators understand project structure, dependencies, routing, and changes between project versions before opening a session in a DAW.

### Core value propositions

- Inspect Cubase projects without opening Cubase.
- Identify plugins used in a project, including plugins missing from the local computer.
- Review channels, routing, sends, side-chains, and audio/MIDI device configuration.
- Compare two versions of a project to identify mix changes.
- Recover access to projects that fail to load because of problematic plugins by creating a modified copy.
- Analyze plugin usage across a collection of projects.
- Export project summaries and reports.

## 2. Advertised Feature Inventory

### 2.1 Plugin Inspector

- Enumerate every plugin instance referenced by the project.
- Group results into virtual instruments, Steinberg effects, and third-party effects.
- Show plugin manufacturer, version, number of instances, and affected channels.
- Identify plugins not installed on the local Mac and flag missing dependencies visually.
- Use Cubase's own plugin lists, built-in plugin inventory, and Waves plugin folder for local availability checks.

**Expected questions answered:** Which plugins are used? Which channels depend on a given plugin? Which plugins must be installed before opening the project?

### 2.2 Track and Channel Inspector

- List tracks/channels with their type, channel fader level, insert plugins and versions, and output destination.
- Filter channel lists by audio, instrument, group, FX, or output.
- Search by track name or plugin name.

**Expected questions answered:** What is in this project? Where are effects inserted? How is each track routed?

### 2.3 Safe Copy / Plugin Deactivation

- Select plugins that should not load when the project is opened in Cubase.
- Produce a **new copy** of the `.cpr` project in which the selected plugins are disabled.
- Keep the original project unchanged.
- Intended for troubleshooting Cubase sessions that crash during loading.

**Important:** The website does not document the proprietary CPR serialization structure or the exact mechanism used to disable plugin instances.

### 2.4 Signal Flow and Routing Visualization

- Select a channel and display its signal flow.
- Show upstream sources, signal processing, and downstream destinations through to final output.
- Include sends, group buses, and side-chain relationships.
- Provide a destination-oriented alternative view showing everything feeding a selected bus.

**Expected questions answered:** What feeds this group? Which tracks contribute to this output? What channel provides the side-chain signal?

### 2.5 Audio and MIDI I/O Inspection

- Identify the audio interface saved in the project.
- Enumerate audio input buses and connections to interface inputs/outputs.
- Enumerate MIDI ports.
- Help identify device mappings that may need to change when transferring projects between studios.

### 2.6 Project Statistics

- Visualize the distribution of track types.
- Chart plugin instance counts grouped by third-party effects, Steinberg effects, and instruments.
- Show plugin installation statistics.
- Rank the most-used plugins and their manufacturers.

### 2.7 Two-Project Comparison

- Load two Cubase `.cpr` projects (Project A and Project B).
- Compare them channel-by-channel.
- Highlight added, removed, and replaced plugins.
- Detect changes in channel settings, fader values, sends, and routing.
- Show **before and after** values for each identified change.

**Expected questions answered:** What changed between two mix versions? Which routing or effects changes explain the difference?

### 2.8 Dust Collector / Cross-Project Plugin Usage

- Scan an entire folder of Cubase projects.
- Rank plugins by the number of projects that reference them.
- Show when plugins were last used.
- Show storage occupied by plugins and identify rarely used or unused plugins.
- Help users decide which plugins are worth retaining.

**Note:** The advertised feature does not publish the exact algorithm for determining last use or plugin disk consumption.

### 2.9 Reporting and Export

- Export a full PDF report.
- Export CSV inventories of tracks and plugins.
- Export a Markdown project summary.
- Copy a short summary to the clipboard for sharing via email or chat.

## 3. Supported Platforms and Formats (Vendor Claims)

| Dimension | Advertised support |
|---|---|
| Application OS | macOS 13 Ventura or later |
| Processor | Apple Silicon M1 or later |
| RAM | 8 GB minimum; 16 GB recommended for large projects |
| Disk | Approximately 50 MB for application |
| Cubase projects | Cubase 12–16; Pro, Artist, and Elements |
| Project authoring platform | CPR files saved on macOS or Windows |
| Intel Mac | Not officially supported |
| Windows application | Not currently offered |
| Nuendo / Cubasis | Not officially supported |
| Cubase required to inspect CPR | No |
| Cubase required for local installed-plugin identification | Cubase 12+ on the Mac |
| Internet connection | Not required |
| Data processing | Vendor states fully local/offline |

Older Cubase project files may be partially readable, with reduced detail. Plugin preset names can only be displayed when stored in a readable form; many plugin formats encapsulate these settings in opaque data.

## 4. User Workflows

### Workflow A — Preflight an unfamiliar project

1. Select or drop a `.cpr` file.
2. Parse its metadata, tracks, plugin instances, and routing.
3. Inspect missing plugins and mismatched studio I/O.
4. Export a dependency report before opening Cubase.

### Workflow B — Troubleshoot a crashing project

1. Read the `.cpr` file without loading plugins.
2. Locate suspected crashing plugin instances.
3. Select the instances to deactivate.
4. Create a modified **copy** of the project, leaving the source untouched.
5. Attempt to open the modified copy in Cubase.

### Workflow C — Compare two mixes

1. Load two `.cpr` versions.
2. Match corresponding channels and plugins where possible.
3. Identify inserts, settings, levels, sends, and route differences.
4. Inspect before/after values.
5. Export a change report.

### Workflow D — Audit plugin dependencies

1. Select a root folder containing multiple Cubase projects.
2. Inventory each project's plugin references.
3. Aggregate references and use statistics.
4. Identify frequently used, rarely used, and potentially unneeded plugins.

## 5. Conceptual Data Model (Suggested, Not Documented)

The following model is useful as **AI implementation context**, but is not asserted to be CPR Inspector's internal architecture.

```text
Project
  id, filePath, name, cubaseVersion, metadata
  channels[]
  pluginInstances[]
  deviceConfiguration
  routingEdges[]

Channel
  id, name, type, faderLevel, mute, solo
  inserts[], instrument?, sends[], outputRoute

PluginInstance
  id, pluginId, manufacturer, name, version
  channelId, slot, role, presetName?, serializedState?

RoutingEdge
  sourceChannelId, destinationChannelId
  kind: output | send | sidechain | input
  gain?, enabled?

DeviceConfiguration
  interfaceName?, audioInputBuses[], audioOutputBuses[]
  midiPorts[]

PluginInventoryEntry
  pluginId, name, manufacturer, installedVersion?
  availability: installed | missing | unknown

ProjectDifference
  entityType, entityIdentity, propertyPath
  beforeValue, afterValue, changeType
```

Fields such as mute, solo, plugin IDs, serialized state, and route gain represent **possible schema fields** to validate during development; the website does not guarantee their extraction.

## 6. Suggested Engineering Requirements for a Comparable Tool

These are recommendations for a new project, **not verified features or implementation details of the vendor product**.

### Parsing and data integrity

- Treat `.cpr` as a versioned, potentially undocumented binary/container format; first research and experimentally validate its structure.
- Support multiple file-format variants through isolated parser adapters.
- Never rely on file extensions alone to validate input.
- Parse bounded binary data defensively, with decompression and resource limits where relevant.
- Preserve unknown fields as opaque data when round-tripping files.
- Distinguish absent, unknown, unreadable, and successfully extracted values.
- Maintain regression fixtures for Cubase version and edition combinations.

### Read-only by default

- Inspection should not alter original files.
- Any Safe Copy operation must write to a different destination and retain a backup-friendly workflow.
- Do not advertise project-file rewriting as safe until transformed files have been round-trip tested in Cubase.
- Never execute embedded plugin binaries or load plugin state merely to inspect project structure.

### Cross-platform direction (optional extension)

- Separate parser, normalized project model, plugin discovery, routing engine, diff engine, and export engine.
- Provide OS-specific local-plugin discovery adapters if supporting Windows alongside macOS.
- Prefer a deterministic headless parsing library that can be used by desktop UI, CLI, and automated tests.

### Comparison design

- Match channels using stable project identifiers when available; otherwise use a clearly labeled heuristic involving channel type, names, and connectivity.
- Avoid silently equating similarly named plugins or tracks.
- Classify additions, deletions, replacements, moved instances, parameter changes, and routing changes separately.
- Report confidence when identity matching is ambiguous.

### Routing design

- Build a typed directed multigraph of channels and connections.
- Preserve output, send, and side-chain edge types.
- Handle cycles and unresolved endpoints safely.
- Support upstream/downstream tracing and bus-centric views.

### Plugin dependency audit

- Maintain a normalized plugin identity with manufacturer and format identifiers where accessible.
- Preserve the distinction between missing and unrecognized plugins.
- Do not infer plugin compatibility solely from similar display names.
- Make storage footprint and last-used timestamps explicitly sourced and caveated.

### Testing and validation

- Create fixture projects for Cubase 12, 13, 14, 15, and 16 where legally available.
- Cover audio, instrument, MIDI, group, FX, output, sends, side-chains, and nested routing.
- Test missing plugins, projects with renamed channels, duplicate plugin instances, and corrupted files.
- For each supported project mutation, validate opening the generated file in actual Cubase, not just parser re-readability.
- Compare output against Cubase's UI to detect parser inaccuracies.

## 7. AI Agent Context / Copy-Paste Instruction

> Build or assess a standalone Cubase `.cpr` project inspection application using the **CPR Inspector feature inventory above as a product reference**, not as evidence of the original app's source code or internals. Prioritize reliable offline parsing, a normalized data model, plugin/track inventories, routing visualization, two-project comparison, project-wide statistics, and Markdown/CSV/PDF exports. Treat Safe Copy as a separately gated, high-risk capability requiring format research and validation in Cubase. Separate documented behavior from assumptions and identify any unknown aspects of Cubase's proprietary file format. Design for clean architecture, modularity, test fixtures, and predictable behavior on incomplete or unknown files. Do not claim functionality is implemented until it is exercised by automated tests and, when applicable, verified in Cubase.

## 8. Known Unknowns / Research Needed

- Exact `.cpr` serialization and compression rules across Cubase 12–16.
- Whether every advertised plugin/channel setting is available in all project variants.
- Stable identities and matching semantics for individual tracks and plugin instances.
- Full handling of encrypted, opaque, or proprietary third-party plugin state.
- Exact procedure used by the vendor's Safe Copy feature.
- Reliable Windows plugin inventory sources and compatibility normalization.
- Exact representation of device ports, send levels, side-chains, and route graph edges.
- Whether plugin disk usage and last-used data are determined from project files, filesystem metadata, or other local indexes.

## 9. Reference

Primary public source: https://courses.domsigalas.com/cpr-inspector

All product capability and compatibility descriptions in sections 1–4 are summarized from the public vendor page accessed on 2026-10-28. Sections 5–8 are independent design suggestions and analysis, not vendor claims.
