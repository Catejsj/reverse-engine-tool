"""Fingerprint an Unreal Engine game from its package files. Stdlib only, read-only.

Reads only container headers and footers: whether a .pak index or an IoStore (.utoc) container is
encrypted decides whether its data can be read at all. Nothing is extracted. Facts for Sherlock Holmes:
The Awakened were measured by hand on 2026-10-14 (stage1-plan.md §2, §9).
"""

from __future__ import annotations

import mmap
import re
import struct
from pathlib import Path

PAK_MAGIC = 0x5A6F12E1
UTOC_MAGIC = b"-==--==--==--==-"
_UTOC_FLAGS = {1: "compressed", 2: "encrypted", 4: "signed", 8: "indexed"}
# Version strings left in a shipping exe, strongest first. All are indirect evidence. Unreal stores
# most of its own text as UTF-16, so each pattern is also tried in that encoding: on SHTA.exe the
# only hit ("Requires UE4.27.1 ...") is UTF-16, and an ASCII-only search found nothing.
_ENGINE_HINTS = [
    re.compile(rb"\+\+UE([45])\+Release-(\d\.\d+)"),
    re.compile(rb"UE([45])\.(\d+)\.\d+"),
    re.compile(rb"\+\x00\+\x00U\x00E\x00([45])\x00\+\x00R\x00e\x00l\x00e\x00a\x00s\x00e\x00-\x00"
               rb"\d\x00\.\x00((?:\d\x00){1,2})"),
    re.compile(rb"U\x00E\x00([45])\x00\.\x00((?:\d\x00){1,2})\.\x00\d\x00"),
]


def paks_dir(game: Path) -> Path | None:
    """<Project>/Content/Paks, up to four folders down (launchers nest it, e.g. Client/WindowsNoEditor/)."""
    for pattern in ("*/Content/Paks", "*/*/Content/Paks", "*/*/*/Content/Paks", "*/*/*/*/Content/Paks"):
        for d in sorted(game.glob(pattern)):
            if d.parent.parent.name != "Engine":
                return d
    return None


def is_unreal(game: Path) -> bool:
    return paks_dir(game) is not None


def pak_info(path: Path) -> dict:
    """Pak format version and whether its index is encrypted (the byte before the footer magic)."""
    size = path.stat().st_size
    with path.open("rb") as f:
        f.seek(max(0, size - 0x400))
        tail = f.read()
    at = tail.rfind(struct.pack("<I", PAK_MAGIC))
    if at < 1:
        return {"file": path.name, "readable_header": False}
    return {
        "file": path.name,
        "readable_header": True,
        "version": struct.unpack_from("<I", tail, at + 4)[0],
        "index_encrypted": tail[at - 1] != 0,
    }


def utoc_info(path: Path) -> dict:
    """IoStore table of contents: version and container flags (offset 80 in toc versions 2-3)."""
    with path.open("rb") as f:
        head = f.read(96)
    if head[:16] != UTOC_MAGIC or len(head) < 81:
        return {"file": path.name, "readable_header": False}
    toc_version = head[16]
    if toc_version not in (2, 3):
        return {"file": path.name, "readable_header": True, "toc_version": toc_version,
                "flags": None, "note": "header layout not verified for this toc version"}
    flags = head[80]
    return {
        "file": path.name,
        "readable_header": True,
        "toc_version": toc_version,
        "flags": [name for bit, name in _UTOC_FLAGS.items() if flags & bit],
        "encrypted": bool(flags & 2),
    }


def main_exe(project: Path) -> Path | None:
    """The game exe: the largest .exe in <Project>/Binaries/Win64, ignoring crash reporters."""
    exes = [p for p in project.glob("Binaries/Win64/*.exe") if "crash" not in p.name.lower()]
    return max(exes, key=lambda p: p.stat().st_size) if exes else None


def engine_hint(exe: Path | None) -> tuple[str | None, str]:
    """Engine version from strings in the exe. Indirect: a plugin can name a version too."""
    if exe is None:
        return None, "no game exe found"
    with exe.open("rb") as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as m:
        for pattern in _ENGINE_HINTS:
            hit = pattern.search(m)
            if hit:
                major = hit.group(1).decode()
                minor = hit.group(2).replace(b"\x00", b"").decode().split(".")[-1]
                found = bytes(m[hit.start():hit.end()]).replace(b"\x00", b"").decode("ascii", "replace")
                return f"{major}.{minor}", f"indirect: `{found}` in {exe.name}"
    return None, f"no version string in {exe.name}"


def fingerprint(game: Path) -> dict:
    paks = paks_dir(game)
    if paks is None:
        return {"family": "unreal", "version": None, "version_evidence": "no Content/Paks"}
    pak_files = sorted(paks.glob("*.pak"))
    utocs = sorted(paks.glob("*.utoc"))
    names = [p.name for p in pak_files + utocs]
    exe = main_exe(paks.parent.parent)
    ver, evidence = engine_hint(exe)
    major = ver.split(".")[0] if ver else ("4" if any("WindowsNoEditor" in n for n in names) else None)
    return {
        "family": "unreal",
        "major": major,
        "version": ver,
        "version_evidence": evidence,
        "project": paks.parent.parent.name,
        "exe": str(exe.relative_to(game)) if exe else None,
        "iostore": bool(utocs),
        "paks": [pak_info(p) for p in pak_files],
        "utocs": [utoc_info(p) for p in utocs],
    }
