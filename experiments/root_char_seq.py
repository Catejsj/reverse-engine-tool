"""Render a Root character clip as a sequence with fixed framing (helper for the strike animation).

    .venv/bin/python experiments/root_char_seq.py <prefab> <clip> <t0> <t1> <fps> <yaw> <pitch> <outdir> <bundle>...
"""

from __future__ import annotations

import contextlib
import io
import sys
from pathlib import Path

import UnityPy

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
import root_char_render as R  # noqa: E402
from utils import mecanim  # noqa: E402


class Character:
    def __init__(self, prefab: str, bundles: list[str]) -> None:
        self.env = UnityPy.load(*[str(p) for b in [*bundles, *R.SHARED] for p in R.AA.glob(f"{b}_assets_all*.bundle")])
        root = next(o.read() for o in self.env.objects if o.type.name == "GameObject" and o.peek_name() == prefab)
        self.nodes, self.by = R.build(root)
        self.clips: dict[str, mecanim.Clip] = {}

    def clip(self, name: str) -> mecanim.Clip:
        if name not in self.clips:
            tt = next(o.read_typetree() for o in self.env.objects if o.type.name == "AnimationClip" and o.peek_name() == name)
            self.clips[name] = mecanim.Clip(tt)
        return self.clips[name]

    def triangles(self, clip_name: str, t: float):
        c = self.clip(clip_name)
        R.pose_world(self.nodes, c.pose(t))
        with contextlib.redirect_stdout(io.StringIO()):
            return R.gather(self.nodes, self.by, dead="dead" in clip_name.lower(), enabled=c.renderers(t))


def main() -> int:
    prefab, clip, t0, t1, fps, yaw, pitch, out = sys.argv[1], sys.argv[2], *map(float, sys.argv[3:8]), Path(sys.argv[8]).expanduser()
    ch = Character(prefab, sys.argv[9:])
    times = [t0 + i / fps for i in range(int((t1 - t0) * fps) + 1)]
    tris = [ch.triangles(clip, t) for t in times]
    # One framing for the whole sequence: the union of every frame's bounds.
    bounds = [R.view_bounds([(a, b, c) for a, b, c, *_ in R_proj(tr, yaw, pitch)]) for tr in tris]
    xs = [b[0] - b[2] / 2 for b in bounds] + [b[0] + b[2] / 2 for b in bounds]
    ys = [b[1] - b[2] / 2 for b in bounds] + [b[1] + b[2] / 2 for b in bounds]
    span = max(max(xs) - min(xs), max(ys) - min(ys))
    framing = ((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, span)
    out.mkdir(parents=True, exist_ok=True)
    for i, tr in enumerate(tris):
        R.render(tr, yaw, pitch, framing).save(out / f"{i:04d}.png")
    print(f"{len(times)} frames -> {out}")
    return 0


def R_proj(tris, yaw, pitch):
    """View-space triangles (same rotation as root_char_render.render) for bounds only."""
    import math
    cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))

    def view(p):
        x, y, z = p
        x, z = x * cy - z * sy, x * sy + z * cy
        y, z = y * cp - z * sp, y * sp + z * cp
        return (x, y, z)
    return [(view(a), view(b), view(c)) for a, b, c, *_ in tris]


if __name__ == "__main__":
    sys.exit(main())
