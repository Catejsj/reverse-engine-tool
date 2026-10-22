"""Trace Root's battle FX: which material/texture each particle renderer under FX_Cloud / FX_Death uses,
and export the sharedfx textures to ~/ret-work/root/extract/fx/. Read-only on Root."""
from pathlib import Path
import UnityPy

AA = Path.home() / ".local/share/Steam/steamapps/common/Root/Root_Data/StreamingAssets/aa/StandaloneWindows64"
OUT = Path.home() / "ret-work/root/extract/fx"
OUT.mkdir(parents=True, exist_ok=True)

# Load the battle bundle together with its dependencies so cross-bundle references resolve.
env = UnityPy.load(*[str(AA / n) for n in ("marquisebattle_assets_all.bundle", "sharedfx_assets_all.bundle",
                                           "sharedtextures_assets_all.bundle")])
for obj in env.objects:
    if obj.type.name == "Texture2D" and obj.assets_file.name and "sharedfx" in str(obj.assets_file.parent.name if hasattr(obj.assets_file, "parent") else ""):
        pass
    if obj.type.name == "Texture2D":
        tex = obj.read()
        try:
            tex.image.save(OUT / f"{tex.m_Name}.png")
        except Exception as e:
            print("skip", tex.m_Name, e)

def name_of(pptr):
    try:
        o = pptr.read()
        return getattr(o, "m_Name", "?"), o
    except Exception:
        return None, None

for obj in env.objects:
    if obj.type.name != "GameObject":
        continue
    go = obj.read()
    if go.m_Name not in ("FX_Cloud", "FX_Death"):
        continue
    print(f"\n## {go.m_Name}")
    todo = [go]
    while todo:
        g = todo.pop()
        for comp in g.m_Component:
            c = comp.component.read() if hasattr(comp, "component") else comp.read()
            t = c.object_reader.type.name if hasattr(c, "object_reader") else type(c).__name__
            if t == "ParticleSystemRenderer":
                for m in c.m_Materials:
                    mname, mat = name_of(m)
                    texs = []
                    if mat is not None:
                        for key, te in mat.m_SavedProperties.m_TexEnvs:
                            tn, _ = name_of(te.m_Texture)
                            if tn:
                                texs.append(f"{key}={tn}")
                    print(f"   {g.m_Name}: material {mname} -> {texs}")
            if t == "Transform":
                for ch in c.m_Children:
                    todo.append(ch.read().m_GameObject.read())
