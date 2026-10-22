"""Fingerprint a Unity game from its files. Stdlib only, read-only.

Everything here is a header read; no asset is decoded. The facts it reproduces for Root were first
measured by hand on 2026-10-14 (stage1-plan.md §2).
"""

from __future__ import annotations

import re
import struct
from pathlib import Path

IL2CPP_MAGIC = 0xFAB11BAF          # bytes AF 1B B1 FA on disk
_VERSION = re.compile(rb"(20\d\d\.\d+\.\d+[abfpx]\d+|[5-9]\.\d+\.\d+[abfpx]\d+)")


def data_dir(game: Path) -> Path | None:
    """The `<Name>_Data` folder that sits beside a Unity exe."""
    for d in sorted(game.glob("*_Data")):
        if (d / "globalgamemanagers").exists() or (d / "data.unity3d").exists():
            return d
    return None


def is_unity(game: Path) -> bool:
    return (game / "UnityPlayer.dll").exists() or data_dir(game) is not None


def version(data: Path) -> tuple[str | None, str]:
    """Unity version and where it was read. The serialized-file header carries it near the start."""
    for name in ("globalgamemanagers", "data.unity3d"):
        p = data / name
        if p.exists():
            with p.open("rb") as f:
                m = _VERSION.search(f.read(4096))
            if m:
                return m.group(1).decode(), f"{p.name} header"
    return None, "not found"


def backend(game: Path, data: Path) -> dict:
    """IL2CPP (native, names in global-metadata.dat) or Mono (managed .NET assemblies)."""
    meta = data / "il2cpp_data" / "Metadata" / "global-metadata.dat"
    if (game / "GameAssembly.dll").exists() and meta.exists():
        with meta.open("rb") as f:
            head = f.read(8)
        magic, ver = struct.unpack("<II", head) if len(head) == 8 else (0, 0)
        return {
            "kind": "il2cpp",
            "metadata": str(meta.relative_to(game)),
            "metadata_version": ver if magic == IL2CPP_MAGIC else None,
            # A wrong magic means the metadata was encrypted or obfuscated by the developer.
            "metadata_readable": magic == IL2CPP_MAGIC,
        }
    if (data / "Managed" / "Assembly-CSharp.dll").exists():
        return {"kind": "mono", "assemblies": str((data / "Managed").relative_to(game))}
    return {"kind": "unknown"}


def fingerprint(game: Path) -> dict:
    data = data_dir(game)
    if data is None:
        return {"family": "unity", "version": None, "version_evidence": "no _Data folder"}
    ver, evidence = version(data)
    return {
        "family": "unity",
        "version": ver,
        "version_evidence": evidence,
        "backend": backend(game, data),
        "addressables": (data / "StreamingAssets" / "aa").is_dir(),
    }
