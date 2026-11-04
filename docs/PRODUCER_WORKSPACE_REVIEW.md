# Producer workspace review

Task: open a session, identify the track/instrument or connection of interest,
inspect its saved setup, and return without losing context. Raw reader evidence
must remain reachable without dominating the music-production workflow.

The follow-up [page/menu consistency review](UI_PAGE_MENU_REVIEW_2026-11-02.md)
covers all 21 routes in dark/wide and light/narrow modes, shared table/menu spacing,
contrast fixes, real handler transitions and remaining acceptance limits.

Reference: [CPR Inspector product page](https://courses.domsigalas.com/cpr-inspector),
reviewed 2026-11-02, including its track, routing and audio-I/O screenshots. Useful
principles are a wide content area, compact session context, restrained dark
surfaces/blue selection, readable track rows and focused input/output groups.
This independent app retains its own code and evidence boundaries. Reference
images are local/ignored; no third-party assets are incorporated into the app.

## Observed issues and decisions

- Existing 76px brand + 42px breadcrumb + 44px status bar and 248px fixed sidebar
  reserve too much space. Compact the shell and collapse navigation at narrow sizes.
- Purple/green surfaces compete with the session. Replace them with neutral
  graphite/slate or white surfaces and one blue selection/action accent.
- Overview repeats large metrics, reader notes and activity headings before any
  useful session content. Use one compact session summary and focused next views.
- Tracks mixes automation lanes into the standalone grid and sorts unnamed
  records first. Preserve arrangement order; attach lanes to owning tracks and
  disclose unresolved ownership without inventing a parent.
- I/O opens a graph at an unnamed signature record. Lead with input/output
  assignments, keep signal-flow exploration separate and source fields in details.
- Plug-in cards reserve large empty artwork spaces. Make the compact details list
  the initial view, while keeping cards as an optional browser.

```text
Compact app / current session                       Open / library
Navigation | Session title and focused actions
           | Compact summary where useful
           | Search / type / view controls
           | Main track, plug-in or mapping list
           | Selected track: automation, inserts, media and evidence
Activity remains in a thin persistent status bar
```

## Regression method

Capture real Skia visual trees using isolated preferences/database and published
Desktop builds. Before/after images and per-page failures are stored under
ignored `artifacts/studio-redesign/`. Review populated pages at wide/narrow sizes
and in both themes; exercise filters, view switches, navigation and ownership
states. A screenshot render alone does not establish keyboard, assistive
technology, live DPI or audio acceptance. Those remain explicit in `PLAN.md`.

## Results — 2026-11-02

- Captured all 20 original pages before the changes; the final render sweep
  captured all 21 pages, including the new Studio tools hub, without capture
  errors. Manifests and PNGs are in `before/`, `final/` and their matching
  `*-manifest.json` files under the ignored artifact directory.
- Captured eight additional light-theme pages at a 960 × 780 window size.
  Reviewed the expanded and collapsed navigation, overview, track detail,
  plug-in list, I/O assignments and routing states. Corrected the clipped compact
  navigation rail, excessive shell height and graph-node text contrast. Explicit
  44px Material presenter/container resources keep the last session/tool items
  readable without clipping icons. See the [official lightweight styling keys](https://platform.uno/docs/articles/external/uno.themes/doc/styles/NavigationView.html);
  key availability was also checked against the installed Material 7.1.1 assembly.
- Exercised track selection, the I/O Signal flow tab and Projects Details view
  through the normal selection/invocation handlers in isolated preview sessions.
  These checks verify those rendered transitions, not physical keyboard or
  screen-reader navigation.
- Automation appears as a lane count and detail section of its owner track.
  Unresolved routing and I/O assignments remain accessible through explicit
  controls; they do not dominate the initial view. Raw source details remain
  available. Native owner relationships remain candidates.
- Release regression: 631 tests pass. CLI and Desktop self-contained Windows
  publishes pass. Final packaged-build captures are in `packaged/`.

Remaining acceptance includes actual high-contrast and 100–200% DPI sessions,
physical keyboard/UIA operation, dialogs and history, audio playback/seeking,
slow/missing/corrupt project activation, and large-library behavior. These remain
open in `PLAN.md`; the render sweep is not a claim that every interaction works.
