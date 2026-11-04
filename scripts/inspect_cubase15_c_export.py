"""Inventory bounded callbacks in the known local Ghidra C export, read-only.

The export has no executable identity header. Require its exact snapshot hash;
that identifies text only, not independently the executable it was derived from.
Optional excerpts contain proprietary code and must stay in ignored artifacts.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import mmap
from pathlib import Path
import sys

from inspect_cubase15_ida_export import TARGETS as IDA_TARGETS


EXPECTED_EXPORT_SHA256 = "35b8643f6f5211cc4e5bf0ccd5efd5f7541f9cc289b09d28a4f9dd1d0a444cf8"
MAX_PROCEDURE_BYTES = 64 * 1024
TARGETS = {name: "FUN_" + label.removeprefix("sub_").lower()
           for name, label in IDA_TARGETS.items()}
TARGETS.update({
    "midi-polypressure-registration": "FUN_14157fe30",
    "midi-polypressure-factory": "FUN_141bbe540",
    "midi-polypressure-constructor": "FUN_141bb5760",
    "midi-polypressure-reader": "FUN_141bb5ae0",
    "midi-polypressure-writer": "FUN_141bb5b30",
    "midi-programchange-registration": "FUN_14157fe80",
    "midi-programchange-factory": "FUN_141bbe580",
    "midi-programchange-constructor": "FUN_141bb6170",
    "midi-sysex-registration": "FUN_14157fed0",
    "midi-sysex-constructor": "FUN_141bb70c0",
    "midi-sysex-reader": "FUN_141bb7580",
    "midi-sysex-buffer-constructor": "FUN_141bbbbf0",
    "midi-sysex-buffer-reader": "FUN_141cf3610",
    "midi-sysex-buffer-writer": "FUN_141cf36b0",
    "midi-extended-event-reader": "FUN_141bba0e0",
    "audio-routing-inventory": "FUN_143513190",
    "audio-routing-slot-inventory": "FUN_143518a00",
    "audio-routing-slot-remap": "FUN_143518370",
    "audio-bus-selection-reader": "FUN_14355fb90",
    "audio-bus-selection-writer": "FUN_14355fbb0",
    "audio-channel-writer": "FUN_14356e550",
    "audio-channel-reader": "FUN_14356ec30",
    "audio-bus-uid-reader": "FUN_14370f840",
    "audio-child-bus-reader": "FUN_14370f880",
    "audio-bus-parent-reader": "FUN_14370fff0",
    "audio-bus-writer": "FUN_143710170",
    "tempo-track-registration": "FUN_1417de230",
    "tempo-track-factory": "FUN_1431e8420",
    "boolean-u16-reader": "FUN_1439e0260",
    "boolean-u16-writer": "FUN_1439e0210",
})


def find_procedure(data: mmap.mmap, label: str) -> tuple[int, bytes] | None:
    """Find a definition rather than a call or prototype, with bounded extent."""
    token = (label + "(").encode("ascii")
    position = 0
    while True:
        position = data.find(token, position)
        if position < 0:
            return None
        signature_end = data.find(b")", position, position + 4096)
        if signature_end < 0:
            return None
        line_end = data.find(b"\n", signature_end, signature_end + 4096)
        if line_end < 0:
            return None
        start = data.rfind(b"\n", max(0, position - 4096), position) + 1
        prefix = data[start:position]
        if (not any(value in prefix for value in (b";", b"=", b"{", b"}", b"(")) and
                not data[signature_end + 1:line_end].strip() and
                data[line_end + 1:line_end + 12].lstrip().startswith(b"{")):
            limit = min(len(data), start + MAX_PROCEDURE_BYTES)
            ends = [(end, len(marker)) for marker in (b"\n}\r\n", b"\n}\n")
                    if (end := data.find(marker, line_end, limit)) >= 0]
            if not ends:
                raise ValueError(f"{label}: no closing brace within 64 KiB")
            end, marker_bytes = min(ends)
            return start, data[start:end + marker_bytes]
        position = line_end + 1


def inspect(path: Path, names: list[str], output: Path | None = None) -> dict:
    rows = []
    with path.open("rb") as source:
        if not path.stat().st_size:
            raise ValueError("C export is empty")
        with mmap.mmap(source.fileno(), 0, access=mmap.ACCESS_READ) as data:
            digest = hashlib.sha256(data).hexdigest()
            if digest != EXPECTED_EXPORT_SHA256:
                raise ValueError(f"Unrecognized C export SHA-256 {digest}; known addresses were not applied")
            for name in names:
                label = TARGETS[name]
                procedure = find_procedure(data, label)
                if procedure is None:
                    rows.append(dict(name=name, label=label, status="definition not found"))
                    continue
                start, raw = procedure
                row = dict(name=name, label=label, status="complete bounded definition",
                           file_offset=start, bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
                if output is not None:
                    output.mkdir(parents=True, exist_ok=True)
                    destination = output / (name + ".txt")
                    destination.write_text(raw.decode("utf-8", errors="replace").replace("\r", ""),
                                           encoding="utf-8")
                    row["excerpt"] = destination.name
                rows.append(row)
            return dict(input_name=path.name, snapshot_bytes=len(data), export_sha256=digest,
                        procedures=rows, warning="Decompiler output is evidence to cross-check with the binary; "
                        "it does not verify musical semantics or release compatibility.")


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
    sys.exit(main())
