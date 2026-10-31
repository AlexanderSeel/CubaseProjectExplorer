"""Inspect known serializer routines in the user-supplied Cubase 15 binary.

No execution, writes, network access, or third-party modules are required for
the default evidence inventory. --disassemble optionally uses Capstone 5.
Addresses and interpretations apply only to the exact SHA-256 below.
"""
from __future__ import annotations

import argparse
import hashlib
import mmap
from pathlib import Path
import struct
import sys


EXPECTED_SHA256 = "b4a9c748537ead87981aebe18a01aae3acba8bfc76d1c1150c0054de75515aa0"
ROUTINES = {
    "chunk-header": (0x1446150B0, 0x144615157),
    "root-archive-reader": (0x144603D30, 0x144603FB3),
    "root-strings-writer": (0x144604970, 0x144604A25),
    "archive-constructor": (0x1445E9500, 0x1445E96BF),
    "archive-class-reader": (0x1445EA890, 0x1445EAD6B),
    "archive-class-writer": (0x1445EA6E0, 0x1445EA882),
    "archive-body-size": (0x1445EB010, 0x1445EB060),
    "archive-object-reader": (0x1445E9990, 0x1445E9E10),
    "string-reader": (0x1439E04A0, 0x1439E053E),
    "string-writer": (0x1439E03D0, 0x1439E0494),
    "class-registration": (0x1439C6EC0, 0x1439C6F77),
    "tempo-registration": (0x1417DE1E0, 0x1417DE224),
    "tempo-reader": (0x1431E0870, 0x1431E08DC),
    "tempo-writer": (0x1431E08F0, 0x1431E095E),
    "tempo-track-reader": (0x1431E2E00, 0x1431E30E8),
    "tempo-track-writer": (0x1431E30F0, 0x1431E32F1),
    "signature-reader": (0x1431D3B70, 0x1431D3C3A),
    "signature-writer": (0x1431D3C40, 0x1431D3CE0),
    "marker-reader": (0x1426FC400, 0x1426FC464),
    "marker-writer": (0x1426FC470, 0x1426FC4CB),
    "float32-reader": (0x1439DFDA0, 0x1439DFE00),
    "float64-reader": (0x1439DFFF0, 0x1439E006D),
    "uint32-reader": (0x1439DF440, 0x1439DF4A0),
    "uint16-reader": (0x1439DF070, 0x1439DF0C3),
}


def inspect(path: Path, selected: list[str], disassemble: bool) -> None:
    decoder = None
    if disassemble:
        try:
            import capstone
        except ImportError as error:
            raise ValueError("--disassemble requires the optional capstone module") from error
        decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)

    with path.open("rb") as source:
        with mmap.mmap(source.fileno(), 0, access=mmap.ACCESS_READ) as data:
            digest = hashlib.sha256(data).hexdigest()
            if digest != EXPECTED_SHA256:
                raise ValueError(f"Unrecognized binary SHA-256 {digest}; known addresses were not applied")
            pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
            if data[:2] != b"MZ" or data[pe_offset:pe_offset + 4] != b"PE\0\0":
                raise ValueError("Not the expected PE executable")
            section_count = struct.unpack_from("<H", data, pe_offset + 6)[0]
            optional_size = struct.unpack_from("<H", data, pe_offset + 20)[0]
            image_base = struct.unpack_from("<Q", data, pe_offset + 24 + 24)[0]
            sections = []
            for index in range(section_count):
                offset = pe_offset + 24 + optional_size + index * 40
                _, virtual_address, raw_size, raw_offset = struct.unpack_from("<IIII", data, offset + 8)
                sections.append((virtual_address, raw_size, raw_offset))

            print(f"SHA-256: {digest}\nFile bytes: {len(data)}\nImage base: 0x{image_base:x}")
            for name in selected:
                start, end = ROUTINES[name]
                rva = start - image_base
                matches = [(raw_offset + rva - address) for address, size, raw_offset in sections
                           if address <= rva and end - image_base <= address + size]
                if len(matches) != 1:
                    raise ValueError(f"Routine {name} does not map to exactly one section")
                offset = matches[0]
                code = data[offset:offset + end - start]
                print(f"\n{name}: VA 0x{start:x}..0x{end:x} (end exclusive), "
                      f"file offset 0x{offset:x}, bytes {len(code)}, SHA-256 {hashlib.sha256(code).hexdigest()}")
                if decoder:
                    for instruction in decoder.disasm(code, start):
                        print(f"  0x{instruction.address:x}: {instruction.mnemonic} {instruction.op_str}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("binary", type=Path)
    parser.add_argument("--function", action="append", choices=ROUTINES, dest="functions")
    parser.add_argument("--disassemble", action="store_true")
    arguments = parser.parse_args()
    try:
        inspect(arguments.binary, arguments.functions or list(ROUTINES), arguments.disassemble)
    except (OSError, ValueError, struct.error) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
