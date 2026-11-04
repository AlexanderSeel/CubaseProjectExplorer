# Cubase 15 C-export follow-up: counted tempo tables

Investigated 2026-11-04. This extends the
[serializer analysis](CUBASE15_SERIALIZER_ANALYSIS.md) with anonymous raw-field
coverage. It does not establish BPM, musical timebase, effective tempo maps or
complete historical project loading.

## Evidence and reproduction

The local C snapshot is 590,827,789 bytes, SHA-256
`35b8643f6f5211cc4e5bf0ccd5efd5f7541f9cc289b09d28a4f9dd1d0a444cf8`.
The corresponding executable was previously verified as SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
Addresses apply to that build only. Text hashes identify the exports without
independently authenticating their derivation from the executable.

```powershell
python scripts/inspect_cubase15_c_export.py D:\ReverseEng\Cubase15.exe.c `
  --function tempo-track-registration --function tempo-track-factory `
  --function tempo-track-reader --function tempo-track-writer `
  --function boolean-u16-reader --function boolean-u16-writer `
  --output artifacts/tempo-tables-2026-11-04/callbacks
python scripts/inspect_cubase15_ida_export.py Cubase15.exe.asm `
  --function boolean-u16-reader --function boolean-u16-writer `
  --output artifacts/tempo-tables-2026-11-04/asm
```

Full code excerpts, executable bytes, private paths and source reports stay in
ignored local artifacts. No Cubase code is executed or modified.

| Routine | Observation |
| --- | --- |
| `0x1417DE230` | Registers `MTempoTrackEvent`, factory `0x1431E8420`, revision 3. |
| `0x1431E2E00` | Reads a signed 32-bit count, rejects values below 1, then reads `f32`, `f64`, `f64`, `u16` for each entry. |
| Same callback, active revision at archive vtable `+0x28` | Revisions above 0 additionally read an `f32` and a serialized Boolean after the table. |
| Same callback, named revision at `+0x30` | Revisions above 1 read a shared attribute list; above 2 also read per-event attribute lists. These lists remain opaque here. |
| `0x1431E30F0` | Writer mirrors the count and 22-byte rows, then writes the additional `f32`, serialized Boolean and attribute lists. |
| `0x1439E0260` / `0x1439E0210` | Boolean reader/writer each transfer **two** stream bytes, even though the in-memory property occupies one byte. |

The Boolean width is independently visible in the IDA export: both helpers pass
`2` as the transfer length and compare the returned length with `2`. The reader
swaps the two wire bytes when required, then reduces the resulting word to an
in-memory Boolean. The inspector preserves the original word as `u16-be`.

This corrects an existing observation: the prior single-entry decoder exposed
only one byte at `body+30`. Its fields covered 31 bytes; the supported fixed
prefix actually covers **32 bytes**. A wire value `0001` must remain `0001`
with numeric rendering `1`, rather than its first byte `00`. The two controlled
54-byte Cubase 15 fixtures retain 22 opaque suffix bytes after that prefix.

## Implemented subset and bounds

For explicit class revisions 0–3, the decoder supports the shared prefix:

`i32 count + count × (f32 + f64 + f64 + u16)`

Each row occupies 22 bytes. Revisions 1–3 require the complete additional six
bytes: one `f32` and one raw `u16`. Revision 0 omits that suffix. The decoder
validates that the entire declared fixed table and required fixed suffix fit
inside the bounded class body before allocating candidate fields. A count of
zero, negative count, truncated table or unsupported revision yields no raw
tempo fields. Integer division validates counts without unchecked multiplication.

Each observation retains its absolute offset, storage type, invariant numeric
rendering and exact hex bytes, including NaN payloads, infinities and signed
zero. Anonymous `body+offset` field names avoid assigning musical meanings.
Attribute grammar and the rest of the object are not validated by this prefix
reader; a complete fixed prefix is not a claim of a complete object load.

Raw-field retention is capped at **256 fields per record** and **100,000 per
project across archives**. Counted tables retain complete rows in order;
budget exhaustion cannot jump over omitted rows to the final suffix. For a
revision-3 table, 63 rows plus the count and suffix require 255 fields. A larger
table retains 63 complete rows and its declared count, marks raw-field results
partial, and discloses retained/declared entry counts in provenance. Archive
`Partial` and `LimitReasons` also disclose omitted raw fields. Byte, header,
record-count and depth limits remain independent.

The existing `Read` API remains available. `ReadBounded` exposes the field list
and an explicit retention-partial flag for the inspector. This flag concerns
retained raw fields, rather than semantic completeness of Cubase data.

## Allocation and cancellation changes

The inventory scanner initializes an empty field list on each retained header.
The enrichment pass now reuses that immutable record instance when no fields
are decoded; previously it copied every record solely to attach an empty list.
The pass checks cancellation every 128 records. Each tempo-table decode formats
at most 63 rows, irrespective of an untrusted declared count. No quantified
end-to-end performance improvement is claimed for this change.

## Validation

The focused decoder/inventory suite passed **61 tests** and the full suite passed
**630 Release tests**, with both controlled private fixtures enabled. CLI and
Uno Desktop Release production builds passed with zero warnings/errors.
Five existing xUnit2031 advisories remain in unrelated test files.

Synthetic RIFF/RIF2 checks cover revisions 0–3, multiple entries, exact/partial
field budgets, malformed counts, truncated six-byte suffixes, raw nonfinite
values and signed zero. A two-archive stress fixture retains all 800 class
headers while limiting raw fields to exactly 100,000 and marking the second
archive partial. Native BPM remains unset. Source bytes remain unchanged.

The read-only corpus check covered **33 selected Cubase 10–15 CPR/BAK files**.
Each contained one retained tempo-track record: **26 revision-2** and **7
revision-3** observations, all with a declared count of one. The old exact-shape
decoder exposed 35 fields in five of those records; the updated prefix decoder
exposes **231 fields** across all 33. Every reported field's raw hex was compared
with the original bytes at its absolute offset, and existing fields kept their
bits apart from the deliberate one-byte-to-two-byte suffix correction. The raw
suffix words were zero in 30 files and one in three files. All source SHA-256
values remained unchanged. This supports the revised prefix in these selected
files without establishing musical meaning or genuine multi-entry coverage.

Genuine multi-entry tables and revision-0/1 projects still need independent
validation. Raw class inventories can also find incidental headers inside
opaque scalar bytes; synthetic signed-zero values exercise that distinction.
Such empty record matches do not gain raw tempo fields. No source project,
database, plug-in state or executable is modified.
