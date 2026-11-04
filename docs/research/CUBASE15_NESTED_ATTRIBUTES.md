# Cubase 15 nested event attributes: bounded reader extension

Investigated and implemented 2026-11-02 from the local user-provided executable,
IDA export and genuine demo files. No source project, executable or disassembly
was modified or executed. The application/CLI were built and run independently;
no plug-in binaries were loaded.

## Implemented boundary subset

The counted MMidiPart reader now walks the MMidiEvent-v2 attribute list item by
item. It consumes the existing exact scalar flags 1/4 and these two observed
object-valued combinations:

| ID / flags | Required framed class | Supported revision / prefix | Treatment |
| --- | --- | --- | --- |
| `ScAr` / `0x0022` | `CmAttribCollection` | Class rev0, no extra base prefix | Entire framed value stays opaque |
| `bOuc` / `0x0002` | `MParamEventCurveRationalBezier` | Class rev0; named `MParamEventCurveBase` rev0; optional exact base prefix | Entire framed value stays opaque |

Supported nulls consume one mode-width token. Positive object references must
resolve to an earlier complete object of the required class/revision. New
objects require matching exact-cursor class framing and a bounded declared body
length. Their contents are skipped; no nested score/text/curve values are
decoded, applied to base fields or promoted to musical state.

The 10,000-detail archive inventory can omit later headers while retaining known
class declarations. For a supported interned class token at the exact attribute
cursor, the new reader can validate the earlier declaration and consume the
mode-width length directly. It does not scan for a plausible header, chase a
new declaration or expand global inventory budgets. The resulting object can
serve as an earlier supported reference. Inventory-limit warnings remain intact.

After every supported item, the cursor resumes at the next attribute, then at
the event-specific suffix. Modern note fields still require the established
MMidiEvent-v2/MMidiNote-v1 declarations, finite values, exact suffix length and
complete counted-list ending. Evidence explicitly says nested attributes were
not applied to the raw fields. Unknown IDs/flags/classes/revisions/references,
string/container branches, incomplete bodies and budgets remain partial.

This initial extension applied to counted streams. The subsequent
[typed-note continuation](TYPED_NOTE_ATTRIBUTES_TEST_AUDIT_2026-11-02.md) shares
this bounded framing with separately framed MMidiNote records. Remaining
object/string/container cases stay open in [the reader inventory](../READER_FEATURES.md).

## ASM and independent executable evidence

Executable: 135,534,616 bytes, SHA-256
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`.
IDA export: 1,079,140,046 bytes; its header identifies that executable hash.
Addresses apply only to this Cubase 15.0.40 build.

| Procedure | Observed behavior |
| --- | --- |
| `0x1445749D0` | Reads u16 flags, tests bit 2 before container/string/scalar branches, and asks the archive's `+0x20` callback for CmObject. Both observed flag values select this branch. |
| `0x1445EA890` | Class reader handles mode-width tokens, prefix/inline/interned declarations and calls the body-length helper for new objects. |
| `0x1445EB010` | Body-length helper reads u64 in wide mode, unsigned u32 otherwise; negative/failed native lengths fail. |
| `0x1445E9990` | Object reader calls the class reader, registers the object at the size-field position relative to the archive origin, calls its serializer, and returns/seeks to saved body start plus declared length. |
| `0x1417D7A60` | Registers MParamEventCurveBase at class revision 0. |
| `0x1417D7AB0` | Registers MParamEventCurveRationalBezier at class revision 0 with the base registration pointer. |
| `0x141E044B0` | Derived class check recognizes the curve class, its base and CmObject when inheritance checks are requested. |
| `0x141BB4690` | Existing note callback reads the MMidiEvent base followed by three f64 values and the named note-revision byte. Attribute processing can affect final state. |

Eight complete procedure excerpts were extracted locally. Newly added extraction
targets cover the object-length helper and three curve registration/type checks.
The length-helper excerpt is 651 bytes at export byte offset 501,014,290 with
SHA-256 `c93d22207c90614423ce8941b202f085feac8ce6aae247ac69bb0218238dab6a`.
The curve registration excerpts are 453/465 bytes with hashes
`c56f0c4460cdef0e9b823f8868efb5727b9b949a4ae37f242c10423b664703d9` and
`c155fdcd31ceb0c6599d43863dd907664486f7b03032e5862a08f56206f917b3`.

Capstone 5.0.9 independently disassembled the following bounded ranges directly
from the hashed PE. Ranges can include alignment bytes; they are not assertions
about complete unwind-function extents.

| Range, end exclusive | Bytes | Machine-code SHA-256 |
| --- | ---: | --- |
| `0x1445E9990..0x1445E9E10` | 1,152 | `6b35b271b86db444e3df53228ff4e395865196ee0ab235071dacac797dbe49fa` |
| `0x1445EA890..0x1445EAD6B` | 1,243 | `1365d9240f41d06908c95bf20c2a01a570f9b930009f505e1afb554e5378cc98` |
| `0x1445EB010..0x1445EB060` | 80 | `53d171fd7a37b669fe6ae845b60c893865d27fbe8ead5c84dd2706d9f763be0e` |

The executable instructions corroborate the class-reader calls and mode-dependent
length helpers, plus the object-reader's saved-start/declared-size seek. They
establish wire boundaries, not curve parameters, notation meaning or valid final
Cubase state.

```powershell
python scripts/inspect_cubase15_ida_export.py E:\Cubase15.exe.asm `
  --function archive-object-reader --function archive-class-reader `
  --function archive-object-length-reader --function event-attribute-value-reader `
  --function midi-note-reader --function parameter-curve-base-registration `
  --function parameter-bezier-registration --function parameter-bezier-class-check `
  --output artifacts/nested-attributes/asm

# Use a local Python environment with Capstone 5 for --disassemble.
python scripts/inspect_cubase15_serializers.py E:\Cubase15.exe `
  --function archive-object-reader --function archive-class-reader `
  --function archive-body-size --disassemble
```

## Paired genuine-demo results

Both CLI structure and snapshot commands were re-run on the same 21 paths under
`E:\Demos`. All thirteen genuine CPR/BAK files still parse. Eight AppleDouble
resource files remain rejected (seven 120-byte files and one 176-byte file).
Every source hash matches both its immediate before/after value and the saved
baseline hash. No originals were changed.

| File group | Baseline candidates | New candidates | Delta | Arrangement partial before → after |
| --- | ---: | ---: | ---: | --- |
| Cubase 15 project | 3,069 | 3,330 | +261 | Yes → No |
| Cubase 15 backup 01 | 3,069 | 3,330 | +261 | Yes → No |
| Cubase 15 backup 02 | 3,069 | 3,330 | +261 | Yes → No |
| Cubase 10.5 Sunrise | 1,144 | 1,689 | +545 | Yes → No |
| Other nine readable demos | 16,885 | 16,885 | 0 | No → No |
| **Total** | **27,236** | **28,564** | **+1,328** | **4 → 0 files** |

Paired comparisons found no lost source offsets and no changes in any previously
found note's position, duration, pitch, velocity, channel, raw flags, possible
track/part names/offsets or source layout. New values remain candidates; there is
no independent Cubase-visible verification of the additional notes yet.

Clearing arrangement-level partial flags is not a whole-project completeness
claim. Inventory detail caps can still produce warnings, typed nested notes are
not expanded by this patch, and many project entities remain undecoded. Backups
of one session are correlated fixtures, not independent compatibility evidence.

Local reports and the paired comparison are under ignored
`artifacts/nested-attributes/`: `manifest.json`, `NN-cpr-structure.json`,
`NN-inspect.json`, `paired-summary.json`, `asm/` and `binary-disassembly.txt`.
The original [demo baseline](DEMOS_COMPATIBILITY_2026-11-02.md) is retained.

## Regression and production validation

- **622 Release tests passed**, none failed or skipped. Five existing xUnit2031
  warnings appear on test recompilation; they are unrelated to this change.
- New synthetic RIFF/RIF2 cases cover opaque note-like payloads, following scalar
  attributes, correct raw values/offsets, nulls/back references, forbidden forward
  references, unsupported classes/revisions/flags, malformed lengths, byte budgets,
  corrupt list trailers, base-prefixed curves and omitted inventory headers.
- CLI Release build passed. Desktop self-contained win-x64 publish passed with
  an isolated BaseOutputPath; the ordinary Desktop output was locked by an
  already-running app (PID 42436, MSB3027/MSB3021). That process was left running.
- The self-contained published CLI also read the genuine Cubase 15 project and
  returned the expected 3,330 note candidates at candidate evidence level.
- No UI behavior was changed in this reader continuation. Earlier live
  DPI/keyboard/accessibility/playback acceptance remains pending.

Production artifacts are local/ignored under `artifacts/nested-attributes/release/`.
