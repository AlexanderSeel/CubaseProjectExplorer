# Legacy CPR reverse-engineering notes

## Scope and source

This note records research leads for Cubase SX2/SX3 CPR files. It is not a CPR specification and does not make SX2/SX3 parsing claims for the production reader.

Primary research lead: [omeriko9/Cubase-Project-File-Reverse-Engineering](https://github.com/omeriko9/Cubase-Project-File-Reverse-Engineering), especially its [README](https://github.com/omeriko9/Cubase-Project-File-Reverse-Engineering/blob/master/README.md) and [author's binary notes](https://github.com/omeriko9/Cubase-Project-File-Reverse-Engineering/blob/master/My%20thought%20process.txt). The author scopes the work to Cubase versions 2 and 3, calls it work in progress, and describes inspecting self-created project files. The repository has no LICENSE file in the inspected checkout; do not copy its implementation or fixture into this project without permission and a licensing review.

Modern-format lead: [daniel-lynch/cpr2rpp](https://github.com/daniel-lynch/cpr2rpp), including its [format notes](https://github.com/daniel-lynch/cpr2rpp/blob/master/docs/FORMAT.md). It is MIT-licensed. Its author reports deriving the format from Cubase 12.0.50 Win64 files and validating against renders, source audio, file frame counts, and eleven `.bak` generations. This is a promising empirical description, not a Steinberg specification; the README says it has only been tested on a small number of projects.

## Findings reported by the research project

The author reports partial display of VST Mixer data, and identification of some audio/MIDI track records and names. Their notes hypothesize that:

- `MTrackList` contains a sequence of track records.
- Track records include classes such as `MAudioTrackEvent` and `MMidiTrackEvent`; names can occur in nested `MListNode` records.
- Some repeated class names may be represented by compact four-byte references after their first appearance.
- Sections may be traversed using declared lengths, with nested records inside record bodies.
- Mixer decoding varies by Cubase version and includes fixed distances in the experimental implementation.

These are source-reported observations and hypotheses from a narrow legacy corpus. The notes explicitly leave MIDI part internals, effect ordering, and other track-body bytes unresolved. The author reports that editing an effect name did not restore the effect in Cubase. None of this validates modern CPR layouts or writes.

## Modern Cubase 12 format lead (separate from SX2/SX3)

The `cpr2rpp` notes describe two coexisting encodings in Cubase 12.0.50 Win64 files:

- A recursively nested, big-endian named-record grammar used for mixer channels, plug-in racks, EQ, sends, and preferences. The notes assign record types to integer, float, string, object, array, blob, and class-instance forms. They describe fader dB values, pan blobs, four EQ bands, and plug-in state observations.
- A compact audio-event encoding in the arrangement, with class references, event positions expressed at 480 PPQ, lengths/source offsets expressed in sample frames, pool clip references, fades, and tempo-track values.

The notes include crucial cautions: mixer channels can have no timeline events; displayed clip names may not identify the playing source; event fields may be attributed to the preceding record; units can be consistently but incorrectly scaled; `.bak` comparisons help distinguish volatile data; automation, MIDI items, routing/folder topology, markers, and many other semantics are outside the documented implementation. The project README lists only single-tempo support and warns that unsupported features and project variation remain.

This is the first reviewed source in this research set that documents a modern CPR encoding beyond candidate token extraction. It gives us a concrete Cubase 12 adapter target, but its claims must be independently reproduced in our reader with authorized files and expected Cubase UI/render/source-media values. The described values must not be projected onto other Cubase releases without version-specific comparisons.

## Relation to this reader

`CprStructureInspector` recognizes the `RIFF/NUND` signature and currently applies a big-endian 32-bit size field plus big-endian `ROOT/ARCH` lengths as a candidate parsing strategy. These assumptions make the synthetic fixtures readable but do not validate universal outer framing or chunk grammar. A read-only envelope probe now reports big- and little-endian interpretations of the offset-4 field and whether each `+12` estimate matches the file length; this is diagnostic evidence only and does not select a parser or validate either interpretation. For legacy records, the inspector checks an `MListNode` body for the version-specific audio/MIDI marker noted by the SX2/SX3 research and the following bounded length-prefixed string. This is exposed only as a candidate name with byte-pattern provenance; it does not establish that every project uses that encoding or validate track order/ownership. Synthetic tests cover the pattern, not a genuine SX project. It also searches bounded `ARCH` payloads for the documented Cubase 12 channel-name anchor and decodes the associated named-record grammar into an explicitly candidate-only field inventory. The inventory is exposed in CPR structure Markdown diagnostics, not as normalized tracks/channels. `CprEvidenceReader` still does not claim native CPR plug-in instances, ownership, routing, tempo, or mixer settings. The compact arrangement event encoding and semantic channel mapping are not decoded.

The SX2/SX3 repository suggests a focused legacy adapter: inventory and compare `MTrackList`, `MAudioTrackEvent`, `MMidiTrackEvent`, and `MListNode` structure for *identified SX2/SX3 saves*, while preserving unknown bytes and making no claims about newer generations. Its fixed-offset track and mixer routines are not sufficient evidence to add those fields to the production model.

For Cubase 12, the `cpr2rpp` notes suggest a different adapter for recursive named records and compact timeline events. Keep the parsers version-scoped and separate; a shared outer container reader does not imply shared inner record semantics. Read the MIT source as research or reuse only with attribution and license notice, and preserve the format author's empirical caveats.

## Verification needed before promoting any field

1. Obtain permission-cleared, non-sensitive SX2/SX3 fixtures and record the exact Cubase build and platform. Do not use or redistribute the reference repository's checked-in CPR fixture.
2. Create minimal projects and save controlled variants: one audio track, one MIDI track, renamed tracks, reordered tracks, folder nesting, one plug-in, and a changed mixer value. Keep paired before/after files and hashes.
3. Compare byte ranges and archive/class inventories; independently verify length boundaries, compact class references, string encoding, and nested-name ownership across more than one fixture per change.
4. Compare each decoded value with the Cubase UI. Preserve per-field source offsets and candidate status until the mapping is repeatable.
5. Keep unsupported versions and unknown classes opaque. Do not write CPR files; writer support requires a separate validation gate and Cubase round-trip checks.

## Current status

No genuine SX2/SX3 or Cubase 12 comparison fixture is available in this repository. Bounded candidate extraction is implemented for the SX2/SX3 `MListNode` marker/name hypothesis and Cubase 12 named-record grammar, with synthetic parser/bounds fixtures only; no native CPR semantic decoder is enabled. For SX2/SX3, the next useful input is a permission-cleared legacy sample or controlled project set; for Cubase 12, independently reproduce named-record field meanings and audio-event fields against Cubase UI, renders, and source-media frame counts before exposing them as decoded values.

## Additional research review: Cubase Project Formats Deep Dive v2

The user supplied a second CPR research synthesis (`Cubase_Project_Formats_Deep_Dive_v2.md`, 2026-10-30). It is treated as a hypothesis and source index, not as a specification. Its audit of modern public parser code identifies risks that apply directly to this reader: duplicate track names must remain distinct; name/byte proximity is not ownership; fixed-size search windows can miss large nested state; class/type labels do not prove semantic content; and byte order may vary by field codec rather than applying to an entire file. Its SX2/SX3 observations remain a separate legacy family and do not authorize extrapolating fixed offsets to other releases.

The field-level cautions are retained for future fixture work: preserve tempo and signature event series rather than inventing a default; do not label marker positions as ticks until timebase is established; pitch zero and velocity zero can be valid MIDI values; keep MIDI signatures and automation markers candidate-only; and treat the reported >2 GB limit as version-specific until reproduced. Unknown spans should remain opaque, file and record bounds must use 64-bit arithmetic, and correlations must remain distinct from decoded parent/owner edges.

Recommended verification remains controlled Cubase-generated paired saves (A/B and A→B→A, repeated), with Cubase UI plus DAWproject/track-archive/SMF exports used as independent oracles where applicable. This review changes research guardrails and the open validation plan only; it does not promote new CPR fields into normalized project data.

## CubaseTools parser review and selective use

Reviewed [CubaseTools](https://github.com/schwifty00/CubaseTools), its [format notes](https://github.com/schwifty00/CubaseTools/blob/master/docs/cpr_format.md), and its metadata, track, mixer, and plug-in extractors. The repository states Cubase 10–15 support and real-project validation, and is licensed under MIT (copyright 2026 Deep Art). The guide is still a reverse-engineered description rather than a Steinberg specification.

The project now uses only a small, defensive part of that field vocabulary: it scans bounded `ARCH` payloads for typed sample-rate, sample-size, `MTempoEvent/BPM`, and `TimeSignatureEvent` patterns, recording file offsets and status as candidates in structure reports and project warnings. It does not supply default tempo/signature values, parse generic embedded doubles, assign mixer records to the nearest track, deduplicate by displayed name, or interpret plug-in parameters. Candidate scans are capped at 128 MiB of aggregate `ARCH` payload and 128 records. The extraction code was independently written against the published field descriptions; no CubaseTools source code or project fixtures were copied. CubaseTools’ own MIT license remains an attribution source if code is reused in a future change.

This parser extension is diagnostic only: no tempo, signature, sample rate, bit depth, mixer, routing, or plug-in state is promoted into normalized project values. Synthetic tests exercise exact typed signatures and bounds. A read-only smoke sample spanning the local corpus modification-time range produced structure reports for 23 of 24 CPR/BAK files; candidate sample-rate and sample-size patterns appeared in 19, tempo-event patterns in 3, and time-signature patterns in 10. One sample did not pass structural parsing. This confirms that the patterns occur in the local corpus, not that the values are correct; genuine version-pinned comparisons with Cubase UI/exports are still required to establish repeatability, event order, field ownership, and version coverage.
