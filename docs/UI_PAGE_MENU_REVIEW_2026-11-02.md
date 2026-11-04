# Page and menu consistency review

Date: 2026-11-02. Follow-up to the [producer workspace review](PRODUCER_WORKSPACE_REVIEW.md).
Task: open a session, find the relevant track/plug-in/media/connection, inspect
it and return without losing context. Evidence details stay accessible through
progressive disclosure. Project files and normal user preferences remain unchanged.

## Audit and shared layout

Reviewed actual Skia screenshots of all 21 routed pages. The baseline was captured
from the previous published build, followed by dark/wide (1440 × 960 requested
window) and light/narrow (960 × 780) sweeps. Captures use isolated preferences and
scratch library data under ignored `artifacts/ui-consistency/`.

| Observed issue | Implemented correction |
| --- | --- |
| Short/empty tables reserve 300–320px or more | Table height follows retained rows and headers, bounded by the configured maximum; large tables remain virtualized |
| Group/view strip uses an extra rounded container and mixed pill sizes | Flat shared toolbar; matching outlined/selected button padding, heights and radius; single-page card pagers hidden |
| Compact menu has large icon gaps | Reduced the Material compact-item margin while preserving the expanded 44px presenter/container heights; all primary destinations stay reachable |
| Compact filters clip placeholder and entered text | Preserve the 40px Material inner presenters and remove outer vertical padding in the filter style; empty/filled fields rechecked in both themes |
| Long page actions squeeze titles | Shared header places actions on a bounded second row when width is insufficient; action scrolling remains horizontal |
| Home repeats a large marketing heading | Compact neutral session-entry panel and full-width recent-project rows |
| Calendar leaves gaps between short buttons | Month tiles stretch across equal columns; selection uses a blue border on the common surface |
| Calendar/chart labels are too pale in light mode | Explicit theme-aware muted/accent brushes; removed fixed light-gray chart text |
| Settings reparents one theme picker into two locations | One labelled theme control inside Appearance |
| Narrow audio tables squeeze four columns beside preview | Shared master/detail layout stacks below 960px content width and preserves the configured column proportions when expanded |
| Technical catalog URL and preflight warning dump dominate the page | Catalog source settings and detailed reader evidence use expandable sections |
| Empty Search says no match before a query exists | Initial guidance asks for a project, plug-in, track or audio search |
| Metrics use different arrangements across pages | Reports and Database use the same responsive metric component as the session overview |

Material implementation follows the [official navigation styling keys](https://platform.uno/docs/articles/external/uno.themes/doc/styles/NavigationView.html)
and the installed theme's resource availability. The filter padding fix was checked
against the [pinned Material TextBox template](https://github.com/unoplatform/Uno.Themes/blob/8f0eb6fc963dc149015c85f28a764b1178d94bc3/src/library/Uno.Material/Styles/Controls/v2/TextBox.xaml),
whose inner presenters have fixed heights. Shared content uses neutral
slate/white surfaces, blue actions, 12px section insets, compact metrics and one
table/card toolbar pattern. Parser evidence is not promoted by visual styling.

```text
Compact session context and back/forward
Menu | Page title / short task description           Actions / filters
     | Actions on next row when necessary
     | Optional compact metrics / tabs
     | Shared results toolbar
     | Main list / plot / selected details
Status and background activity
```

## Page coverage

Each route below was captured in both reviewed sizes/themes. These are viewport
and transition checks, not a claim that every command, dialog or data state was tested.

| Page | Review focus |
| --- | --- |
| Home | Compact entry actions, metrics, recent sessions, navigation |
| Projects | Filter row, cards/details, export menu entry, project activation |
| Session overview | Responsive metrics, session context and inspection tiles |
| Tracks | Arrangement order, table, attached automation and selected-track details |
| Plug-ins | Compact grouped references, filters and unknown vendor/format evidence |
| Routing | Channel selector, explicit unresolved option, readable nodes and controls |
| Audio & MIDI I/O | Short assignment table, named/unresolved distinction, all three tabs |
| Audio | Native paths, project media, duplicate/activity tabs and narrow master/detail |
| MIDI | Native groups, part picker, plot/table, exported-MIDI and activity tabs |
| Studio tools | Responsive tile grid and routes to all secondary tools |
| Search | Empty guidance, scope/filter controls and compact empty table |
| Calendar | Equal-width months, light contrast, date provenance and project rows |
| Statistics | Track distributions, absent values and library-reference state |
| Compare | Project A/B controls and unset comparison state |
| Local plug-in inventory | Saved-declaration cards, filters and loadability limitation |
| Plug-in catalog | Empty catalog, source settings and selected-product pane |
| Dependency preflight | Evidence-safe empty dependencies and expandable source details |
| Reports | Common metrics and the five report tabs |
| Database | Common metrics, backup/settings entry points and lower record/query area |
| Help | App guidance, file-format terms and research tabs |
| Settings | File-type switches, one Appearance picker, About and read-only notice |

## Verification method and limits

- `before-manifest.json`: 21 baseline route captures.
- `after-manifest.json`: 42 captures, all 21 pages in each reviewed mode.
- `interaction-manifest.json`: all **43** handler transitions/rechecks passed,
  including 12 sidebar routes, 9 Studio tools routes, 13 tab transitions, selected
  track details, Projects Details view, activation of Jazz Practice, lower Database
  content and five corrected light-theme states.
- Follow-up captures in `interaction/` use the normal navigation selection,
  Studio tools button invocation, tab invocation and project-activation handlers.
  Menu-route actions assert the resulting page ID; project activation also checks
  that the open session name changed. These are read-only actions in isolated runs.
- Additional checks cover selected-track details, Projects Details view, lower
  Database content and corrected light Calendar/Catalog/Preflight/Reports/Search
  states. Three final text/stacking rechecks passed in `final-recheck/`; compact
  all four empty/filled filter checks passed in `filter-verified/`. Earlier filter captures exposed
  the clipping and remain as regression evidence. The corrected filter build is
  published separately in `release/verified-app-win-x64`; a preceding publish to
  an active preview output failed with MSB3021/MSB3027 file locks and was superseded.
  A preview during that publish timed out; that attempt is excluded from the passed
  sweeps. The fresh publish and subsequent four captures completed successfully.
- Release tests and Windows publishes are required alongside rendered review.
  The suite is **639 passed, 2 explicit private-fixture skips** after removing five
  additional retired-presentation tests; details are in the
  [reader research](research/SCORE_EVENT_BOUNDARIES_2026-11-02.md).

The captures verify the rendered states that were inspected. They do not verify
physical mouse/keyboard operation, screen readers, actual high contrast or
100–200% Windows DPI, every dialog/menu popup, audio playback/seeking, scan
cancellation or large-library performance. These remain open in [PLAN.md](../PLAN.md).

Known remaining consistency issue: older compact library summaries can retain
pre-normalization track-record counts until re-inspected/rescanned. The open
reader/schema-version invalidation work must address stale persisted summaries;
the selected project is read from its source, and no counts are silently guessed.
