"""Read the section table of a Windows executable (PE file). Stdlib only, read-only.

Why sections: a protection wrapper adds its own section to the exe. The Steam DRM wrapper
(SteamStub) adds `.bind`; that is how recon saw it on SHTA.exe without unpacking anything (D6:
recognise the protection before the payload, and route around it, never strip it).
"""

from __future__ import annotations

import struct
from pathlib import Path

# Section name -> what it indicates. A name match is strong evidence, not proof.
WRAPPERS = {
    ".bind": "Steam DRM wrapper (SteamStub)",
    "UPX0": "UPX packer",
    "UPX1": "UPX packer",
    ".themida": "Themida protector",
    ".vmp0": "VMProtect",
    ".vmp1": "VMProtect",
    ".enigma1": "Enigma protector",
}


def sections(path: Path) -> list[str] | None:
    """Section names in file order, or None if this is not a PE file."""
    try:
        with path.open("rb") as f:
            dos = f.read(64)
            if len(dos) < 64 or dos[:2] != b"MZ":
                return None
            pe_at = struct.unpack_from("<I", dos, 0x3C)[0]
            f.seek(pe_at)
            head = f.read(24)          # "PE\0\0" + 20-byte COFF header
            if len(head) < 24 or head[:4] != b"PE\0\0":
                return None
            count = struct.unpack_from("<H", head, 6)[0]
            optional_size = struct.unpack_from("<H", head, 20)[0]
            f.seek(pe_at + 24 + optional_size)
            table = f.read(40 * count)
    except OSError:
        return None
    names = []
    for i in range(count):
        raw = table[i * 40:i * 40 + 8]
        if len(raw) < 8:
            break
        names.append(raw.rstrip(b"\0").decode("ascii", "replace"))
    return names


def wrappers(names: list[str]) -> list[str]:
    """What the section names say about protection, deduplicated, in order."""
    found: list[str] = []
    for n in names:
        w = WRAPPERS.get(n)
        if w and w not in found:
            found.append(w)
    return found
