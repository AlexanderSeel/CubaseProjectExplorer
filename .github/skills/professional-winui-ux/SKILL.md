---
name: professional-winui-ux
description: Professional Microsoft Fluent 2 / WinUI 3 desktop UX design, scan-result exploration, data grids, hierarchy and interactive signal-flow visualization.
---

# Professional WinUI / Fluent UI Designer

Use this skill for every CubaseProjectExplorer change to WinUI navigation, page layout,
data presentation, filter behavior, project browsing, visualizations, accessibility,
or UI-specific view models and controls.

## First actions — mandatory

1. Read `AGENTS.md`, `PLAN.md` (the **P1 — Professional UI/UX redesign** section),
   `src/CubaseInspector.WinUI/MainWindow.xaml` and relevant view models.
2. Inspect the *current* rendered Windows UI screenshots when available; otherwise
   explicitly mark visual review **pending**, not passed. Look for actual usability
   problems before proposing decorative changes.
3. Map core user tasks in priority order: scan folder → see all found projects and
   scan issues → search/sort/filter → select project → inspect details/hierarchy →
   trace signal flow → compare/export. Make the next user action obvious.
4. Preserve source-data evidence levels: CPR candidate tracks are *not* confirmed
   Cubase tracks; filename matches are *not* verified installations; unknown
   BPM/channel routing is *unknown*, not zero or no connection.

## WinUI professional design principles

- Use Windows App SDK / WinUI 3, Fluent 2 conventions, theme resources and
  consistent typography, spacing, content hierarchy and appropriate icons.
- Prefer a stable navigation shell (NavigationView or equivalent), meaningful
  command bars, readable master-detail layouts and predictable back behavior.
  Avoid accumulating dozens of equally prominent tabs or toolbars.
- All controls must have deliberate bounds; no overlap, unnecessary reserved
  whitespace, unlabeled rows or value text floating without context.
- Apply progressive disclosure: compact list, sortable detailed grid and
  browseable cards; explicitly define what data each view and density level
  provides. Keep selection and filters coherent across modes.
- Data grids must offer headers, sensible column widths, alignment, sort
  indicators, resizing, optional visible-column presets and virtualization.
  Lists/cards need empty/loading/error states, hover/selection/focus feedback,
  right-click menus when useful and a working keyboard path.
- Use restrained semantic colors and icons, with evidence/status meaning
  explained in words. Light, dark and high-contrast modes must all be usable.
  Prefer visual polish through spacing, contrast, hierarchy and interaction
  consistency rather than extraneous gradients or fake hardware controls.
- Ensure interaction at 100–200% Windows DPI; test narrow/windows, long
  filenames/paths, non-English strings, and very large collections.

## Scanned-project explorer — priority acceptance criteria

A folder scan MUST leave users in an obvious, first-class project library.
Immediately show total found, parsed, failed, candidate/partial and displayed
counts, plus the scanned folder, elapsed/progress and useful next actions.

Each selectable project supports:
- file name and relative path, CPR/BAK/DAWproject type, detected version
- size and file modification time with correct provenance
- track/plugin counts with evidence label (confirmed/candidate)
- warnings/error status and clear selection/inspect action

Provide Cards/Tiles, dense Details Grid, Compact List and optional folder tree.
Provide Compact/Standard/Expanded information density with sane truncation and
tooltips. Keep selection across view modes; dedicated project filter controls
must not silently filter away unknown BPM. Show scan failures explicitly and
offer retry where possible. Preserve the ability to return to the library
after drilling into a project's tracks or plugins.

## Hierarchy and graphs

- Separate filesystem folder tree from verified Cubase channel/track hierarchy.
- Routing: use a real interactive directed/layered node-edge graph when data is
  verified. Represent FX buses, sends, sidechains and output distinctly.
  Include pan/zoom/fit, focus, selection details, legend and an accessible
  linear/tabular fallback; explain unknown endpoints, cycles and evidence.
- Avoid a misleading graph for CPR candidate-only records. Indicate that
  routing is unavailable until the parser has trustworthy edges.
- Charts always show units, scale/counts, provenance and a readable table.

## Implementation and validation routine

1. Deliver a small UI audit + wireframes and map it back to UX-* plan items.
2. Implement modular WinUI views/components and observable view models; keep
   parsing services unchanged unless necessary. Prefer measured improvements
   over rewriting unrelated screens.
3. Add view-model tests for filtering, selection, switchable view modes,
   unknown values and scanning/cancellation.
4. Run xUnit tests and CLI + WinUI x64 Release builds, resolve regressions.
5. Inspect actual Windows screenshots for *populated, empty, error and
   filtering* states at different window sizes and light/dark/high-contrast.
   **Building and geometric test assertions are not visual approval.**
6. Update `PLAN.md` with screenshots and CI/test evidence; commit logically
   grouped work on `main`. Do not mark visual tasks done without real review.

### Definition of done

A first-time user can open the app, scan a mixed project folder, immediately
see and filter all discovered projects, understand errors and evidence, choose
a project, inspect structured details and return to results without searching
for a hidden tab. No overlapping controls, missing labels, deceptive status
colors, or unreadable data at common Windows DPI sizes.
