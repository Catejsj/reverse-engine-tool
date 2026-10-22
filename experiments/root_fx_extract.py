"""Extract a Root particle effect completely: every ParticleSystem under a GameObject, with its full settings
(raw typetree), local transform, renderer, material properties, texture (PNG) and mesh (OBJ).

    .venv/bin/python experiments/root_fx_extract.py <GameObjectName> <bundle> [<bundle> ...]

Read-only on Root. Output: ~/ret-work/root/extract/fx/<GameObjectName>/ (effect.json + textures + meshes).
The renderer (root_fx_render.py) works only from that folder, never from Root's files.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import UnityPy

AA = Path.home() / ".local/share/Steam/steamapps/common/Root/Root_Data/StreamingAssets/aa/StandaloneWindows64"
# Always loaded so cross-bundle references (materials, textures, shaders, meshes) resolve.
SHARED = ("sharedfx_assets_all.bundle", "sharedtextures_assets_all.bundle", "shaders_assets_all.bundle")


def clean(x):
    """Typetree -> JSON-safe (floats rounded, bytes dropped)."""
    if isinstance(x, dict):
        return {k: clean(v) for k, v in x.items()}
    if isinstance(x, list):
        return [clean(v) for v in x]
    if isinstance(x, float):
        return round(x, 5) if x == x and abs(x) != float("inf") else None
    if isinstance(x, bytes):
        return None
    return x


def deref(pptr):
    try:
        return pptr.deref() if hasattr(pptr, "deref") else None
    except Exception:
        return None


def components(go):
    for c in go.m_Component:
        o = deref(c.component if hasattr(c, "component") else c)
        if o is not None:
            yield o


def material_info(mat, out: Path) -> dict:
    tt = mat.object_reader.read_typetree()
    props = tt["m_SavedProperties"]
    textures = {}
    for key, env in mat.m_SavedProperties.m_TexEnvs:
        if not env.m_Texture.m_PathID:
            continue
        try:
            tex = env.m_Texture.read()
            png = out / f"{tex.m_Name}.png"
            if not png.exists():
                tex.image.save(png)
            textures[key] = {"name": tex.m_Name, "scale": [env.m_Scale.x, env.m_Scale.y],
                             "offset": [env.m_Offset.x, env.m_Offset.y]}
        except Exception as e:
            textures[key] = {"error": type(e).__name__}
    try:
        shader = mat.m_Shader.read().m_ParsedForm.m_Name
    except Exception:
        shader = None
    return {"name": mat.m_Name, "shader": shader, "textures": textures,
            "floats": clean(dict(props.get("m_Floats", []))),
            "colors": clean(dict(props.get("m_Colors", [])))}


def extract(root_go, out: Path) -> list[dict]:
    systems: list[dict] = []

    def walk(go, parent: str | None, depth: int) -> None:
        tr = None
        ps = renderer = None
        for o in components(go):
            t = o.type.name
            if t in ("Transform", "RectTransform"):
                tr = o.read()
            elif t == "ParticleSystem":
                ps = o
            elif t == "ParticleSystemRenderer":
                renderer = o
        entry = {"name": go.m_Name, "parent": parent, "depth": depth, "active": bool(go.m_IsActive)}
        if tr is not None:
            p, r, s = tr.m_LocalPosition, tr.m_LocalRotation, tr.m_LocalScale
            entry["transform"] = {"pos": [p.x, p.y, p.z], "rot": [r.x, r.y, r.z, r.w], "scale": [s.x, s.y, s.z]}
        if ps is not None:
            entry["particle"] = clean(ps.read_typetree())
        if renderer is not None:
            rt = renderer.read_typetree()
            entry["renderer"] = clean({k: rt.get(k) for k in (
                "m_RenderMode", "m_RenderAlignment", "m_SortMode", "m_SortingFudge", "m_MinParticleSize",
                "m_MaxParticleSize", "m_LengthScale", "m_VelocityScale", "m_CameraVelocityScale", "m_Pivot",
                "m_Flip", "m_SortingOrder")})
            r = renderer.read()
            entry["materials"] = []
            for m in r.m_Materials:
                mat = deref(m)
                if mat is not None:
                    entry["materials"].append(material_info(mat.read(), out))
            mesh = deref(r.m_Mesh) if hasattr(r, "m_Mesh") else None
            if mesh is not None:
                m = mesh.read()
                obj = out / f"{m.m_Name}.obj"
                if not obj.exists():
                    obj.write_text(m.export())
                entry["mesh"] = m.m_Name
        systems.append(entry)
        if tr is not None:
            for ch in tr.m_Children:
                child = deref(ch)
                if child is not None:
                    walk(child.read().m_GameObject.read(), go.m_Name, depth + 1)

    walk(root_go, None, 0)
    return systems


def main() -> int:
    name, bundles = sys.argv[1], sys.argv[2:]
    env = UnityPy.load(*[str(AA / b) for b in (*bundles, *SHARED)])
    out = Path.home() / "ret-work/root/extract/fx" / name
    out.mkdir(parents=True, exist_ok=True)
    for obj in env.objects:
        if obj.type.name == "GameObject" and obj.peek_name() == name:
            systems = extract(obj.read(), out)
            (out / "effect.json").write_text(json.dumps({"effect": name, "systems": systems}, indent=1))
            for s in systems:
                mats = [m["name"] for m in s.get("materials", [])]
                print(f"{'  ' * s['depth']}{s['name']}: particle={'particle' in s} mesh={s.get('mesh')} mats={mats}")
            print(f"-> {out}")
            return 0
    print(f"{name} not found")
    return 1


if __name__ == "__main__":
    sys.exit(main())
