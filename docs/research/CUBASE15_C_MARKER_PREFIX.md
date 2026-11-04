# Cubase 15 C-export follow-up: marker prefix and retained-field allocation

Investigated 2026-11-04. Continues the marker lead in the
[signature revision follow-up](CUBASE15_C_SIGNATURE_REVISIONS.md).
The implemented subset exposes anonymous raw observations, without deriving
marker identity, timeline position, track ownership or effective marker state.

## Evidence and reproduction

The local `D:\ReverseEng\Cubase15.exe.c` snapshot is 590,827,789 bytes,
SHA-256 `35b8643f6f5211cc4e5bf0ccd5efd5f7541f9cc289b09d28a4f9dd1d0a444cf8`.
The assembly export identifies executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
Both exports describe the same executable, not independent saved-state validation.
No Cubase code is executed or modified; complete excerpts stay in ignored artifacts.

```powershell
python scripts/inspect_cubase15_c_export.py D:\ReverseEng\Cubase15.exe.c `
  --function marker-registration --function marker-factory --function marker-constructor `
  --function marker-reader --function marker-writer --function marker-string-reader `
  --function marker-string-writer --function part-name-reader --function marker-string-value-writer `
  --function uint32-reader --function event-base-reader `
  --output artifacts/marker-reader-2026-11-04/callbacks
python scripts/inspect_cubase15_ida_export.py Cubase15.exe.asm `
  --function marker-registration --function marker-factory --function marker-constructor `
  --function marker-reader --function marker-writer --function marker-string-reader `
  --function marker-string-writer --function part-name-reader --function marker-string-value-writer `
  --function uint32-reader --function event-base-reader `
  --output artifacts/marker-reader-2026-11-04/asm
```

| Address | Observed behavior |
| --- | --- |
| `0x1416A6470` | Registers `MMarkerEvent`, factory `0x1426FD360`, revision 0. |
| `0x1426FD360` / `0x1426FBB90` | Factory invokes constructor, which assigns primary vtable `0x1460A7098`; reader/writer entries are at `+0x90`/`+0x98`. |
| `0x1426FC400` | Reads a string, a 32-bit scalar, then delegates to the inherited `MEvent` reader `0x1431ABD30`. |
| `0x1426FC470` | Writes that string/scalar pair before the inherited writer. |
| `0x14458ACF0` / `0x14458AD00` | Thin archive-to-stream wrappers around `0x14458AAC0` / `0x14458AC40`. |
| `0x14458AAC0` | Reads signed i32 byte length, limits positive lengths to 50,000, then reads that many bytes. Recognizes UTF-8 BOM bytes at the beginning or end; removes an existing terminating NUL before internal storage. Unmarked bytes keep their legacy encoding. |
| `0x1431ABD30` | Reads flags, three doubles, an `MDataNode` object and revision-gated attributes. Their context is deliberately outside this prefix reader. |

The assembly confirms primitive call order, signed length comparisons and BOM
branches. The writer uses zero for an empty string; nonempty legacy strings
include a terminator. The modern string-write branch is not needed to claim
general string-codec support: retained text follows only the recognized read subset.

## Implemented subset and boundaries

For explicit `MMarkerEvent` revision 0 only, the prefix is:

`i32 string-byte-count + string bytes + u32 anonymous scalar`

The complete prefix must fit the class body and enclosing archive before any
fields are retained, even when the caller budget is zero. Negative, oversized,
truncated, unknown-revision and future-revision inputs stay opaque. The native
string reader treats negative string lengths as empty; this reader deliberately
does not accept them. No guessed scalar boundary or byte resynchronization follows
an invalid length.

Fields use `body+offset` names, original absolute offsets, exact hex bytes,
invariant numeric values and candidate provenance. A zero-length string produces
the two scalar observations, without inventing payload bytes. The following scalar
is not labeled a marker ID.

Terminated printable ASCII is rendered as text. UTF-8 text requires a BOM at
either end, valid UTF-8 and no embedded controls. Invalid UTF-8, unterminated
strings, controls and unmarked high bytes remain opaque payload observations,
with their original bytes intact. Rendering is deliberately narrower than the
native codec; no current system codepage is guessed. Raw bytes include the BOM
and terminator even when the displayed text omits them.

Markdown reports escape HTML and Markdown metacharacters in table cells, so
retained text renders literally. JSON/model values and original raw bytes stay
unchanged; a marker string cannot introduce an HTML element or Markdown link
through the report's value cell.

String observation retention is capped at **4096 wire bytes** per marker. Longer
valid strings up to 50,000 bytes are skipped without copying/decoding, while their
declared length and following scalar remain available. Their provenance records
the omission; the decoder and archive inventory are marked retention-partial,
with an explicit string limit in the archive reason. The existing 256-field per
record and 100,000-field project budgets remain in force. No inherited event body,
marker position, effective name/ID or normalized marker collection is reconstructed.

## Tempo allocation optimization

The exact 24-byte `MTempoEvent` revision-0 layout is unchanged. Its decoder now
allocates/formats only leading fields that fit the remaining retention budget.
Previously it constructed all four field objects and their strings before
discarding omitted fields. At a zero budget, a valid bounded record returns an
empty retention-partial result before creating those objects. Raw NaN payloads,
signed zero, infinities, offsets and retained-field ordering are unchanged.

A local Release probe compared the previous published assembly with the updated
decoder using the same zero-filled 24-byte record. Each budget was warmed for
2000 calls, then measured for 10,000 calls with
`GC.GetAllocatedBytesForCurrentThread`. Total measured bytes:

| Retained field budget | Before | After |
| --- | ---: | ---: |
| 0 | 6,320,128 | 128 |
| 1 | 7,360,000 | 1,920,000 |
| 4 | 6,320,000 | 6,320,000 |

Both zero-budget runs include the same 128-byte measurement/runtime overhead.
Constant field-name strings preserve the full-read allocation cost; constructing
those names per call would have added allocations to the common path. This
isolated probe establishes budget-dependent allocation behavior, not an
end-to-end application memory or speed improvement. Local harnesses remain in
ignored `artifacts/marker-reader-2026-11-04/`.

## Validation

The full suite passed **821 Release tests, with zero failures or skips**, including
both hash-pinned controlled Cubase 15 fixtures. Marker tests cover ASCII and BOM
forms, invalid UTF-8, legacy encoding, embedded controls, every shorter prefix,
invalid counts/revisions, exact offsets/hex, 4096/4097/50,000-byte boundaries,
field budgets, literal Markdown rendering and RIFF/RIF2 inventory propagation.
The tempo budget regression checks retained bits, ordering and malformed bounds.
Windows x64 self-contained CLI and Uno Desktop Release production publishes
passed without warnings or errors. An initial CLI publish without restore lacked
the runtime-specific assets; restoring the existing dependencies resolved it.
The published CLI read both controlled projects (zero and 16 note candidates)
and a synthetic marker case (three raw fields). JSON retained its original
text and Markdown rendered it literally; all three source byte arrays were
unchanged after reading.

A read-only comparison covered all **33 files** in the existing bounded private
routing sample. Every original SHA-256 remained unchanged after inspection with
the baseline and updated CLI. After excluding only newly supported marker and
automation raw-field lists, complete structural JSON was identical; existing
notes, routing tokens, field values and limits were preserved. **No compact
`MMarkerEvent` records were present**, so this sample checks regressions rather
than genuine marker decoding. No source project or complete private report is
committed. The C-export hash remained unchanged after analysis.

Genuine one-change Cubase marker saves and paired visible-state/export comparisons
remain necessary. Serializer revision 0 is not a Cubase release number. Inherited
event/object/attribute decoding, marker kinds, IDs, positions, timebase and
ownership remain open. Existing live Uno acceptance stays pending; this batch
adds reader evidence without introducing a new view or control.
