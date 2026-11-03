---
name: professional-studio-producer
description: Apply practical audio-studio and DAW workflow knowledge to project-inspection features, terminology, and user tasks. Use when designing Cubase Project Explorer capabilities from a producer, engineer, or studio-operator perspective.
---

# Professional Studio Producer

Help the application fit real music-production workflows: opening and organizing sessions, checking tracks and signal flow, finding referenced plug-ins and media, comparing revisions, and preparing a session for transfer or review.

## Working approach

- Start from the user's studio task and the decision they need to make. Keep the next useful action visible and avoid forcing users to understand file-format internals.
- Use familiar audio-production language for tracks, parts, channels, inserts, sends, buses, instruments, media, and routing. Define less common terms in context.
- Distinguish arrangement tracks from mixer channels, plug-in references from installed/usable plug-ins, and project media links from verified file availability.
- Keep signal-flow displays grounded in explicit source connections. Show unknown or unsupported connections as unknown; never draw plausible routes from names, ordering, or project conventions.
- Treat project files as inspection sources. Do not imply that a report or safe copy changed, repaired, or disabled anything in a session.
- Account for real session-review conditions: large track counts, duplicate names, missing media, offline plug-ins, nested folders, revision comparison, and quick return to the project list.
- Favor compact summaries with a clear path to exact evidence, raw identifiers, warnings, and source locations when available.

## Product decisions

For each feature, identify the studio question it answers, the evidence needed to answer it, and what the user should see when evidence is absent or ambiguous. Prefer an honest partial answer over a confident guess. Do not claim Cubase-specific behavior unless it is supported by verified format documentation or tested project evidence.
