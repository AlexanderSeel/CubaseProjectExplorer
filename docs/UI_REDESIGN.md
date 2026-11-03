# Studio workspace design and acceptance

The primary user is a producer reviewing a saved session: find a project,
understand its tracks/instruments/media, inspect a connection, compare or export,
and return to the library. Source evidence remains available without making
binary-format terminology the main workflow.

## Design system

- Uno Material Design 3 with one `MaterialToolkitTheme`, neutral graphite/slate
  dark surfaces, white/light-gray surfaces and a blue action accent.
- Studio home leads with Open a project and Add a project folder; library
  statistics and recently changed sessions follow. Dates remain file dates.
- Compact navigation keeps the library and selected session visible; a studio-tools
  hub holds secondary workflows instead of a long permanent menu.
  Help and appearance stay in the footer; breadcrumbs retain back/forward paths.
- Shared page titles, contextual actions, rounded surfaces, readable dense grids,
  card browsing, segmented view choices and consistent source-detail windows
  carry the design through all workspaces.
- Material resource roles are used for theme-aware XAML colors. Typography uses
  the actual short Material keys and controls inherit Material styles.
- Loading, empty, filtered-empty, failed and partial states retain feedback and
  a recovery action. Unknown native fields remain unknown or candidate evidence.

```text
Brand / studio context                         Local · read only
Back / forward / current location
Library | Page title + context                Relevant actions
Session | Home: open/add hero → metrics → recent sessions
Tools   | Projects: search/filter → cards or detailed grid
        | Session: summary → tracks/plugins/media/connections
Help    | Source details and advanced options stay one action away
Settings| Persistent activity, progress and cancellation
```

## Review method

An isolated preferences file (`CPE_PREFERENCES_PATH`) selects a scratch SQLite
database containing read-only demo results. Optional `CPE_PREVIEW_PROJECT`,
`CPE_PREVIEW_PAGE`, `CPE_PREVIEW_QUERY` and `CPE_CAPTURE_FILE` drive review states.
Capture uses the real Skia visual tree through `RenderTargetBitmap`, not a web
mockup or a reconstructed drawing. These variables are only honored together
with the explicit preferences path; normal preferences and the user's library
are not changed by review sessions.

Native Windows `PrintWindow` returned black for the hidden GPU window, so it is
not accepted as visual evidence. Actual rendered captures are kept under ignored
`artifacts/demos/`. Builds alone do not approve the UI.

## Acceptance

- [x] Selected populated home, project cards, session overview, native MIDI,
  audio-path and studio-map renders inspected.
- [x] Selected empty-library, no-filter-results and partial-project renders inspected.
- [x] Selected wide/narrow renders in light and dark themes inspected.
- [ ] Full workspace/state cross-product, missing-source recovery and routing interaction.
- [ ] Keyboard focus/activation, Escape and back/forward behavior.
- [ ] High contrast, screen-reader/UIA names and announcements.
- [ ] 100%, 125%, 150% and 200% DPI on a live Windows desktop.
- [ ] Playback, source-detail dialogs and large-library scroll responsiveness.

## Rendered review checkpoint, 2026-11-02

| State | Requested outer window | Theme | Local artifact |
| --- | --- | --- | --- |
| Home, 13 demo entries | 1440×960 | Dark | `home-dark.png` |
| Home, narrow | 960×780 | Light | `home-light-narrow.png` |
| Project cards | 1440×960 | Dark | `projects-dark.png` |
| Overview, populated | 1440×960 | Dark | `overview-dark.png` |
| Overview, narrow long title | 960×780 | Light | `overview-light-narrow.png` |
| MIDI, actual native groups/plot/notes | 1440×960 | Dark | `midi-themed.png` |
| MIDI, compact selector | 960×780 | Light | `midi-light-narrow.png` |
| Studio map, candidate node | 1440×960 | Light | `studio-light.png` |
| Audio, native paths | 1440×960 | Dark | `audio-native-dark.png` |
| Audio, stacked details | 960×780 | Light | `audio-light-narrow.png` |
| No project filter matches | 1440×960 | Dark | `filtered-empty.png` |
| Empty library | 1280×720 | Dark | `home-empty.png` |
| Cubase 15 partial session | 1440×960 | Dark | `partial-demo15.png` |
| Published self-contained app / populated home | 1440×960 | Dark | `published-home.png` |

Artifacts live in ignored `artifacts/demos/`. Capture excludes native window
chrome, so the rendered pixel dimensions differ from requested outer dimensions.
The hidden Win32 host renders actual app content; it does not exercise live
keyboard, assistive technology, physical DPI changes, audio output or dialogs.

Review found and corrected indented supporting copy, oversized navigation rows,
half-width narrow page headers, unthemed toolkit grid header/selection states,
empty exported-MIDI defaults despite native notes, a blank audio project selector,
a duplicate media pane above the Audio tabs, and missing direct no-results
recovery. Native sessions now open their populated note/audio-path tab when the
corresponding structured-export list is empty. Audio details stack at narrow
widths and overview metrics reflow instead of squeezing three columns.

The design uses the official [Uno Material setup](https://platform.uno/docs/articles/external/uno.themes/doc/material-getting-started.html)
and [semantic color roles](https://platform.uno/docs/articles/external/uno.themes/doc/material-colors.html).
The toolkit grid's separate header/selection resource states were checked against
its [upstream theme definitions](https://github.com/CommunityToolkit/WindowsCommunityToolkit/blob/rel/7.1.0/Microsoft.Toolkit.Uwp.UI.Controls.DataGrid/DataGrid/DataGrid.xaml).
Remaining live acceptance stays unchecked in this document and `PLAN.md`.

The subsequent [producer workspace review](PRODUCER_WORKSPACE_REVIEW.md) records
the compact neutral-palette redesign, automation ownership views, I/O-first
layout, all-page render sweep and programmatic interaction checks. Earlier
lavender/mint screenshots above are retained as historical comparison evidence.
