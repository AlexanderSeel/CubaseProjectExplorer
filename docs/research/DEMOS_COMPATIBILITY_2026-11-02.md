# Read-only demo corpus: compatibility baseline

This is the baseline before the nested-attribute extension. See the
[continuation report](CUBASE15_NESTED_ATTRIBUTES.md) for 28,564 candidates and
zero arrangement-level partial files after that extension. Broader decoding
and semantic verification remain open.

Date: 2026-11-02. Source: the user-provided `E:\Demos` tree. Inspection used the
Release CLI's `cpr-structure --json` and `inspect --json` commands. Originals were
opened read-only and hashed before/after. Reports, local paths, source hashes and
the scratch review database remain in ignored `artifacts/demos/`; no CPR/BAK,
media, plug-in, executable or disassembly content is committed.

## Results

There are 21 `.cpr`/`.bak` paths totaling 123,038,654 bytes. Thirteen are genuine
project/backup files totaling 123,037,638 bytes; both CLI commands succeed for
all thirteen. Eight are macOS AppleDouble resource forks (seven 120-byte files
and one 176-byte file) under
`__MACOSX` (magic `00 05 16 07`), correctly rejected as unsupported input.
All 21 before/after SHA-256 comparisons matched.

| Demo / file | Saved-by text | Candidate track records | Note candidates | Arrangement partial |
| --- | --- | ---: | ---: | --- |
| Austin Hull / Cubase 15 project | 15.0.0 | 199 | 3,069 | Yes, 9 nested-attribute stops |
| Cubase 15 backup 01 | 15.0.0 | 200 | 3,069 | Yes, 9 nested-attribute stops |
| Cubase 15 backup 02 | 15.0.0 | 200 | 3,069 | Yes, 9 nested-attribute stops |
| Cubase 12 Pop / mixed | 12.0.0 | 180 | 1,652 | No |
| Cubase 12 Pop / unmixed | 12.0.0 | 170 | 1,652 | No |
| Austin Hull / Cubase 13 | 13.0.0 | 212 | 3,402 | No |
| Austin Hull / Cubase 14 | 14.0.5 | 188 | 1,422 | No |
| Cubase 9.5 demo | 9.5.10 | 105 | 4,065 | No |
| Jazz Practice | 8.0.5 | 14 | 406 | No |
| Lafzo ki baatein | 5.1.0 | 24 | 1,784 | No |
| Sunrise | 10.5.0 | 68 | 1,144 | Yes, 5 nested-attribute stops |
| Project Files | 5.1.0 | 306 | 2,502 | No |
| VCA Mixer SetUp | 6.5.0 | 45 | 0 | No |
| **Total** | | | **27,236** | **4 files** |

Track-record counts are snapshot candidates, not the much larger count of all
serialized archive records and not verified Cubase-visible track totals. “No” in
the last column means no arrangement scan limit was reported for this bounded
pass. It does not mean all fields/events/plug-in state were decoded. Zero MIDI
candidates do not prove that a project contains no MIDI.

The corpus includes saved-by 5.1 through 15.0, but has no representative of every
release: in particular this tree does not supply distinct Cubase 7 or 11 saved-by
files. Backups of one session are correlated cases, not three independent version
fixtures. Earlier separately documented SX1–3/Cubase 6–14 samples complement this
tree without replacing independent per-version verification.

## Exact remaining nested-attribute boundary

Every reported packed-note failure in the four partial files stops first on
attribute ID `0x53634172` (`ScAr`) with type flags `0x0022`. Its first matching
dispatch bit is `0x0002`. The static value reader `0x1445749D0` reads flags through
`0x1439DF250`, tests bit 2 before container/string/scalar branches, then asks the
archive's `+0x20` object reader for `CmObject`. Flags `0x0022` must therefore not
be treated as the fixed 14-byte scalar shape.

The bounded archive inventory independently finds framing exactly where the
attribute's nested value begins:

| File / note body offset | ScAr item offset | Nested value header | Inventory observation |
| --- | ---: | ---: | --- |
| Cubase 15 project / 3,597,219 | 3,597,264 | 3,597,270 | Interned `CmAttribCollection`, rev0, 10-byte body at 3,597,278 |
| Sunrise / 6,768,227 | 6,768,272 | 6,768,278 | Interned `CmAttribCollection`, rev0, 72-byte body at 6,768,286; nested `ScTextAttrib`, rev0, 54-byte body |

These observations identify a concrete next codec task: consume validated nested
object/reference framing within the counted attribute list, then resume the next
attribute and note suffix at an exact boundary. The current implementation stops
and reports partial evidence. It does not jump to a plausible later note or
assign meaning to the score/text attribute. The title or tag alone is not proof
of articulation, notation or expression semantics.

The updated diagnostic includes the attribute ID, flags, absolute source offset
and required grammar. Synthetic RIFF/RIF2 regression cases place later note-like
bytes after an unsupported nested item and assert no recovered prefix/later
notes are invented.

## ASM reproduction and evidence limits

The same local 1,079,140,046-byte IDA export identifies executable SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
Three complete excerpts were re-extracted:

| Target | Procedure | Export byte offset | Excerpt bytes | SHA-256 |
| --- | --- | ---: | ---: | --- |
| Attribute collection reader | `0x141B74340` | 171,499,576 | 2,915 | `2387d4ab12b73bc88805ac5598604c14b5907ac0e8a6da87e3ffd28c90b62bcb` |
| Attribute item reader | `0x144574E30` | 497,446,408 | 368 | `60d3aae7a3a95ce5c84f053d40bbf2bf3183ae740e80244e078a2f13ea07fc4c` |
| Typed value reader | `0x1445749D0` | 497,438,624 | 3,372 | `31318fa85f23d55d525191ba856309ee7bb13d1e05fe5f2156151ee65228fcdc` |

```powershell
python scripts/inspect_cubase15_ida_export.py E:\Cubase15.exe.asm `
  --function event-attribute-reader --function event-attribute-item-reader `
  --function event-attribute-value-reader --output artifacts/demos/asm-attributes

dotnet src/CubaseInspector.Cli/bin/Release/net10.0/CubaseInspector.Cli.dll `
  cpr-structure '<local demo.cpr>' --json
dotnet src/CubaseInspector.Cli/bin/Release/net10.0/CubaseInspector.Cli.dll `
  inspect '<local demo.cpr>' --json
```

For a manifest-driven sweep with source-hash checks, use the existing
`scripts/Test-PrivateCprFixtures.ps1` and keep its manifest/results outside tracked
content. The local full sweep used `artifacts/demos/inspect_demos.py` and wrote
`manifest.json`, `NN-cpr-structure.json` and `NN-inspect.json`.

Static branches and successful reads do not verify note timing, ownership,
plug-in identities, automation, full project state or universal backward
compatibility. Compare genuine Cubase-visible state or controlled exports before
promoting any of those fields. See [reader completeness](READER_COMPLETENESS_2026-11-02.md),
[inherited serializers](CUBASE15_BACKWARD_COMPATIBILITY.md) and
[packed callbacks](CUBASE15_PACKED_EVENT_CALLBACKS.md).
