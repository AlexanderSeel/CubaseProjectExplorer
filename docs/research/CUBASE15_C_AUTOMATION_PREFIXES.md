# Cubase 15 automation-track prefix follow-up

Investigated 2026-11-04. Continues the automation target work in
[PLAN.md](../../PLAN.md) and [the earlier automation findings](AUTOMATION_IO_2026-11-02.md).
The implemented subset retains anonymous raw fields, with no parameter, mode,
target, owner, curve, musical unit or effective automation interpretation.

## Evidence and reproduction

The local C export is 590,827,789 bytes, SHA-256
`35b8643f6f5211cc4e5bf0ccd5efd5f7541f9cc289b09d28a4f9dd1d0a444cf8`.
The existing extractor checks this exact text hash before using known labels.
The IDA export identifies executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
Both are static views, not independent validation of saved musical state.
Inputs were opened read-only; Cubase and plug-ins were not executed.

```powershell
python scripts/inspect_cubase15_c_export.py D:\ReverseEng\Cubase15.exe.c `
  --function automation-track-registration --function automation-track-reader `
  --function automation-track-writer --function automation-lane-reader `
  --function automation-lane-writer --function automation-base-reader `
  --function automation-base-writer --function automation-parameter-cleanup `
  --function event-base-reader --function uint32-reader --function float32-reader `
  --function uint16-reader --output artifacts/automation-followup-2026-11-04/callbacks

python scripts/inspect_cubase15_ida_export.py Cubase15.exe.asm `
  --function automation-track-registration --function automation-track-reader `
  --function automation-track-writer --function automation-base-reader `
  --function automation-base-writer --function automation-parameter-cleanup `
  --function event-base-reader --function uint32-reader --function float32-reader `
  --function uint16-reader --output artifacts/automation-followup-2026-11-04/asm
```

Full proprietary excerpts and their offset/hash manifests remain in ignored
artifacts. Only address labels, bounded findings and synthetic tests enter source.

| Callback | Direct observation |
| --- | --- |
| Registration `0x141623B20` | Registers `MAutomationTrack` at revision 2, factory `0x14225E800`. |
| Reader `0x142251240` | Active revision below 2 reads a signed 32-bit count and a repeated u32/f32 pair; each row also reads u16 when active revision is positive. All revisions then read two u16 values and delegate to `0x1431EBA20`. |
| Writer `0x1422514D0` | Writes the current two u16 values, then delegates to `0x1431EBCF0`; it does not write or independently validate legacy rows. |
| Lane reader `0x14224EEE0` | Calls base `0x1431F1380`, then conditionally reads its own u32/f32/u16 prefix. Its offsets cannot be copied from the track callback. |
| Base reader `0x1431F1380` | Calls `MEvent` reader `0x1431ABD30`, obtains an `MTrack` object, then applies named `MTrackEvent` revision gates, scalars and an `MAutomationNode` object. |
| `0x1422613F0` | Iterates runtime `MParamEvent` objects, checks finite/duplicate values and may remove entries. This excerpt establishes no disk count, point stride or target-ID codec. |

The IDA track-reader excerpt corroborates the revision gates, primitive read
order and final delegation. The first two legacy row values are passed to runtime
construction/conversion paths, but that does not establish saved target identity
or musical units. The two exports are not an independent Cubase round trip.

## Implemented bounded subset

| Explicit class revision | Fixed prefix |
| --- | --- |
| 0 | i32 count, `count` rows of u32/f32 (8 bytes each), two u16 words |
| 1 | i32 count, `count` rows of u32/f32/u16 (10 bytes each), two u16 words |
| 2 | Two u16 words (4 bytes) |

The original bits, invariant numeric renderings, absolute offsets, byte widths,
class revision and callback provenance are retained as `CprBinaryFieldCandidate`
entries. Floating-point NaN payloads and negative zero remain available in hex.
No Boolean names or enum meanings are assigned to the u16 words.

Legacy counts must be nonnegative, and the whole table plus trailing words must
fit the bounded class body before any field is retained. Negative counts remain
unsupported here even though the native signed loop would skip them. Empty
tables are supported. Unknown/future revisions and incomplete prefixes yield no
raw fields. This does not classify the undecoded inherited body as valid.

The existing 256-field per-record and 100,000-field aggregate project limits
apply. Only leading retained fields are allocated/formatted; omitted fields
report retention as partial. A zero budget still validates the whole prefix.
Arithmetic checks use the bounded span before multiplication or allocation.
The inherited `MTrack` body remains opaque, and prefix success is not a complete
object load. No extra automation lanes or owner IDs are created.

## Validation

- Focused automation-prefix suite: **25 Release tests passed**. Cases cover all
  three revisions, high-bit words, exact offsets/hex, NaN/negative zero, zero
  counts, all shorter prefixes, unsupported counts/revisions, retained leading
  fields, per-record limits and RIFF/RIF2 inventory-to-snapshot propagation.
- Full Release regression suite: **788 passed, zero failed or skipped**, including
  both hash-pinned controlled Cubase 15 fixtures.
- A final shared-workspace run after concurrent marker-reader changes passed
  **818 Release tests, zero failed or skipped**. Those separately arriving
  changes were preserved and are outside the automation-prefix scope here.
- Windows x64 self-contained CLI and Uno Desktop Release production publishes
  passed without warnings/errors, and both were repeated for that final shared
  source under `shared-cli-win-x64` and `shared-app-win-x64` artifact directories.
- Before/after published-CLI comparison covered eight files from the existing
  33-file private routing sample, including the two controlled fixtures. All
  eight were readable and all source SHA-256 values were unchanged after both
  inspections. **70 revision-2 automation-track records** gained **140 raw
  fields**. Every field's hex matched its exact source bytes. After excluding
  only those new automation raw-field lists, the complete structural JSON was
  identical: prior note values/context, record inventories, routing tokens and
  limitations did not change. No legacy automation-track body was found in
  this bounded sample.
- The reproduction commands found all 12 requested complete C-export definitions
  and all ten requested complete IDA procedures. Full excerpts, private paths,
  per-file hashes, JSON comparisons and production outputs remain ignored.

Genuine revision-0/1 automation-track bodies and controlled Cubase/export pairs
remain required. Parameter target/mode decoding, independent lane ownership,
point/time domains and inherited objects remain open. This batch changes reader
evidence only; live Uno acceptance remains pending and builds are not visual
approval. Local reports and binaries stay under ignored
`artifacts/automation-followup-2026-11-04/`.
