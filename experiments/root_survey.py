"""Survey Root's bundles: which objects (by type and name) each holds. Read-only; prints only."""
import sys, re
from collections import Counter
from pathlib import Path
import UnityPy

AA = Path.home() / ".local/share/Steam/steamapps/common/Root/Root_Data/StreamingAssets/aa/StandaloneWindows64"
pattern = re.compile(sys.argv[1], re.I) if len(sys.argv) > 1 else None
for b in sys.argv[2:]:
    for f in sorted(AA.glob(f"{b}*.bundle")):
        env = UnityPy.load(str(f))
        kinds = Counter()
        hits = []
        for obj in env.objects:
            t = obj.type.name
            kinds[t] += 1
            if t in ("Texture2D", "Sprite", "AudioClip", "AnimationClip", "ParticleSystem", "GameObject", "Material"):
                try:
                    name = obj.peek_name() or ""
                except Exception:
                    name = ""
                if not pattern or pattern.search(name):
                    hits.append(f"{t}:{name}")
        print(f"\n## {f.name}  {dict(kinds.most_common(8))}")
        for h in sorted(set(hits))[:40]:
            print("  ", h)
