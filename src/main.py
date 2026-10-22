"""ret — The Reversal Engine. Wiring only; the logic is in core/ and utils/.

    .venv/bin/python src/main.py recon <game folder>
    .venv/bin/python src/main.py snapshot <game folder>
    .venv/bin/python src/main.py diff <game folder> <snapshot.json>
    .venv/bin/python src/main.py assets <game folder> [--grep REGEX]
    .venv/bin/python src/main.py companion          # plays sounds for a running mod

Output goes to ~/ret-work/<game>/, outside the repository: what RET learns about a game
is the publisher's material and is never committed, indexed, or published (R2, R5).
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from core import companion, recon, snapshot  # noqa: E402
from utils import iostore, steam, unreal  # noqa: E402

WORK = Path.home() / "ret-work"


def _workdir(game: Path) -> Path:
    return WORK / game.name.lower().replace(" ", "-")


def cmd_recon(game: Path) -> int:
    fp = recon.recon(game)
    out = _workdir(game) / "recon"
    out.mkdir(parents=True, exist_ok=True)
    (out / "fingerprint.json").write_text(json.dumps(fp, indent=1), encoding="utf-8")
    md = recon.to_markdown(fp)
    (out / "fingerprint.md").write_text(md, encoding="utf-8")
    print(md)
    print(f"written: {out}/fingerprint.json, fingerprint.md")
    return 2 if fp["verdict"]["status"] == "stop" else 0


def cmd_snapshot(game: Path) -> int:
    st = steam.fingerprint(game) or {}
    m = snapshot.manifest(game, st.get("buildid"))
    path = _workdir(game) / "snapshots" / f"{time.strftime('%Y%m%d-%H%M%S')}-build{st.get('buildid')}.json"
    snapshot.save(m, path)
    print(f"{len(m['files'])} files recorded -> {path}")
    return 0


def cmd_diff(game: Path, saved: Path) -> int:
    old = json.loads(saved.read_text(encoding="utf-8"))
    st = steam.fingerprint(game) or {}
    d = snapshot.diff(old, snapshot.manifest(game, st.get("buildid")))
    if d["buildid_changed"]:
        print("Steam build changed since this snapshot: this is an update, not a mod. Re-snapshot (D9).")
    for key in ("added", "removed", "changed"):
        print(f"{key}: {len(d[key])}")
        for p in d[key][:50]:
            print(f"  {p}")
    return 1 if any(d[k] for k in ("added", "removed", "changed")) else 0


def cmd_assets(game: Path, pattern: str | None) -> int:
    """Stage 2 — every asset path the game ships (Unreal IoStore), names only."""
    paks = unreal.paks_dir(game)
    if paks is None:
        print("no Unreal Content/Paks folder: only Unreal IoStore games are supported so far")
        return 1
    paths: list[str] = []
    for container, entries in iostore.list_assets(paks).items():
        print(f"{container}: {len(entries)} files")
        paths += [iostore.game_path(e) for e in entries if e.endswith(".uasset")]
    paths = sorted(set(paths))
    out = _workdir(game) / "extract" / "assets.txt"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(paths) + "\n", encoding="utf-8")
    print(f"{len(paths)} assets -> {out}")
    if pattern:
        rx = re.compile(pattern, re.IGNORECASE)
        for p in (p for p in paths if rx.search(p.rsplit("/", 1)[-1])):
            print(f"  {p}")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(prog="ret", description="The Reversal Engine")
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("recon", help="fingerprint a game (read-only)").add_argument("game", type=Path)
    sub.add_parser("snapshot", help="record every file before a change").add_argument("game", type=Path)
    d = sub.add_parser("diff", help="compare the folder with a snapshot")
    d.add_argument("game", type=Path)
    d.add_argument("snapshot", type=Path)
    s = sub.add_parser("assets", help="list every asset the game ships (Unreal IoStore)")
    s.add_argument("game", type=Path)
    s.add_argument("--grep", help="regex on the asset name")
    sub.add_parser("companion", help="play sounds for a running mod (Ctrl+C to stop)")
    a = ap.parse_args()
    if a.cmd == "companion":
        try:
            companion.run()
        except KeyboardInterrupt:
            pass
        return 0
    game = a.game.expanduser().resolve()
    if not game.is_dir():
        ap.error(f"not a folder: {game}")
    if a.cmd == "recon":
        return cmd_recon(game)
    if a.cmd == "snapshot":
        return cmd_snapshot(game)
    if a.cmd == "assets":
        return cmd_assets(game, a.grep)
    return cmd_diff(game, a.snapshot)


if __name__ == "__main__":
    sys.exit(main())
