# Cubase 15 RIF2 / MIDI controlled regression fixtures

Updated 2026-10-31. The two original CPR files were supplied by the project
owner. **Do not commit them:** they contain a Windows user-directory path and
a machine GUID and are ignored as `*.cpr`. The implementation and examples
below do not embed any original project bytes.

## Source files and expectations

| File | SHA-256 | Bytes | RIF2 payload bytes | Arrangement ARCH bytes | Notes |
|---|---|---:|---:|---:|---|
| CPETestEmpty.cpr | `cf0d6f5f1828d7da6857506b5973c5dcd7c6c1712ec60683862077be22a1bcdf` | 119154 | 119138 | 42335 | 0 |
| CPETestMidiC1_UpTo16Steps.cpr | `77fa9d77ea911ed161eea13c7b099e3943f6753b42c81b5c41f31be79a1b1804` | 139306 | 139290 | 56979 | 16 |

Both were saved by Cubase 15.0.40 / WIN64. Both have **7 ROOT and
7 ARCH chunks**, in this order:

1. Version / PAppVersion
2. Arrangement1 / PArrangement
3. ComputerGuid / CmString
4. Devices / FAttributes
5. WindowLayouts / UWindowLayout
6. ProjectLayouts / FAttributes
7. Metadata / StMedia::PAttributes

In both files, the first four bytes are `RIF2`, bytes 4–11 contain the
**big-endian uint64** number of bytes after the 16-byte header, and bytes
12–15 are `NUND`. The chunk grammar is `4-byte ASCII ID + big-endian
uint64 length + length bytes of payload`. There is no RIFF-style even-byte
padding in either example. Both sizes and all 14 chunk boundaries agree.

## MIDI note fields independently located

In the MIDI file, the PArrangement ARCH contains 16 populated
`MMidiNote` class records (plus an empty class-registration record).
Each populated object in this controlled example has a **112-byte body**:

| Offset within body | Field | Encoding | Observed |
|---:|---|---|---|
| 0 | note start position | big-endian float64 | 0, 120, 240, …, 1800 |
| 9 | MIDI note number | byte | 36, 37, …, 51 (Cubase C1–Eb2) |
| 10 | note-on velocity | byte | 100 |
| 73 | `adcn` marker | ASCII 4 bytes | part of this note object; not an independent note header |
| 77 | `adcn` tag | 2 bytes | `00 01` |
| 79 | `adcn` parameter | big-endian uint32 | 32 |
| 87 | note duration | big-endian float64 | 120 ticks |

16th-note spacing at 480 ticks per quarter note is consistent with
the supplied Cubase editing operation; verify timebase behavior on
additional versions before treating PPQ=480 as universal. Velocity zero
or MIDI pitch zero must not be rejected unconditionally in future
decoders. These fields are **version-scoped and candidate-only** until
confirmed across independent sessions, build versions and modified
note velocities / durations.

A separate type-registration record is **not** a MIDI event. Counting
the `MMidiNote` string or `adcn` occurrences alone is insufficient to
count notes or establish ownership.

## Run locally, without uploading private CPR data

1. Place both files in a directory *outside* the repository, for example
   `C:\CubaseTestFixtures`. Do not use a cloud-synced or public folder.
2. In PowerShell, from the repository root, run:

```powershell
$env:CPE_CPR_FIXTURE_DIR = "C:\CubaseTestFixtures"
dotnet test tests/CubaseInspector.Tests/CubaseInspector.Tests.csproj -c Release --filter FullyQualifiedName~CprRif2FixtureTests
Remove-Item Env:CPE_CPR_FIXTURE_DIR
```

The optional test checks file hashes, read-only inspection, 14 chunks and
all sixteen expected note number, position, duration and velocity values.
Without `CPE_CPR_FIXTURE_DIR`, the private test returns immediately;
independent synthetic tests still run on all platforms and in CI.

The existing `scripts/Test-PrivateCprFixtures.ps1` can also inspect
additional private CPR/BAK samples using an external manifest. Never
commit the binary fixtures, stdout reports with absolute user paths,
Cubase executables, or plug-in state blobs.

## Open work

- Make ownership of `MMidiNote` objects, parts, tracks and class-reference
  tables provable rather than relying on structural proximity.
- Cover versions with alternate note lengths or body layouts, controllers,
  pitch bend, aftertouch, sustain and automation.
- Map part-relative vs absolute timing and tempo map from real paired saves.
- Validate other RIF2 projects, including 2 GiB+ cases, truncation, malformed
  length fields, limits and byte swapping.
- Only promote to normalized `ProjectMidiNote` once multiple independently
  controlled fixtures validate timebase, ownership and semantics.
