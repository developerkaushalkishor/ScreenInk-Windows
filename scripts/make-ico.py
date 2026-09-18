#!/usr/bin/env python3
"""Wrap a 256x256 PNG in a standards-compliant single-image ICO file."""

from pathlib import Path
import struct
import sys


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: make-ico.py INPUT.png OUTPUT.ico", file=sys.stderr)
        return 2

    png = Path(sys.argv[1]).read_bytes()
    if not png.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("Input must be a PNG image")

    header = struct.pack("<HHH", 0, 1, 1)
    # Width/height value 0 represents 256 pixels in the ICO directory.
    directory = struct.pack("<BBBBHHII", 0, 0, 0, 0, 1, 32, len(png), 22)
    Path(sys.argv[2]).write_bytes(header + directory + png)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
