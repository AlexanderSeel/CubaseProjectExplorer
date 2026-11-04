# Cubase 15 C-export follow-up: time-signature revisions

Investigated 2026-11-04. This extends the fixed-prefix findings in the
[serializer analysis](CUBASE15_SERIALIZER_ANALYSIS.md). It adds anonymous raw
observations, without assigning numerator, denominator, bar, time or effective
meter-map semantics.

## Reproduction and evidence

The read-only input `D:\ReverseEng\Cubase15.exe.c` is 590,827,789 bytes,
SHA-256 `35b8643f6f5211cc4e5bf0ccd5efd5f7541f9cc289b09d28a4f9dd1d0a444cf8`.
The assembly export identifies executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
These hashes identify the investigated snapshots; the two exports are views of
the same executable, not independent native-project validation.

```powershell
python scripts/inspect_cubase15_c_export.py D:\ReverseEng\Cubase15.exe.c `
  --function time-signature-registration --function time-signature-factory `
  --function time-signature-reader --function time-signature-writer `
  --function uint64-reader --function uint16-reader --function uint16-writer `
  --output artifacts/signature-followup-2026-11-04/callbacks
python scripts/inspect_cubase15_ida_export.py Cubase15.exe.asm `
  --function time-signature-registration --function time-signature-factory `
  --function time-signature-reader --function time-signature-writer `
  --function uint64-reader --function uint16-reader --function uint16-writer `
  --output artifacts/signature-followup-2026-11-04/asm
```

Full excerpts and private reports remain in ignored artifacts. Neither source
projects nor Cubase code are executed or modified.

| Address | Observed behavior |
| --- | --- |
| `0x1417DCEB0` | Registers `MTimeSignatureEvent`, factory `0x1431DF570`, revision 2. |
| `0x1431D3B70` | Reads one 64-bit word, then six 16-bit words, before querying the named class revision. |
| Same callback, archive vtable slot `+0x30` | Calls the attribute reader only when the named revision is greater than 1. No revision branch changes the preceding scalar reads. |
| `0x1431D3C40` | Writes the same fixed scalar sequence, followed by the current attribute writer. This writer does not validate historical projects. |

The assembly confirms the seven primitive calls and the `revision > 1` gate.
The shared fixed prefix therefore has anonymous offsets `0, 8, 10, 12, 14, 16,
18` and occupies 20 bytes. It is retained for explicit class revisions 0, 1 and
2. Null, future or unknown revisions remain opaque. A revision number is not a
Cubase release number.

## Reader behavior and limits

Every retained field has its absolute file offset, storage type, invariant
unsigned rendering, exact original hex bytes, candidate evidence and serializer
revision. The whole fixed prefix must fit in its bounded record and archive,
including when the retention budget is zero. Attribute suffix bytes remain
opaque, and fixed-prefix success does not assert a complete object load.

The existing per-record and aggregate raw-field budgets still apply. The
signature decoder now creates/formats only the fields it can retain. With zero
remaining capacity, a valid prefix reports omitted fields without constructing
the seven field objects, their numeric/hex strings or their source string.
Partial capacity preserves complete leading fields and reports retention as
partial. This reduces unnecessary formatting at the budget boundary; no
end-to-end performance claim is made.

Native `TimeSignatureNumerator` and `TimeSignatureDenominator` stay unset.
Observed field values do not establish musical meaning or apply attribute
overrides.

## Validation

The focused signature/decoder suite passed 37 Release tests. New cases cover
revisions 0–2, raw high-bit words, offsets, every shorter prefix, budgets 0–8,
unknown/future revisions and RIFF/RIF2 inventory-to-snapshot propagation.
Synthetic source bytes remain unchanged. The full suite passed **763 Release
tests, with zero failures or skips**, including both controlled private Cubase
15 fixtures. CLI and Uno Desktop Release production builds passed with zero
warnings/errors. The C-export SHA-256 remained unchanged after analysis.

Genuine revision-0/1 projects and independently compared Cubase/export pairs
remain required before asserting historical coverage or normalized meter maps.

## Marker lead retained for later work

The bounded marker callback `0x1426FC400` reads a string via `0x14458ACF0`, a
32-bit scalar via `0x1439DF440`, then delegates to `0x1431ABD30` before checking
archive error state. Writer `0x1426FC470` mirrors that sequence. String framing,
base-event boundaries and independent field meanings still need tracing; no
marker-name, position or ID decoder is added from this lead.
