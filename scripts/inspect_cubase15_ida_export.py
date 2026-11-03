"""Read selected procedures from a local IDA text export without executing it.

The file may still be growing: searches use one fixed, read-only mapped prefix.
Only procedure excerpts requested below are written, under --output. Full
disassemblies and excerpts must remain local/ignored; do not commit them.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import mmap
from pathlib import Path
import re
import sys

EXPECTED_INPUT_SHA256 = "b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0"
MAX_PROCEDURE_BYTES = 64 * 1024
TARGETS = {
    "midi-event-registration": "sub_14157FD50",
    "midi-note-registration": "sub_14157FD90",
    "midi-event-reader": "sub_141BB0390",
    "midi-event-writer": "sub_141BB05D0",
    "midi-note-reader": "sub_141BB4690",
    "midi-note-writer": "sub_141BB49A0",
    "event-attribute-reader": "sub_141B74340",
    "event-attribute-item-reader": "sub_144574E30",
    "event-attribute-value-reader": "sub_1445749D0",
    "cm-string-registration": "sub_1418DDCF0",
    "cm-string-factory": "sub_14458BA30",
    "cm-string-constructor": "sub_141BBBB20",
    "cm-string-class-check": "sub_1419DD7C0",
    "cm-string-reader": "sub_14458B250",
    "cm-string-writer": "sub_14458B270",
    "data-node-registration": "sub_1417DA0C0",
    "event-flags-reader": "sub_1439DF250",
    "byte-reader": "sub_1439DEFF0",
    "attribute-id-reader": "sub_1439DF650",
    "uint64-reader": "sub_1439DF880",
    "float64-reader": "sub_1439DFFF0",
    "archive-constructor": "sub_1445E9500",
    "archive-current-version": "sub_1445E78A0",
    "archive-named-version": "sub_1445E9E20",
    "archive-class-reader": "sub_1445EA890",
    "archive-object-reader": "sub_1445E9990",
    "archive-object-length-reader": "sub_1445EB010",
    "parameter-curve-base-registration": "sub_1417D7A60",
    "parameter-bezier-registration": "sub_1417D7AB0",
    "parameter-bezier-class-check": "sub_141E044B0",
    "automation-track-registration": "sub_141623B20",
    "automation-lane-registration": "sub_141623B70",
    "automation-track-factory": "sub_14225E800",
    "automation-lane-factory": "sub_14225E920",
    "automation-lane-constructor": "sub_14224F630",
    "automation-lane-reader": "sub_14224EEE0",
    "automation-lane-writer": "sub_14224F030",
    "automation-track-reader": "sub_142251240",
    "automation-track-writer": "sub_1422514D0",
    "track-input-properties-writer": "sub_141BDDD80",
    "track-input-properties-reader": "sub_141BDE190",
    "channel-io-reference-collector": "sub_143513190",
    "archive-declaration-reader": "sub_1445EA6A0",
    "archive-declaration-writer": "sub_1445EA650",
    "midi-part-registration": "sub_141580CA0",
    "midi-part-factory": "sub_141BCA170",
    "midi-part-reader": "sub_141BC6E90",
    "midi-part-writer": "sub_141BC7070",
    "midi-packed-event-factory": "sub_141BB14B0",
    "part-eventlist-legacy-reader": "sub_1431BDE30",
    "part-base-reader": "sub_143192630",
    "part-timebase-reader": "sub_143438E50",
    "part-name-reader": "sub_14458AAC0",
    "midi-note-constructor": "sub_141BB3BA0",
    "midi-controller-registration": "sub_14157FD00",
    "midi-aftertouch-registration": "sub_14157FCB0",
    "midi-pitchbend-registration": "sub_14157FDE0",
    "midi-poly-pressure-registration": "sub_14157FE30",
    "midi-program-change-registration": "sub_14157FE80",
    "midi-sysex-registration": "sub_14157FED0",
    "midi-chord-registration": "sub_14157FC60",
    "midi-note-change-registration": "sub_14157FF20",
    "midi-score-registration": "sub_1416C4C10",
    "midi-poly-pressure-factory": "sub_141BBE540",
    "midi-program-change-factory": "sub_141BBE580",
    "midi-sysex-factory": "sub_141BBE5B0",
    "midi-chord-factory": "sub_141BBE350",
    "midi-note-change-factory": "sub_141BBE5E0",
    "midi-score-factory": "sub_1427EDDC0",
    "midi-controller-constructor": "sub_141BB63A0",
    "midi-aftertouch-constructor": "sub_141BB5D20",
    "midi-pitchbend-constructor": "sub_141BB6B90",
    "midi-poly-pressure-constructor": "sub_141BB5760",
    "midi-program-change-constructor": "sub_141BB6170",
    "midi-sysex-constructor": "sub_141BB70C0",
    "midi-chord-constructor": "sub_141BB7B10",
    "midi-note-change-constructor": "sub_141BB9DE0",
    "midi-score-constructor": "sub_1427EB240",
    "midi-sysex-payload-constructor": "sub_141BBBBF0",
    "midi-controller-reader": "sub_141BB68B0",
    "midi-aftertouch-reader": "sub_141BB5FF0",
    "midi-pitchbend-reader": "sub_141BB6F40",
    "midi-poly-pressure-reader": "sub_141BB5AE0",
    "midi-poly-pressure-writer": "sub_141BB5B30",
    "midi-sysex-reader": "sub_141BB7580",
    "midi-sysex-writer": "sub_141BB7600",
    "midi-sysex-payload-reader": "sub_141CF3610",
    "midi-sysex-payload-writer": "sub_141CF36B0",
    "midi-chord-reader": "sub_141BB7E00",
    "midi-chord-writer": "sub_141BB7E50",
    "midi-note-change-reader": "sub_141BBA0E0",
    "midi-note-change-writer": "sub_141BBA160",
    "midi-score-reader": "sub_1427EBB00",
    "midi-score-writer": "sub_1427EBC80",
    "event-post-attribute-reader": "sub_141BB2C10",
    "event-pre-write-state": "sub_141BB2E10",
    "event-post-write-state": "sub_141BB2EB0",
    "midi-curve-attribute-loader": "sub_143161B60",
    "tempo-track-reader": "sub_1431E2E00",
    "tempo-track-writer": "sub_1431E30F0",
    "time-signature-reader": "sub_1431D3B70",
    "time-signature-writer": "sub_1431D3C40",
    "marker-reader": "sub_1426FC400",
    "marker-writer": "sub_1426FC470",
    "midi-track-registration": "sub_141581220",
    "midi-track-factory": "sub_141BEFE80",
    "midi-track-reader": "sub_141BCCFB0",
    "midi-track-writer": "sub_141BCD590",
    "audio-track-registration": "sub_1415CED60",
    "audio-track-factory": "sub_141E8C6B0",
    "audio-track-reader": "sub_141E85B90",
    "audio-track-writer": "sub_141E85F50",
    "instrument-track-registration": "sub_14168CF60",
    "instrument-track-factory": "sub_142615CA0",
    "plugin-slot-registration": "sub_1415A4B10",
    "plugin-slot-factory": "sub_141D4FDA0",
    "plugin-slot-reader": "sub_141D49320",
    "plugin-slot-writer": "sub_141D49BC0",
    "plugin-slot-loader": "sub_141D49EA0",
}


def inspect(path: Path, names: list[str], output: Path | None = None) -> dict:
    snapshot_bytes = path.stat().st_size
    if snapshot_bytes == 0:
        raise ValueError("IDA export is empty")
    rows = []
    with path.open("rb") as source:
        with mmap.mmap(source.fileno(), snapshot_bytes, access=mmap.ACCESS_READ) as data:
            header = data[: min(snapshot_bytes, 16 * 1024)]
            match = re.search(rb"Input SHA256\s*:\s*([a-fA-F0-9]{64})", header)
            if not match or match[1].decode().lower() != EXPECTED_INPUT_SHA256:
                raise ValueError("IDA header does not identify the expected Cubase 15 input hash; known addresses were not applied")
            for name in names:
                label = TARGETS[name]
                start = data.find((label + " proc").encode("ascii"))
                if start < 0:
                    rows.append(dict(name=name, label=label, status="not present in snapshot"))
                    continue
                end = data.find((label + " endp").encode("ascii"), start,
                                min(snapshot_bytes, start + MAX_PROCEDURE_BYTES))
                if end < 0:
                    rows.append(dict(name=name, label=label, file_offset=start,
                                     status="end marker not present within 64 KiB"))
                    continue
                end += len(label) + len(" endp")
                raw = data[start:end]
                row = dict(name=name, label=label, status="complete procedure excerpt",
                           file_offset=start, bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
                if output is not None:
                    output.mkdir(parents=True, exist_ok=True)
                    destination = output / (name + ".txt")
                    destination.write_text(raw.decode("utf-8", errors="replace")
                                           .replace("\r\r\n", "\n").replace("\r\n", "\n"),
                                           encoding="utf-8")
                    row["excerpt"] = destination.name
                rows.append(row)
    return dict(input_name=path.name, expected_executable_sha256=EXPECTED_INPUT_SHA256,
                snapshot_bytes=snapshot_bytes, procedures=rows,
                warning="The IDA header identifies the input executable, not the export's integrity. "
                        "Class versions and inferred field meanings require independent validation.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export", type=Path)
    parser.add_argument("--function", choices=TARGETS, action="append", dest="functions")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.export, args.functions or list(TARGETS), args.output), indent=2))
        return 0
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
