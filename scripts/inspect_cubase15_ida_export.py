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
    "midi-event-reader": "sub_141BB0390",
    "midi-note-reader": "sub_141BB4690",
    "midi-note-writer": "sub_141BB49A0",
    "event-attribute-reader": "sub_141B74340",
    "event-flags-reader": "sub_1439DF250",
    "archive-class-reader": "sub_1445EA890",
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
