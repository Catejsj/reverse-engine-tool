"""Stage 1 — recon: what is this program? One page, before anything else runs (doctrine D1).

Reads file names, headers and footers only. Never launches, patches or unpacks anything. The
verdict follows D6 — find the protection before the payload — and the brief's rule that RET routes
around protections and never strips them:

    stop     anti-cheat is present: do not attach anything to this game
    caution  a wrapper, online components, or auto-updates: work around them, note them
    go       nothing in the way
"""

from __future__ import annotations

import os
import re
from pathlib import Path

from utils import pe, steam, unity, unreal

SCHEMA = "ret.fingerprint/1"
MAX_DEPTH = 7

# Matched against lowercased file and folder names. Evidence is a name, so each is a lead, not proof.
ONLINE = [
    (re.compile(r"^steam_api(64)?\.dll$"), "Steamworks API"),
    (re.compile(r"^eossdk"), "Epic Online Services"),
    (re.compile(r"^firebase"), "Firebase"),
    (re.compile(r"discord.*\.dll$"), "Discord SDK"),
    (re.compile(r"^appsflyer"), "AppsFlyer analytics"),
    (re.compile(r"photon.*\.dll$"), "Photon networking"),
]
ANTI_CHEAT = [
    (re.compile(r"easyanticheat"), "EasyAntiCheat"),
    (re.compile(r"battleye|^beservice"), "BattlEye"),
    (re.compile(r"anticheatexpert"), "AntiCheatExpert (ACE)"),
    (re.compile(r"xigncode"), "XIGNCODE"),
    (re.compile(r"gameguard|^npgg"), "nProtect GameGuard"),
    (re.compile(r"mhyprot"), "mhyprot"),
    (re.compile(r"^vgk"), "Riot Vanguard"),
    (re.compile(r"gameprotect"), "game protection module"),
    (re.compile(r"\.sys$"), "kernel driver shipped in the game folder"),
]
LICENCE = re.compile(r"(eula|licen[cs]e|terms).*\.(pdf|txt|rtf|html?|md)$")


def _walk(game: Path):
    """(relative path, is_dir) for everything down to MAX_DEPTH."""
    base = len(game.parts)
    for root, dirs, files in os.walk(game):
        here = Path(root)
        if len(here.parts) - base >= MAX_DEPTH:
            dirs[:] = []
        for d in dirs:
            yield (here / d).relative_to(game), True
        for f in files:
            yield (here / f).relative_to(game), False


def _match(table, name: str) -> str | None:
    for pattern, label in table:
        if pattern.search(name):
            return label
    return None


def scan(game: Path) -> dict:
    online: dict[str, list[str]] = {}
    anti: dict[str, list[str]] = {}
    licences: list[str] = []
    exes: list[dict] = []
    for rel, is_dir in _walk(game):
        name = rel.name.lower()
        label = _match(ANTI_CHEAT, name)
        if label:
            anti.setdefault(label, []).append(str(rel))
        if is_dir:
            continue
        label = _match(ONLINE, name)
        if label:
            online.setdefault(label, []).append(str(rel))
        if LICENCE.search(name):
            licences.append(str(rel))
        if name.endswith(".exe"):
            names = pe.sections(game / rel) or []
            exes.append({"path": str(rel), "sections": names, "wrappers": pe.wrappers(names)})
    return {"online": online, "anti_cheat": anti, "licence_files": sorted(licences),
            "executables": sorted(exes, key=lambda e: e["path"])}


def _engine(game: Path) -> dict:
    if unity.is_unity(game):
        return unity.fingerprint(game)
    if unreal.is_unreal(game):
        return unreal.fingerprint(game)
    return {"family": "unknown"}


def verdict(fp: dict) -> dict:
    reasons: list[str] = []
    if fp["anti_cheat"]:
        reasons.append("anti-cheat present: " + ", ".join(fp["anti_cheat"]))
        return {"status": "stop", "reasons": reasons}
    for exe in fp["executables"]:
        for w in exe["wrappers"]:
            reasons.append(f"{w} on {exe['path']}: route around it, never strip it")
    if fp["online"]:
        reasons.append("online components: " + ", ".join(fp["online"])
                       + ". Mods must stay out of online modes")
    engine = fp["engine"]
    backend = engine.get("backend", {})
    if backend.get("kind") == "il2cpp" and not backend.get("metadata_readable"):
        reasons.append("IL2CPP metadata is not in the standard format (encrypted or obfuscated)")
    for c in engine.get("paks", []) + engine.get("utocs", []):
        if c.get("index_encrypted") or c.get("encrypted"):
            reasons.append(f"{c['file']} is encrypted")
    st = fp.get("steam") or {}
    if st.get("auto_update") == steam.AUTO_UPDATE["0"]:
        reasons.append("Steam keeps this game auto-updated: results are only valid for buildid "
                       f"{st.get('buildid')} (D9)")
    return {"status": "caution" if reasons else "go", "reasons": reasons}


def recon(game: Path) -> dict:
    game = game.expanduser().resolve()
    fp: dict = {"schema": SCHEMA, "game": game.name, "path": str(game)}
    fp["steam"] = steam.fingerprint(game)
    fp["engine"] = _engine(game)
    fp.update(scan(game))
    fp["verdict"] = verdict(fp)
    return fp


def to_markdown(fp: dict) -> str:
    e, st, v = fp["engine"], fp.get("steam") or {}, fp["verdict"]
    lines = [f"# Recon — {fp['game']}", "",
             f"**Verdict: {v['status'].upper()}**", *[f"- {r}" for r in v["reasons"]], "",
             "| | |", "|---|---|",
             f"| Engine | {e.get('family')} {e.get('version') or '?'} ({e.get('version_evidence', '')}) |"]
    b = e.get("backend")
    if b:
        extra = f", metadata v{b['metadata_version']}" if b.get("metadata_version") else ""
        lines.append(f"| Backend | {b['kind']}{extra} |")
    if e.get("family") == "unreal":
        lines.append(f"| Packaging | {'IoStore + pak' if e.get('iostore') else 'pak'}; "
                     f"exe `{e.get('exe')}` |")
        for c in e.get("paks", []) + e.get("utocs", []):
            state = "encrypted" if (c.get("index_encrypted") or c.get("encrypted")) else "readable"
            ver = c.get("version", c.get("toc_version"))
            lines.append(f"| `{c['file']}` | v{ver}, {state} |")
    if st:
        lines.append(f"| Steam | app {st.get('appid')}, build {st.get('buildid')}, "
                     f"{st.get('auto_update')}, Proton prefix {st.get('proton_prefix_version')} |")
    for exe in fp["executables"]:
        if exe["wrappers"]:
            lines.append(f"| `{exe['path']}` | {', '.join(exe['wrappers'])} |")
    for label, paths in fp["online"].items():
        lines.append(f"| Online | {label}: `{paths[0]}`{' +' if len(paths) > 1 else ''} |")
    for label, paths in fp["anti_cheat"].items():
        lines.append(f"| Anti-cheat | {label}: `{paths[0]}` |")
    for lic in fp["licence_files"]:
        lines.append(f"| Licence | `{lic}`: read it before modding |")
    return "\n".join(lines) + "\n"
