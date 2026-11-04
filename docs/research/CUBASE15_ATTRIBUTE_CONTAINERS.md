# Bounded primitive attribute-container traversal

Date: 2026-11-04. Continues the P0 attribute work in [PLAN.md](../../PLAN.md).
This extends wire-boundary traversal only. It does not load a Cubase container,
identify a plug-in, apply an attribute or establish final note/score state.

## Static evidence

The local 590,827,789-byte C snapshot has SHA-256
`35b8643f6f5211cc4e5bf0ccd5efd5f7541f9cc289b09d28a4f9dd1d0a444cf8`.
The extractor checks this hash. The 1,079,140,046-byte IDA export identifies
executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
These are static views of the same executable, not independent musical-state
validation. Inputs were opened read-only; Cubase/plug-ins were not executed.

| Callback | Observed boundary |
| --- | --- |
| Attribute value `0x1445749D0` | After flags, object bit 2 takes priority, then container bit `0x0100`, then string bit 8 and scalar bits 1/4. The container branch calls `0x14456D470`. |
| Container `0x14456D470` | Reads a string through `0x14458ACF0`/`0x14458AAC0`, converts up to 32 hexadecimal digits into a type token, obtains a runtime implementation and invokes storage reading. The inspector never performs the lookup/instantiation. |
| Storage bridge `0x1445D71F0` | Passes the archive stream into the property reader `0x1445DCE10`. No independent outer payload-length field is established here. |
| Property reader `0x1445DCE10` | Reads a signed i32 count; each property has a length-prefixed name and u16 type. Exact type 1 reads u64, type 4 reads f64, and type 8 reads a length-prefixed string. More complex object/array/reference/recursive branches are separate. The primitive loop has no post-list wire suffix. |
| String helper `0x14458AAC0` | Signed i32 byte count; positive strings are bounded by 50,000 native bytes. Recognizes UTF-8 BOMs at either end. Negative-length native behavior is not accepted by the inspector. |

The complete C and IDA excerpts corroborate primitive widths, branch priority,
loop count and ending. Unknown native property tags can have runtime fallback
behavior; they are never treated as zero-width properties by this reader.
No guessed writer address is used as evidence.

```powershell
python scripts/inspect_cubase15_c_export.py D:\ReverseEng\Cubase15.exe.c `
  --function event-attribute-value-reader --function attribute-container-reader `
  --function attribute-container-storage-reader --function attribute-container-storage-body `
  --function marker-string-reader `
  --output artifacts/attribute-containers-2026-11-04/callbacks

python scripts/inspect_cubase15_ida_export.py Cubase15.exe.asm `
  --function event-attribute-value-reader --function attribute-container-reader `
  --function attribute-container-storage-reader --function attribute-container-storage-body `
  --function marker-string-reader `
  --output artifacts/attribute-containers-2026-11-04/asm
```

Only address mappings and findings enter the repository. Full proprietary code,
excerpt hashes/offsets and private project reports remain in ignored artifacts.

## Supported subset and limits

The shared typed/count-stream/score attribute reader accepts only exact flags
`0x0100` and printable four-byte attribute IDs for this extension. Other flag
combinations remain unsupported even if the native dispatcher would select a
container branch.

- The wire type token must have exactly 32 ASCII hexadecimal digits, optionally
  a trailing NUL and a UTF-8 BOM at one end. No identity or runtime compatibility
  is inferred from that token.
- The property count must be 0–256. Only exact primitive types 1, 4 and 8 are
  traversed; property names and string values remain uninterpreted raw spans.
- A wire string must have a nonnegative byte count no greater than 50,000 and
  fit the available span. An entire container may consume at most 64 KiB,
  including the type-token and property framing; the boundary is inclusive.
- Success advances a local cursor to the following attribute/suffix. Failure
  leaves the caller's cursor unchanged and stops interpretation with the outer
  attribute ID, flags, source offset and a reason. No byte resynchronization,
  payload allocation, recursion, plug-in loading or container instantiation occurs.
- Existing class revisions, event counts, part/list endings, note/byte budgets
  and cancellation behavior remain required. Object, array and recursive
  property types, unsupported counts/lengths and other flag forms stay partial.

Runtime factory acceptance of a type is not established. This is a narrow opaque
property-table walk, not successful loading of the native object or its values.

## Arrangement boundary hardening

Adversarial fixtures exposed a separate problem: the raw arrangement traversal
could find a framed `MMidiNote` inside a container string before counted-stream
interpretation. Such a byte inventory match is not an escaped event.

For a recognized counted part, the event reader now records exact typed-note
object-escape body offsets. Arrangement processing keeps only previously read
typed-note observations at those explicit offsets, then combines the counted
notes under the existing total note cap. Note-shaped bytes in opaque values
cannot survive through the separate typed path. Escaped references do not add
duplicate notes. The raw class inventory remains available as byte evidence.
Parts whose counted preamble cannot be established keep the existing separately
labeled bounded fallback behavior.

## Validation

- Focused container suite: **16 Release tests passed**, with multiple adversarial
  wire cases per test. RIFF/RIF2 cases cover counted/typed/score paths, exact
  following-note offsets, raw tail bits, opaque embedded typed-note records,
  unsupported properties/counts/lengths, token/BOM forms, 256 properties and the
  inclusive 64 KiB limit. Source bytes remain unchanged.
- Full Release suite: **837 passed, zero failed or skipped**, including both
  hash-pinned controlled Cubase 15 fixtures and existing escaped-note regressions.
- CLI/Uno Windows production publishes and genuine-file regression comparison:
  pending.

Genuine controlled container fixtures and independent Cubase/export comparisons
remain open. Synthetic wire tests validate parser behavior, not Cubase-visible
values, ownership, time units or universal version coverage. No UI interaction
changed; live Uno visual/accessibility/DPI acceptance remains pending.
