"""Reproduce the limited static findings from a local user-owned Cubase15.exe.

Usage: python scripts/verify_cubase15_binary_evidence.py <path-to-Cubase15.exe>
No Cubase execution; Python standard library only.
"""
from __future__ import annotations

import hashlib
import mmap
from pathlib import Path
import struct
import sys

if len(sys.argv) != 2:
    sys.exit("Usage: python verify_cubase15_binary_evidence.py <path-to-Cubase15.exe>")

with Path(sys.argv[1]).open("rb") as fd:
    data = mmap.mmap(fd.fileno(), 0, access=mmap.ACCESS_READ)
    try:
        if data[:2] != b"MZ":
            raise ValueError("Not a PE file")
        peoff = struct.unpack_from("<I", data, 0x3c)[0]
        if data[peoff:peoff + 4] != b"PE\x00\x00":
            raise ValueError("Not a PE file")
        machine, sections = struct.unpack_from("<HH", data, peoff + 4)
        print("File bytes:", len(data))
        print("SHA-256:", hashlib.sha256(data).hexdigest())
        print("PE machine:", hex(machine), "sections:", sections)
        markers = {
            "RIFF comparison": bytes.fromhex("81 f9 46 46 49 52"),
            "RIF2 comparison": bytes.fromhex("81 f9 32 46 49 52"),
            "NUND immediate": bytes.fromhex("ba 44 4e 55 4e"),
            "ROOT comparison": bytes.fromhex("81 f9 54 4f 4f 52"),
            "ARCH comparison": bytes.fromhex("81 f9 48 43 52 41"),
            "UTF16 CPR registration": "cpr\0".encode("utf-16le"),
            "DeserializerFactory": b"DeserializerFactory\0",
            "VstXmlProjectReaderConverter": b"VstXmlProjectReaderConverter\0",
            "FCompressionStream": b"FCompressionStream\0",
        }
        for name, marker in markers.items():
            offset = data.find(marker)
            print(f"{name}: 0x{offset:x}" if offset >= 0 else f"{name}: not found")
    finally:
        data.close()
