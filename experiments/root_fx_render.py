"""Re-simulate a Root particle effect from its extracted settings and render it to PNG frames + a preview GIF.

    .venv/bin/python experiments/root_fx_render.py BallOfViolence [seconds] [seed]

Input: ~/ret-work/root/extract/fx/<Effect>/effect.json (+ textures, meshes) from root_fx_extract.py.
Output: ~/ret-work/root/extract/fx/<Effect>/frames/####.png (RGBA) and preview.gif.

Pure Python + Pillow. What is reproduced from Root's data: emission (rate, bursts, duration, looping),
shapes (sphere, hemisphere, cone, circle), start lifetime / size / speed / rotation / colour, gravity,
limit-velocity damping, size / rotation / colour over lifetime, flipbook frames, mesh or billboard rendering,
textures and material tint. What is approximated (said here so nobody mistakes it for exact):
curves are linear between keys (Unity uses Hermite tangents); mesh puffs are flat-shaded with a
simple light and an outline instead of Root's shader; the camera is a fixed orthographic front view.
"""

from __future__ import annotations

import json
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

FX = Path.home() / "ret-work/root/extract/fx"
FPS = 30
SIZE = 512            # output pixels
SS = 2                # supersampling
UNITS = 3.6           # world units across the frame
CENTER = (0.1, 0.45)  # world point at the frame centre
LIGHT = (-0.45, 0.75, 0.48)
OUTLINE = (48, 32, 28)  # Root's _OutlineColor (0.189, 0.124, 0.109)
BUILTIN_QUAD = 10210    # Unity built-in Quad mesh path id

Vec = tuple[float, float, float]


# ---------- math ----------
def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v: Vec) -> Vec:
    x, y, z, w = q
    vx, vy, vz = v
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx))


def qaxis(axis: Vec, angle: float):
    s = math.sin(angle / 2)
    return (axis[0] * s, axis[1] * s, axis[2] * s, math.cos(angle / 2))


def norm(v: Vec) -> Vec:
    n = math.sqrt(sum(c * c for c in v)) or 1.0
    return (v[0] / n, v[1] / n, v[2] / n)


# ---------- Unity MinMaxCurve / MinMaxGradient ----------
def keys_of(curve: dict) -> list[tuple[float, float]]:
    return [(k["time"], k["value"]) for k in curve.get("m_Curve", [])]


def ev_keys(keys, t: float) -> float:
    if not keys:
        return 1.0
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            return v0 + (v1 - v0) * ((t - t0) / (t1 - t0) if t1 > t0 else 0)
    return keys[-1][1]


def minmax(c: dict | None, t: float, r: float, default: float = 0.0) -> float:
    if not c:
        return default
    m, s = c.get("minMaxState", 0), c.get("scalar", default) or 0.0
    if m == 0:
        return s
    if m == 1:
        return ev_keys(keys_of(c["maxCurve"]), t) * s
    if m == 2:
        lo, hi = ev_keys(keys_of(c["minCurve"]), t), ev_keys(keys_of(c["maxCurve"]), t)
        return (lo + (hi - lo) * r) * s
    lo = c.get("minScalar", 0.0) or 0.0
    return lo + (s - lo) * r


def ev_gradient(g: dict, t: float) -> tuple[float, float, float, float]:
    nc, na = g.get("m_NumColorKeys", 0), g.get("m_NumAlphaKeys", 0)
    ck = [(g[f"ctime{i}"] / 65535, g[f"key{i}"]) for i in range(nc)]
    ak = [(g[f"atime{i}"] / 65535, g[f"key{i}"]["a"]) for i in range(na)]
    rgb = [ev_keys([(tt, k[c]) for tt, k in ck], t) for c in "rgb"] if ck else [1.0, 1.0, 1.0]
    a = ev_keys(ak, t) if ak else 1.0
    return (rgb[0], rgb[1], rgb[2], a)


def ev_color(c: dict | None, t: float, r: float) -> tuple[float, float, float, float]:
    if not c:
        return (1.0, 1.0, 1.0, 1.0)
    m = c.get("minMaxState", 0)
    if m == 0:
        k = c["maxColor"]
        return (k["r"], k["g"], k["b"], k["a"])
    if m == 1:
        return ev_gradient(c["maxGradient"], t)
    if m == 2:
        a, b = c["minColor"], c["maxColor"]
        return tuple(a[k] + (b[k] - a[k]) * r for k in "rgba")  # type: ignore[return-value]
    if m == 3:
        a, b = ev_gradient(c["minGradient"], t), ev_gradient(c["maxGradient"], t)
        return tuple(x + (y - x) * r for x, y in zip(a, b))  # type: ignore[return-value]
    return ev_gradient(c["maxGradient"], r)


# ---------- assets ----------
def load_obj(path: Path):
    verts, uvs, faces = [], [], []
    for line in path.read_text().splitlines():
        p = line.split()
        if not p:
            continue
        if p[0] == "v":
            verts.append(tuple(float(x) for x in p[1:4]))
        elif p[0] == "vt":
            uvs.append(tuple(float(x) for x in p[1:3]))
        elif p[0] == "f":
            idx = [q.split("/") for q in p[1:]]
            vi = [int(i[0]) - 1 for i in idx]
            ti = [int(i[1]) - 1 if len(i) > 1 and i[1] else vi[0] for i in idx]
            for k in range(1, len(vi) - 1):
                faces.append(((vi[0], vi[k], vi[k + 1]), (ti[0], ti[k], ti[k + 1])))
    return verts, uvs, faces


class Texture:
    def __init__(self, path: Path) -> None:
        self.img = Image.open(path).convert("RGBA")
        px = list(self.img.convert("L").getdata())
        self.mean = (sum(px) / len(px) / 255) or 1.0

    def sample(self, u: float, v: float) -> tuple[int, int, int, int]:
        w, h = self.img.size
        return self.img.getpixel((int((u % 1) * (w - 1)), int((1 - (v % 1)) * (h - 1))))

    def tile(self, frame: int, tx: int, ty: int) -> Image.Image:
        w, h = self.img.size
        fw, fh = w // tx, h // ty
        col, row = frame % tx, frame // tx
        return self.img.crop((col * fw, row * fh, (col + 1) * fw, (row + 1) * fh))


# ---------- simulation ----------
class Particle:
    __slots__ = ("born", "life", "pos", "vel", "size", "rot", "spin", "rs", "tilt", "frame")

    def __init__(self, **kw):
        for k, v in kw.items():
            setattr(self, k, v)


def shape_sample(sh: dict, rng: random.Random) -> tuple[Vec, Vec]:
    """(position, direction) in system-local space."""
    t = sh.get("type", 0) if sh.get("enabled", True) else -1
    radius = (sh.get("radius") or {}).get("value", 1.0) if isinstance(sh.get("radius"), dict) else 1.0
    if t in (0, 2):  # sphere / hemisphere (+z)
        while True:
            d = (rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))
            if 0 < sum(c * c for c in d) <= 1:
                break
        d = norm(d)
        if t == 2 and d[2] < 0:
            d = (d[0], d[1], -d[2])
        r = radius * rng.random() ** (1 / 3)
        return (d[0] * r, d[1] * r, d[2] * r), d
    if t == 4:  # cone along +z
        a = math.radians(sh.get("angle", 25.0))
        phi, rr = rng.uniform(0, 2 * math.pi), radius * math.sqrt(rng.random())
        p = (math.cos(phi) * rr, math.sin(phi) * rr, 0.0)
        return p, norm((math.cos(phi) * math.sin(a), math.sin(phi) * math.sin(a), math.cos(a)))
    if t == 10:  # circle in XY
        phi, rr = rng.uniform(0, 2 * math.pi), radius * math.sqrt(rng.random())
        return (math.cos(phi) * rr, math.sin(phi) * rr, 0.0), (math.cos(phi), math.sin(phi), 0.0)
    return (0.0, 0.0, 0.0), (0.0, 0.0, 1.0)


class System:
    def __init__(self, s: dict, world_pos: Vec, world_rot, folder: Path, rng: random.Random) -> None:
        p = s["particle"]
        self.name, self.p, self.rng = s["name"], p, rng
        self.wpos, self.wrot = world_pos, world_rot
        self.init, self.em = p["InitialModule"], p["EmissionModule"]
        self.duration, self.loop = p["lengthInSec"], p["looping"]
        self.mode = (s.get("renderer") or {}).get("m_RenderMode", 0)
        self.mesh = load_obj(folder / f"{s['mesh']}.obj") if s.get("mesh") else None
        self.quad = self.mode == 4 and self.mesh is None and s["name"] == "FX_Bits_Loop"
        mat = (s.get("materials") or [{}])[0]
        tex = next(iter(mat.get("textures", {}).values()), {}).get("name")
        self.tex = Texture(folder / f"{tex}.png") if tex and (folder / f"{tex}.png").exists() else None
        col = (mat.get("colors") or {})
        # Legacy Unity particle shaders compute 2 * _TintColor * texture * vertex colour, so 0.5 is neutral.
        if "_TintColor" in col and (mat.get("shader") or "").find("Tuber") < 0:
            tc = col["_TintColor"]
            self.tint = tuple(min(2.0, 2 * tc[k]) for k in "rgba")
        else:
            tint = col.get("_Color") or {"r": 1, "g": 1, "b": 1, "a": 1}
            self.tint = tuple(tint[k] for k in "rgba")
        uv = p["UVModule"]
        self.uv = (uv["tilesX"], uv["tilesY"], uv) if uv.get("enabled") else None
        self.visible = self.mode != 5 and (self.mode != 4 or self.mesh is not None or self.quad)
        self.parts: list[Particle] = []
        self.emitted_rate = 0.0
        self.fired: set[tuple[int, int]] = set()

    def emit(self, n: int, t: float) -> None:
        I, rng = self.init, self.rng
        for _ in range(n):
            if len(self.parts) >= I["maxNumParticles"]:
                return
            pos, d = shape_sample(self.p["ShapeModule"], rng)
            speed = minmax(I["startSpeed"], 0, rng.random())
            rs = rng.random()
            self.parts.append(Particle(
                born=t, life=max(0.05, minmax(I["startLifetime"], 0, rng.random())),
                pos=pos, vel=(d[0] * speed, d[1] * speed, d[2] * speed),
                size=minmax(I["startSize"], 0, rng.random()),
                rot=minmax(I["startRotation"], 0, rng.random()),
                spin=rng.random(), rs=rs,
                tilt=(rng.uniform(-0.6, 0.6), rng.uniform(-0.6, 0.6)) if I.get("rotation3D") else (0.0, 0.0),
                frame=rng.random()))

    def step(self, t: float, dt: float, active: bool) -> None:
        local_t = t % self.duration if self.loop else t
        emitting = active and (self.loop or t < self.duration)
        if emitting:
            cycle = int(t // self.duration) if self.loop else 0
            for i, b in enumerate(self.em.get("m_Bursts", [])):
                if (cycle, i) not in self.fired and local_t >= b["time"]:
                    self.fired.add((cycle, i))
                    self.emit(int(round(minmax(b["countCurve"], 0, self.rng.random()))), t)
            self.emitted_rate += minmax(self.em["rateOverTime"], local_t / self.duration, self.rng.random()) * dt
            while self.emitted_rate >= 1:
                self.emitted_rate -= 1
                self.emit(1, t)
        g = minmax(self.init.get("gravityModifier"), 0, 0.5) * 9.81
        clamp = self.p.get("ClampVelocityModule", {})
        down = qrot((-self.wrot[0], -self.wrot[1], -self.wrot[2], self.wrot[3]), (0, -1, 0))
        alive = []
        for q in self.parts:
            age = (t - q.born) / q.life
            if age >= 1:
                continue
            vx, vy, vz = q.vel
            vx, vy, vz = vx + down[0] * g * dt, vy + down[1] * g * dt, vz + down[2] * g * dt
            if clamp.get("enabled"):
                limit = minmax(clamp.get("magnitude"), age, q.rs, 1.0)
                sp = math.sqrt(vx * vx + vy * vy + vz * vz)
                if sp > limit:
                    k = 1 - (1 - limit / sp) * min(1.0, clamp.get("dampen", 0.0) * dt * FPS)
                    vx, vy, vz = vx * k, vy * k, vz * k
            q.vel = (vx, vy, vz)
            q.pos = (q.pos[0] + vx * dt, q.pos[1] + vy * dt, q.pos[2] + vz * dt)
            rotm = self.p["RotationModule"]
            if rotm.get("enabled"):
                q.rot += minmax(rotm.get("curve"), age, q.spin) * dt
            alive.append(q)
        self.parts = alive

    def state(self, q: Particle, t: float):
        age = min(0.999, (t - q.born) / q.life)
        size = q.size
        sm = self.p["SizeModule"]
        if sm.get("enabled"):
            size *= minmax(sm.get("curve"), age, q.rs, 1.0)
        col = ev_color(self.init["startColor"], age, q.rs)
        cm = self.p["ColorModule"]
        if cm.get("enabled"):
            c2 = ev_color(cm.get("gradient"), age, q.rs)
            col = tuple(a * b for a, b in zip(col, c2))
        col = tuple(a * b for a, b in zip(col, self.tint))
        world = qrot(self.wrot, q.pos)
        world = (world[0] + self.wpos[0], world[1] + self.wpos[1], world[2] + self.wpos[2])
        frame = 0
        if self.uv:
            tx, ty, uv = self.uv
            n = tx * ty
            f = minmax(uv.get("startFrame"), 0, q.frame) + minmax(uv.get("frameOverTime"), age, q.frame) * uv.get("cycles", 1)
            frame = int(f * n) % n
        return world, size, col, frame


# ---------- drawing ----------
def to_px(x: float, y: float) -> tuple[float, float]:
    scale = SIZE * SS / UNITS
    return ((x - CENTER[0]) * scale + SIZE * SS / 2, (CENTER[1] - y) * scale + SIZE * SS / 2)


def draw_billboard(canvas: Image.Image, sysm: System, world: Vec, size: float, col, rot: float, frame: int) -> None:
    if sysm.tex is None or size <= 0 or col[3] <= 0.01:
        return
    tx, ty = (sysm.uv[0], sysm.uv[1]) if sysm.uv else (1, 1)
    img = sysm.tex.tile(frame, tx, ty) if sysm.uv else sysm.tex.img
    px = max(2, int(size * SIZE * SS / UNITS))
    img = img.resize((px, px), Image.BILINEAR).rotate(math.degrees(rot), expand=True, resample=Image.BILINEAR)
    r, g, b, a = img.split()
    r = r.point(lambda v, k=col[0]: int(v * k))
    g = g.point(lambda v, k=col[1]: int(v * k))
    b = b.point(lambda v, k=col[2]: int(v * k))
    a = a.point(lambda v, k=col[3]: int(v * k))
    img = Image.merge("RGBA", (r, g, b, a))
    cx, cy = to_px(world[0], world[1])
    canvas.alpha_composite(img, (int(cx - img.width / 2), int(cy - img.height / 2)))


def draw_mesh(canvas: Image.Image, sysm: System, world: Vec, size: float, col, q: Particle) -> None:
    if sysm.mesh is None or size <= 0 or col[3] <= 0.01:
        return
    verts, uvs, faces = sysm.mesh
    orient = qmul(sysm.wrot, qmul(qaxis((0, 0, 1), q.rot), qmul(qaxis((1, 0, 0), q.tilt[0]), qaxis((0, 1, 0), q.tilt[1]))))
    pts = []
    for v in verts:
        w = qrot(orient, (v[0] * size, v[1] * size, v[2] * size))
        pts.append((w[0] + world[0], w[1] + world[1], w[2] + world[2]))
    xs = [to_px(p[0], p[1]) for p in pts]
    minx, miny = min(p[0] for p in xs) - 8, min(p[1] for p in xs) - 8
    maxx, maxy = max(p[0] for p in xs) + 8, max(p[1] for p in xs) + 8
    w, h = int(maxx - minx) + 1, int(maxy - miny) + 1
    if w < 2 or h < 2:
        return
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    L = norm(LIGHT)
    tris = []
    for (a, b, c), (ta, tb, tc) in faces:
        pa, pb, pc = pts[a], pts[b], pts[c]
        n = norm((((pb[1] - pa[1]) * (pc[2] - pa[2]) - (pb[2] - pa[2]) * (pc[1] - pa[1])),
                  ((pb[2] - pa[2]) * (pc[0] - pa[0]) - (pb[0] - pa[0]) * (pc[2] - pa[2])),
                  ((pb[0] - pa[0]) * (pc[1] - pa[1]) - (pb[1] - pa[1]) * (pc[0] - pa[0]))))
        # Back faces are drawn too (as the shaded inside): G_Cloudsphere has an opening that Root's
        # camera never faces, and culling them showed it as a hole in the first preview.
        depth = (pa[2] + pb[2] + pc[2]) / 3
        tris.append((depth, (a, b, c), (ta, tb, tc), n if n[2] > 0 else (-n[0] * 0.5, -n[1] * 0.5, -n[2])))
    tris.sort(key=lambda x: x[0])
    for _, (a, b, c), (ta, tb, tc), n in tris:
        ndl = max(0.0, n[0] * L[0] + n[1] * L[1] + n[2] * L[2])
        # two-tone toon band, like Root's _LightThreshold 0.3 / _LightSmoothness 0.49
        band = 1.0 if ndl > 0.42 else (0.9 if ndl > 0.12 else 0.82)
        shade = band
        if sysm.tex is not None and uvs:
            u = (uvs[ta][0] + uvs[tb][0] + uvs[tc][0]) / 3
            v = (uvs[ta][1] + uvs[tb][1] + uvs[tc][1]) / 3
            s = sysm.tex.sample(u, v)
            shade *= 0.9 + 0.1 * ((s[0] + s[1] + s[2]) / 765 / sysm.tex.mean)
        rgb = tuple(int(min(255, 255 * col[i] * shade)) for i in range(3))
        poly = [(xs[i][0] - minx, xs[i][1] - miny) for i in (a, b, c)]
        d.polygon(poly, fill=rgb + (255,))
    layer.putalpha(layer.getchannel("A").point(lambda v, k=col[3]: int(v * k)))
    canvas.alpha_composite(layer, (int(minx), int(miny)))


def outlined(layer: Image.Image, width: int = 7) -> Image.Image:
    """One outline around the whole cloud silhouette, Root's _OutlineColor, behind the puffs."""
    mask = layer.getchannel("A").point(lambda v: 255 if v > 90 else 0).filter(ImageFilter.MaxFilter(width))
    out = Image.new("RGBA", layer.size, OUTLINE + (0,))
    out.putalpha(mask)
    out.alpha_composite(layer)
    return out


# ---------- scene ----------
def world_transforms(systems: list[dict]) -> dict[str, tuple[Vec, tuple]]:
    by = {s["name"]: s for s in systems}
    cache: dict[str, tuple[Vec, tuple]] = {}

    def get(name: str):
        if name in cache:
            return cache[name]
        s = by[name]
        tr = s.get("transform", {"pos": [0, 0, 0], "rot": [0, 0, 0, 1]})
        lp, lr = tuple(tr["pos"]), tuple(tr["rot"])
        if s["depth"] == 0 or not s.get("parent") or s["parent"] not in by:
            res = ((0.0, 0.0, 0.0), lr)
        else:
            pp, pr = get(s["parent"])
            off = qrot(pr, lp)
            res = ((pp[0] + off[0], pp[1] + off[1], pp[2] + off[2]), qmul(pr, lr))
        cache[name] = res
        return res

    for s in systems:
        get(s["name"])
    return cache


def main() -> int:
    effect = sys.argv[1]
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 2.5
    seed = int(sys.argv[3]) if len(sys.argv) > 3 else 7
    folder = FX / effect
    data = json.loads((folder / "effect.json").read_text())
    rng = random.Random(seed)
    wt = world_transforms(data["systems"])
    systems = [System(s, wt[s["name"]][0], wt[s["name"]][1], folder, rng)
               for s in data["systems"] if s.get("particle") and s.get("active", True)]
    for s in systems:
        print(f"{s.name:24s} mode={s.mode} visible={s.visible} mesh={'yes' if s.mesh else ('quad' if s.quad else '-')} "
              f"tex={'yes' if s.tex else '-'} loop={s.loop}")
    out = folder / "frames"
    out.mkdir(exist_ok=True)
    for old in out.glob("*.png"):
        old.unlink()
    frames: list[Image.Image] = []
    sub = 4
    dt = 1 / (FPS * sub)
    total = int(seconds * FPS)
    t = 0.0
    for f in range(total):
        for _ in range(sub):
            for s in systems:
                s.step(t, dt, active=True)
            t += dt
        canvas = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
        draws = []
        for s in systems:
            if not s.visible:
                continue
            for q in s.parts:
                world, size, col, frame = s.state(q, t)
                draws.append((world[2], s, q, world, size, col, frame))
        draws.sort(key=lambda d: d[0])
        cloud = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        front = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        for _, s, q, world, size, col, frame in draws:
            if s.mesh is not None:
                draw_mesh(cloud, s, world, size, col, q)
            elif "Dust" in s.name:
                draw_billboard(canvas, s, world, size, col, q.rot, frame)
            else:
                draw_billboard(front, s, world, size, col, q.rot, frame)
        canvas.alpha_composite(outlined(cloud))
        canvas.alpha_composite(front)
        img = canvas.resize((SIZE, SIZE), Image.LANCZOS)
        img.save(out / f"{f:04d}.png")
        frames.append(img)
    # Transparent preview (the frames themselves are RGBA; GIF keeps 1-bit transparency).
    frames[0].save(folder / "preview.gif", save_all=True, append_images=frames[1:],
                   duration=int(1000 / FPS), loop=0, disposal=2)
    (out / "manifest.json").write_text(json.dumps(
        {"effect": effect, "fps": FPS, "count": total, "size": SIZE, "units": UNITS, "center": CENTER,
         "seed": seed, "note": "frame 0000 = effect start; cloud fully opaque by ~frame 6"}, indent=1))
    print(f"{total} frames -> {out}, preview -> {folder / 'preview.gif'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
