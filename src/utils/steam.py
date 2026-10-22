"""Which Steam build is this, and how will Steam change it? Stdlib only, read-only.

D9 (pin the target): every RET result is tied to (buildid, Proton version). A changed buildid
invalidates golden values and snapshots; it must never be silently "restored" over a real patch.
`AutoUpdateBehavior 0` means Steam keeps the game updated, so recon warns about it.
"""

from __future__ import annotations

import re
from pathlib import Path

_KV = re.compile(r'"(\w+)"\s+"([^"]*)"')
AUTO_UPDATE = {"0": "always keep updated", "1": "only update on launch", "2": "high priority"}


def _library(game: Path) -> Path | None:
    """The steamapps folder, if the game sits in steamapps/common/<game>."""
    if game.parent.name == "common" and game.parent.parent.name == "steamapps":
        return game.parent.parent
    return None


def _manifest(steamapps: Path, installdir: str) -> dict[str, str] | None:
    for acf in sorted(steamapps.glob("appmanifest_*.acf")):
        try:
            kv = dict(_KV.findall(acf.read_text(encoding="utf-8", errors="replace")))
        except OSError:
            continue
        if kv.get("installdir") == installdir:
            return kv
    return None


def fingerprint(game: Path) -> dict | None:
    steamapps = _library(game)
    if steamapps is None:
        return None
    kv = _manifest(steamapps, game.name)
    if kv is None:
        return {"appid": None, "note": "no appmanifest matches this folder"}
    appid = kv.get("appid")
    proton = None
    version_file = steamapps / "compatdata" / str(appid) / "version"
    if version_file.exists():
        proton = version_file.read_text(encoding="utf-8", errors="replace").strip() or None
    auto = kv.get("AutoUpdateBehavior")
    return {
        "appid": appid,
        "name": kv.get("name"),
        "buildid": kv.get("buildid"),
        "auto_update": AUTO_UPDATE.get(auto or "", auto),
        "proton_prefix_version": proton,
    }
