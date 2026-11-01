#!/usr/bin/env python3
"""Check a Windows Setup payload, including engine offers when packed on Unix.

Usage: python3 build/check-windows-setup.py SETUP.exe
The payload and manifest hashes are checked without executing the Windows program.
"""

import hashlib
import json
from pathlib import Path
import struct
import sys


def check(path):
    data = Path(path).read_bytes()
    assert data[-72:-56] == b"AZOTH-PAYLOAD-01", "Missing unsigned Setup payload trailer"
    offset, length, manifest_length = struct.unpack_from("<qqq", data, len(data) - 56)
    assert offset > 0 and length > 0 and manifest_length > 0
    assert offset + length + manifest_length + 72 == len(data)
    manifest_bytes = data[offset + length:-72]
    assert hashlib.sha256(manifest_bytes).digest() == data[-32:], "Manifest checksum mismatch"
    manifest = json.loads(manifest_bytes)
    assert hashlib.sha256(data[offset:offset + length]).hexdigest() == manifest["compressedSha256"]
    assert sum(manifest["segments"]) == length
    pins = json.loads((Path(__file__).resolve().parents[1] /
                       "src/AZOTH.Engines/Infrastructure/engines.json").read_text())["components"]
    expected = {
        "libreoffice": ["libreoffice", "h2orestart", "jre"],
        "pandoc": ["pandoc"],
        "ghostscript": ["ghostscript", "vcruntime"],
        "ffmpeg": ["ffmpeg"],
        "calibre": ["calibre"],
        "tesseract": ["tesseract"],
    }
    offers = {item["id"]: item for item in manifest["engines"]}
    assert offers.keys() == expected.keys(), f"Incorrect Windows offers: {list(offers)}"
    for name, components in expected.items():
        offer = offers[name]
        assert offer["markerFolders"] == [c for c in components if pins[c]["kind"] != "oxt"], name
        size = sum(pins[c]["size"] for c in components)
        assert offer["downloadSize"] == size, name
        assert offer["suggested"] == (size < 60_000_000 and name not in ("ghostscript", "tesseract")), name
    print(f"Verified {manifest['version']}: {len(manifest['files'])} files, payload SHA-256, six Windows engine offers")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    check(sys.argv[1])
