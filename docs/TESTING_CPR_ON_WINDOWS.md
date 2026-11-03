# Test the native Cubase CPR reader on Windows

The current native reader is **experimental and read-only**. It scans the CPR
container, identifies ROOT/ARCH streams and class records, and suggests track
names. It does not yet decode every plugin, route, automation lane, MIDI note,
fader setting or other proprietary state. The interface labels these as
candidate evidence so that a partial parse is never mistaken for complete
project reconstruction.

## Download and run

1. Open [Windows validation](https://github.com/AlexanderSeel/CubaseProjectExplorer/actions/workflows/windows-ci.yml).
2. Open the most recent **successful** workflow for \`main\`.
3. Under **Artifacts**, download \`CubaseProjectExplorer-CLI-win-x64\` and optionally
    \`CubaseProjectExplorer-Uno-Desktop-win-x64\`. Extract the ZIP.
4. Run the standalone CLI (no .NET SDK required):
   \`\`\`powershell
   .\CubaseInspector.Cli.exe cpr-structure "D:\Cubase\MySong\MySong.cpr" --markdown > structure.md
   .\CubaseInspector.Cli.exe cpr-structure "D:\Cubase\MySong\MySong.cpr" --json > structure.json
   .\CubaseInspector.Cli.exe inspect "D:\Cubase\MySong\MySong.cpr" --markdown > inspection.md
   \`\`\`
5. Inspect \`structure.md\`: check Version, candidate tracks, class counts,
   PArrangement, and any "PARTIAL" warning.
 6. To test the Uno Desktop shell, launch \`CubaseInspector.App.exe\` and use
    **Open project**. The Uno artifact is a build snapshot, not a signed installer.
7. Keep the original .cpr unchanged. Share the Markdown / JSON reports (redact
   names and paths if sensitive) and the version of Cubase used to create
   the file. The software will never ask Cubase to load any plugin in this path.

## How we validate

CI tests generated synthetic containers plus a public Cubase-5 donor from
\`omeriko9/MyDAW\` (downloaded by the workflow only). Expected from that donor:
12 chunks, 7 candidate MAudioTrackEvent records and Track1 through Track7,
plus a Cubase 5.1.1 version hint. The donor is **not bundled** in this repository
or our published artifacts.

Full native compatibility requires legally obtained actual Cubase 12/13/14/15/16
projects, comparisons against Cubase itself, and versioned track/plugin adapters.
Safe-copy **plugin deactivation** is deliberately disabled pending Cubase
round-trip validation. Ordinary unchanged backups are supported.

## Validated workflow example

[GitHub Actions run 40841213626](https://github.com/AlexanderSeel/CubaseProjectExplorer/actions/runs/40841213626) completed successfully with the real public MyDAW Cubase 5 donor and published two artifacts. The C# reader detected 12 chunks, seven expected `MAudioTrackEvent` names and a version hint of 5.1.1. This confirms a real CPR read at the **container / candidate track** level, not full decoding of Cubase 12–16 content.

## Crash backups from 2018/2019

The same `cpr-structure` and `inspect` commands accept `.bak` files **if their first twelve bytes contain RIFF/NUND**. The Uno Open Project picker also includes `.bak`. Unrelated backups are not treated as Cubase projects; incomplete Cubase backups can report `TRUNCATED_CPR` / `TRUNCATED_CHUNK` and must not be mistaken for a recovered session. Always keep the `.bak` source unchanged. A byte-identical copy is only a backup of the backup, not an automatic repair.

## Legacy Cubase SX/SL and pre-11 checklist

Run the same commands with archival `.cpr` files and capture their results.

| Application / file generation | Reading currently implemented | Validation level |
|---|---|---|
| Cubase SX 1, SX 2, SX 3 | Try RIFF/NUND chunks, bounded ROOT/ARCH and PAppVersion; candidate records only | Synthetic tests; actual SX archives still required |
| Cubase SL 1–3 | Same candidate-level inspection | Synthetic tests only |
| Cubase 4 through 10.5 | Same candidate-level inspection; labels older saved-by hints | Cubase 5.1.1 public CPR verified at structural/candidate-track level; other releases need real fixtures |
| Cubase 11+ | Existing candidate CPR reader | Per-release field validation remains incomplete |
| Cubase VST `.all`, `.arr` | No direct parser | Not supported; separate conversion required |

Recommended CLI sequence for each release:

```powershell
.\CubaseInspector.Cli.exe inspect "D:\LegacyCubase\Project.cpr" --json > project-candidates.json
.\CubaseInspector.Cli.exe cpr-structure "D:\LegacyCubase\Project.cpr" --json > project-structure.json
```

In the **Project Library**, select the file and inspect its **Cubase saved-by
version hint**, **generation** and evidence warnings. These are not assertions
about when the project was originally created. Look for truncation errors,
unknown ROOT/ARCH classes and candidate tracks; do not assume legacy mixer or
plugins are verified. For each original Cubase generation, record the number of
tracks in the actual application and compare it with the inspector's candidate
records to identify gaps. A structural match alone does not constitute full
project-format compatibility.

Keep the source file, its original media and all generated reports separate.
No Cubase version is launched, and plugins are not executed by the read-only
inspector.

## Private versioned fixture corpus (read-only)

Use `scripts/Test-PrivateCprFixtures.ps1` on a local Windows machine. The manifest
and **all Cubase source files must stay outside the Git checkout**. The script
checks SHA-256 of each fixture before generating JSON reports, keeps stderr and
exit codes, compares optional structural chunk/record/candidate-hint counts and
expected readable/invalid outcomes, and writes summary JSON/CSV without editing the projects. On successful structural checks it also runs `inspect --coverage-json` to capture a per-project field availability report. It does
**not** automatically certify field accuracy: compare the generated reports
against an independently recorded Cubase session.

Example private manifest (`fixtures.json`, next to its relative project files):

```json
[
  {
    "id": "cubase5-clean-audio",
    "file": "Cubase5/Example.cpr",
    "sha256": "REPLACE_WITH_64_HEX_SHA256",
    "savedBy": "Cubase 5.1.1",
    "expectedStatus": "readable",
    "expectedTracks": 7,
    "expectedChunks": 12,
    "expectedTrackHints": 7,
    "expectedClassRecords": null,
    "expectedPlugins": null,
    "expectedRouting": "unknown"
  }
]
```

```powershell
.\scripts\Test-PrivateCprFixtures.ps1 -Manifest "D:\PrivateCubaseFixtures\fixtures.json" `
  -Cli ".\publish\CubaseInspector.Cli.exe" `
  -OutputDirectory "D:\PrivateCubaseResults"
```

The manifest hash example is intentionally a placeholder; replace it with
`(Get-FileHash -Algorithm SHA256 <fixture>).Hash`. Retain the original
authoring Cubase version, edition, platform, real application track count,
instrument/FX/channel count, actual inserts, physical and logical routing
and provenance notes in private fixture records. Never upload project media,
plugin state, names or absolute project paths in public issues.

### Evidence and coverage matrix

| Generation | Real fixture provenance | Structural read | Verified track identities | Verified plugin instances | Mixer/tempo/routing |
|---|---|---|---|---|---|
| SX 1–3 / SL 1–3 | Not acquired | Synthetic only | Unverified | Unverified | Unverified |
| Cubase 4–10.5 | Public Cubase 5.1.1 donor at candidate level | Cubase 5 structural only | Unverified | Unverified | Unverified |
| Cubase 11 | Not independently verified | Candidate only | Unverified | Unverified | Unverified |
| Cubase 12–16 | Genuine per-version files required | Candidate only | Unverified | Unverified | Unverified |
| Legacy .bak crash recovery | Genuine damaged backups required | Header / truncation rejection only | Unverified | Unverified | Unverified |
| DAWproject | Version-specific exported fixtures required | Structured export reader | Exported fields only | Exported fields only | Exported fields only |

For intentionally invalid backups, set `expectedStatus` to `invalid` and optionally `expectedErrorCode` (for example `TRUNCATED_CPR` or `TRUNCATED_CHUNK`). The runner verifies both the nonzero exit and the diagnostic code; it never uses candidate tokens to treat damaged inputs as successfully recovered. The Windows CI smoke includes a temporary corrupt copy of the pinned public Cubase 5 donor, with the original source left unchanged.

The optional `expectedTrackHints` and `expectedClassRecords` fields compare only
**candidate parser output**, not the true number of Cubase UI tracks or plugin
instances. A failing mismatch is recorded in `fixture-results.json` and produces
a nonzero runner exit code. `expectedTracks` / `expectedPlugins` record external
Cubase expectations for later manual comparison; they are not currently asserted
against candidate records.

Use `inspect "your-project.cpr" --coverage-json` for a standalone, machine-readable overview of the fields actually exposed by the current reader. A `NotExported` field in a structured export means the inspected normalized export contains no such value; it does not prove that the original Cubase session lacked the setting.

**Promotion rule:** A class header, token frequency, candidate name or catalog
match is not a confirmed track or insert. Version-specific field decoding
requires independently observed offsets, a genuine legal fixture and a
regression test for both a present value and an absent/unknown value. File
modification times do not establish plugin last use. A truncated backup cannot
be repaired or rendered valid by incidental metadata tokens.

