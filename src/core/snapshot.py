"""Snapshot a game folder before RET changes it, and diff against it after (doctrine D4).

The book's habit: revert-able VM, snapshot before running the sample, diff after. Here: a manifest
of every file, so a mod's footprint is exactly the diff and uninstalling is checkable.

Files up to HASH_LIMIT are SHA-256 hashed. Larger ones (the 22 GB .ucas) are recorded by size and
mtime: RET never edits a game's own containers, it only adds files beside them, so a size/mtime
change on one of those means Steam touched it, not us.

D9: the manifest records the Steam buildid. A diff across two buildids is reported as an update,
never as something to undo.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path

HASH_LIMIT = 256 * 1024 * 1024


def _entry(path: Path) -> dict:
    st = path.stat()
    if st.st_size <= HASH_LIMIT:
        h = hashlib.sha256()
        with path.open("rb") as f:
            for block in iter(lambda: f.read(1 << 20), b""):
                h.update(block)
        return {"size": st.st_size, "sha256": h.hexdigest()}
    return {"size": st.st_size, "mtime_ns": st.st_mtime_ns}


def manifest(game: Path, buildid: str | None) -> dict:
    files: dict[str, dict] = {}
    for root, _, names in os.walk(game):
        for n in names:
            p = Path(root) / n
            if p.is_file() and not p.is_symlink():
                files[str(p.relative_to(game))] = _entry(p)
    return {"game": game.name, "buildid": buildid, "files": dict(sorted(files.items()))}


def diff(old: dict, new: dict) -> dict:
    a, b = old["files"], new["files"]
    return {
        "buildid_changed": old.get("buildid") != new.get("buildid"),
        "added": sorted(set(b) - set(a)),
        "removed": sorted(set(a) - set(b)),
        "changed": sorted(k for k in set(a) & set(b) if a[k] != b[k]),
    }


def save(data: dict, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=1), encoding="utf-8")
