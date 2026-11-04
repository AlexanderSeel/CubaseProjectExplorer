# Cubase 15 C-export follow-up: mixed MIDI traversal

Investigated 2026-11-04 from the user-supplied local Ghidra C export.
This extends [the counted stream investigation](CUBASE15_MIDI_STREAM_COMPATIBILITY.md).
It establishes a bounded inspection subset, with note values retained as
candidates. Musical timing, effective values, ownership and compatibility
across releases still require comparison with Cubase and exports.

## Inputs and reproduction

The 590,827,789-byte `Cubase15.exe.c` snapshot has SHA-256
`35b8643f6f5211cc4e5bf0ccd5efd5f7541f9cc289b09d28a4f9dd1d0a444cf8`.
The associated local executable was independently hashed as
`b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0`,
matching the previously investigated Cubase 15.0.40 binary. The C export has
no input-identity header; its text hash identifies the snapshot, without
authenticating the decompiler's derivation or recovering omitted arguments.
Addresses below apply to this executable only.

```powershell
python scripts/inspect_cubase15_c_export.py D:\ReverseEng\Cubase15.exe.c `
  --function midi-packed-event-factory `
  --function midi-polypressure-registration --function midi-polypressure-constructor `
  --function midi-polypressure-reader --function midi-polypressure-writer `
  --function midi-programchange-registration --function midi-programchange-constructor `
  --function midi-event-reader --function midi-sysex-registration `
  --function midi-sysex-constructor --function midi-sysex-reader `
  --function midi-sysex-buffer-constructor --function midi-sysex-buffer-reader `
  --function midi-sysex-buffer-writer --output artifacts/c-mixed-midi
```

The script uses a read-only mapping, rejects an unknown export hash and reports
bounded definitions with text offsets, lengths and hashes. Excerpts remain
local in ignored artifacts. It does not execute or modify Cubase or projects.
Decompiler output and private project data must not be committed.

## Additional event boundaries

The previously traced factory `0x141BB14B0` switches on exact discriminator
bytes; it does not mask channel nibbles. The registration/factory/constructor
chain gives these additional classes, each registered at revision 0:

| Byte | Class | Registration | Constructor | Primary vtable | Reader at `+0x90` |
| --- | --- | --- | --- | --- | --- |
| `A0` | `MMidiPolyPressure` | `0x14157FE30` | `0x141BB5760` | `0x145F41F48` | `0x141BB5AE0` |
| `C0` | `MMidiProgramChange` | `0x14157FE80` | `0x141BB6170` | `0x145F42AC8` | `0x141BB0390` |
| `F0` | `MMidiSysex` | `0x14157FED0` | `0x141BB70C0` | `0x145F43C08` | `0x141BB7580` |

The reader-slot pointers were checked directly in the hash-matched PE file.
Poly-pressure's callback calls the existing `MMidiEvent` reader, then the
already traced in-memory curve-attribute loader `0x143161B60`, with no further
stream primitives. Its writer `0x141BB5B30` mirrors that boundary. Program
change's reader slot points directly to `MMidiEvent`'s reader, with no derived
suffix. With explicit base revision 2 and supported scalar attributes, both
therefore occupy `18 + 14 × attribute-count` bytes including the discriminator.
Their loaded values are not promoted to normalized pressure/program events.

The `F0` callback first loads the same base, then calls a byte buffer object's
reader. The buffer constructor `0x141BBBBF0` installs vtable `0x145EF6370`;
the hash-matched binary's reader slot points to `0x141CF3610`:

1. Read a signed 32-bit byte count.
2. Read another 32-bit word, unused for payload extent by this callback.
3. Read exactly the first count's number of raw bytes.

The writer `0x141CF36B0` writes the same current length twice, then the raw
bytes. The reader uses only the first word for extent, so unequal words are
not automatically corruption, and the second word is not an extra length.
The inspector requires a nonnegative first count within the containing body;
it skips the bytes with span indices, without allocating or decoding SysEx.
An empty payload is permitted. MIDI-looking bytes inside it cannot produce
counted notes. The full supported packet width is
`26 + 14 × attribute-count + payload-byte-count`.

This does not justify skipping every factory case as a scalar base event:
for example `40` constructs a class whose reader `0x141BBA0E0` reads an
additional `u16`, `i32` and `f64`. It remains unsupported by this batch.
Object/string/container attributes also remain unsupported.

## Bounds and allocation changes

All additional packets require an unambiguous named revision-0 declaration
available before their bodies. The counted path retains its existing named
part/base/note revision checks, complete-list body-end check, iteration-based
cancellation, 250,000-event bound, 10,000-note bound and byte budgets.
Negative/oversized SysEx counts, unknown discriminators, unsupported attributes
and truncated complete bodies reject the counted interpretation. A byte-budget
cut inside a supported packet retains preceding candidates explicitly as
partial. The separate weak attributed-pattern scan retains its existing label
when the counted grammar cannot be established.

The note limit is now checked when another packed note is encountered, allowing
supported non-note events after exactly 10,000 notes to finish the list. These
events still undergo revision, extent and body-end validation. A subsequent
note still produces a partial result. Archives with no packed MIDI parts no
longer construct the counted reader's full-header dictionary and object-ID set.
No quantified application performance improvement is claimed.

## Validation

The focused RIFF/RIF2 counted-stream suite passed all 48 cases, including new
mixed-event, scalar attributes before SysEx payloads, embedded-payload,
revision, malformed-length, byte-budget and trailing-non-note regressions.
The full suite passed **579 Release tests**, with `CPE_CPR_FIXTURE_DIR` pointing
at the two hash-pinned controlled Cubase 15 files. CLI and Uno Desktop Release
production builds passed with zero warnings/errors. The first Desktop attempt
used Debug-restored Uno DevServer assets and failed with `UNOB0019`; restoring
the app with `-p:Configuration=Release` resolved it without changing source or
disabling optimization. Five existing xUnit2031 advisories remain in unrelated
test files.

The existing read-only corpus oracle passed on **27 pre-10 files**: all
**27,774 independently anchored note-only packets** matched exact offsets and
raw fields, with no duplicates or unexpected packets in those complete lists.
Every source SHA-256 remained unchanged. Comparison with the prior CLI reports
also preserved all **31,798 existing note candidates**, including raw values
and candidate part/track offsets. No new offsets were added. **1,017** prior
weak-pattern observations now have counted-stream boundaries; an independent
bounded walk of that part found 1,017 note packets, 14 controller packets,
one pitch-bend packet and two program-change packets, ending exactly at its
body boundary. All 31,798 candidates in this selected corpus now come from
counted lists. This is stronger structural evidence for those same observations,
without independent musical validation.

At the MIDI pass, the C-export tool inventoried 75 of its then 77 known targets as
bounded definitions. The [routing follow-up](CUBASE15_C_AUDIO_ROUTING.md) added
multiline signature extraction, which also locates the archive constructor;
only the inherited archive-current-version target remains absent under its IDA
function name. The mixed-event targets resolve.
Additional extractor checks covered definitions versus calls/prototypes, absent
labels, unknown-hash rejection and the 64 KiB extent limit. Full proprietary
excerpts, fixture paths and JSON reports remain in ignored local artifacts.

Synthetic fixtures test the inferred grammar; they do not independently
establish Cubase's musical interpretation. Genuine poly-pressure/SysEx mixed
streams, paired exports and per-release Cubase comparisons remain required
before normalized native MIDI or Standard MIDI export is enabled.
