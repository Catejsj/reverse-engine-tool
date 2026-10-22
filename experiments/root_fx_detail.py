"""Full detail for re-simulating FX_Cloud / FX_Death / FX_HashMarks: curves, gradients, renderer, material. Read-only."""
import json
from pathlib import Path
import UnityPy

AA = Path.home() / ".local/share/Steam/steamapps/common/Root/Root_Data/StreamingAssets/aa/StandaloneWindows64"
env = UnityPy.load(*[str(AA / f"{n}_assets_all.bundle") for n in ("marquisebattle", "sharedfx", "shaders")])

def shader_name(mat):
    try:
        return mat.m_Shader.read().m_ParsedForm.m_Name
    except Exception:
        try:
            return mat.m_Shader.read().m_Name
        except Exception as e:
            return f"unresolved ({type(e).__name__})"
OUT = Path.home() / "ret-work/root/extract/fx/params.json"

def keys(c):
    cur = c.get("maxCurve", {}).get("m_Curve", [])
    return {"mode": c.get("minMaxState"), "scalar": round(c.get("scalar", 0), 4),
            "keys": [(round(k["time"], 3), round(k["value"], 3)) for k in cur]}

def grad(g):
    m = g.get("maxGradient", {})
    cols = [(round(m.get(f"ctime{i}", 0) / 65535, 3), [round(m[f"key{i}"][c], 3) for c in "rgb"]) for i in range(m.get("m_NumColorKeys", 0))]
    alps = [(round(m.get(f"atime{i}", 0) / 65535, 3), round(m[f"key{i}"]["a"], 3)) for i in range(m.get("m_NumAlphaKeys", 0))]
    return {"mode": g.get("minMaxState"), "color": cols, "alpha": alps}

result, seen = {}, set()
for obj in env.objects:
    if obj.type.name != "ParticleSystem":
        continue
    ps = obj.read()
    go = ps.m_GameObject.read()
    if go.m_Name not in ("FX_Cloud", "FX_Death", "FX_HashMarks") or go.m_Name in seen:
        continue
    seen.add(go.m_Name)
    tt = obj.read_typetree()
    init = tt["InitialModule"]
    r = {"duration": tt["lengthInSec"], "startDelay": keys(tt["startDelay"]),
         "lifetime": keys(init["startLifetime"]) | {"min": round(init["startLifetime"]["minScalar"], 3)},
         "size": keys(init["startSize"]) | {"min": round(init["startSize"]["minScalar"], 3)},
         "speed": keys(init["startSpeed"]), "startColor": grad(init["startColor"]),
         "gravity": keys(init["gravityModifier"]) if "gravityModifier" in init else None,
         "rate": keys(tt["EmissionModule"]["rateOverTime"]),
         "bursts": [(b["time"], keys(b["countCurve"])) for b in tt["EmissionModule"]["m_Bursts"]],
         "shape": {k: tt["ShapeModule"].get(k) for k in ("type", "radiusThickness", "angle", "arc")},
         "sizeOverLife": keys(tt["SizeModule"]["curve"]) if tt["SizeModule"]["enabled"] else None,
         "colorOverLife": grad(tt["ColorModule"]["gradient"]) if tt["ColorModule"]["enabled"] else None,
         "rotationOverLife": keys(tt["RotationModule"]["curve"]) if tt["RotationModule"]["enabled"] else None,
         "simulationSpace": init.get("simulationSpace", tt.get("moveWithTransform"))}
    # renderer + material on the same GameObject
    for comp in go.m_Component:
        c = comp.component if hasattr(comp, "component") else comp
        o = c.deref() if hasattr(c, "deref") else None
        if o is None or o.type.name != "ParticleSystemRenderer":
            continue
        rt = o.read_typetree()
        r["renderMode"] = rt.get("m_RenderMode")
        r["mesh"] = None
        mats = []
        for m in o.read().m_Materials:
            try:
                mat = m.read()
            except Exception:
                continue
            mt = mat.object_reader.read_typetree()
            sp = mt["m_SavedProperties"]
            mats.append({"name": mat.m_Name,
                         "shader": shader_name(mat),
                         "floats": {k: round(v, 3) for k, v in sp.get("m_Floats", [])},
                         "colors": {k: [round(v[c], 3) for c in "rgba"] for k, v in sp.get("m_Colors", [])},
                         "texST": {k: [v["m_Scale"], v["m_Offset"]] for k, v in sp.get("m_TexEnvs", []) if v["m_Texture"]["m_PathID"]}})
        r["materials"] = mats
        mesh_ref = rt.get("m_Mesh", {})
        if mesh_ref.get("m_PathID"):
            try:
                r["mesh"] = o.assets_file.files[mesh_ref["m_PathID"]].peek_name()
            except Exception:
                r["mesh"] = f"pathID {mesh_ref['m_PathID']}"
    result[go.m_Name] = r

OUT.write_text(json.dumps(result, indent=1))
print(json.dumps(result, indent=1)[:6000])
