"""Dump the key ParticleSystem settings under FX_Cloud / FX_Death, and list Root's AudioClips by name. Read-only."""
import re
from pathlib import Path
import UnityPy

ROOT = Path.home() / ".local/share/Steam/steamapps/common/Root/Root_Data"
AA = ROOT / "StreamingAssets/aa/StandaloneWindows64"
env = UnityPy.load(str(AA / "marquisebattle_assets_all.bundle"), str(AA / "sharedfx_assets_all.bundle"))

def curve(c):
    return {"mode": c.get("minMaxState"), "scalar": round(c.get("scalar", 0), 3),
            "min": round(c.get("minScalar", 0), 3)}

seen = set()
for obj in env.objects:
    if obj.type.name != "ParticleSystem":
        continue
    tt = obj.read_typetree()
    go = obj.read().m_GameObject.read().m_Name
    if go not in ("FX_Cloud", "FX_Death", "FX_HashMarks") or go in seen:
        continue
    seen.add(go)
    init = tt["InitialModule"]
    print(f"## {go}: duration {tt['lengthInSec']}s loop={tt['looping']}")
    print("   lifetime", curve(init["startLifetime"]), "size", curve(init["startSize"]),
          "speed", curve(init["startSpeed"]), "rotation", curve(init["startRotation"]), "max", init["maxNumParticles"])
    em = tt["EmissionModule"]
    print("   rate", curve(em["rateOverTime"]), "bursts", [(b["time"], curve(b["countCurve"])) for b in em["m_Bursts"]])
    sh = tt["ShapeModule"]
    print("   shape", sh.get("type"), "radius", round(sh.get("radius", {}).get("value", 0), 3) if isinstance(sh.get("radius"), dict) else sh.get("radius"))
    for mod in ("SizeModule", "ColorModule", "RotationModule", "UVModule"):
        print("  ", mod, "enabled" if tt[mod]["enabled"] else "off")
    if tt["UVModule"]["enabled"]:
        uv = tt["UVModule"]; print("   flipbook tiles", uv["tilesX"], "x", uv["tilesY"])

res = UnityPy.load(str(ROOT / "resources.assets"))
clips = sorted({o.peek_name() for o in res.objects if o.type.name == "AudioClip"})
print(f"\n## AudioClips in resources.assets: {len(clips)}")
for c in clips:
    if re.search(r"death|die|dead|kill|defeat|hit|battle|fight|vagabond|remove", c, re.I):
        print("  ", c)
