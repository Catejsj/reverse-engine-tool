"""Render a Root character prefab in a pose from one of its AnimationClips, to a transparent PNG.

    .venv/bin/python experiments/root_char_render.py <prefab> <clip> <time> <out.png> [yaw] [pitch] <bundle>...

Example (the Marquise cat, dead):
    ... P_Chr_Marq_Battle_GameOver A_Chr_Marq_dead01 0 ~/ret-work/root/extract/bodies/marquise_dead.png 200 30 \
        gameoverunits marquisecharacter marquise marquisebattle

Read-only on Root. Pipeline: prefab hierarchy -> clip pose (utils/mecanim.py) -> world matrices -> linear-blend
skinning for SkinnedMeshRenderers, rigid placement for MeshFilters -> orthographic camera -> flat toon shading with
the material texture sampled per triangle -> one outline round the silhouette (Root's MK/Toon look).
Approximations: per-triangle texture sampling (no per-pixel UVs), fixed light, orthographic camera.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

import UnityPy
from PIL import Image, ImageDraw, ImageFilter
from UnityPy.helpers.MeshHelper import MeshHandler

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from utils import mecanim  # noqa: E402

AA = Path.home() / ".local/share/Steam/steamapps/common/Root/Root_Data/StreamingAssets/aa/StandaloneWindows64"
SHARED = ("sharedtextures", "shaders", "sharedfx")
SIZE, SS = 512, 2
OUTLINE = (48, 32, 28)

Mat = list[list[float]]


# ---------- matrices ----------
def trs(p, q, s) -> Mat:
    x, y, z, w = q
    xx, yy, zz, xy, xz, yz, wx, wy, wz = x * x, y * y, z * z, x * y, x * z, y * z, w * x, w * y, w * z
    r = [[1 - 2 * (yy + zz), 2 * (xy - wz), 2 * (xz + wy)],
         [2 * (xy + wz), 1 - 2 * (xx + zz), 2 * (yz - wx)],
         [2 * (xz - wy), 2 * (yz + wx), 1 - 2 * (xx + yy)]]
    return [[r[i][0] * s[0], r[i][1] * s[1], r[i][2] * s[2], p[i]] for i in range(3)] + [[0, 0, 0, 1]]


def mul(a: Mat, b: Mat) -> Mat:
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def apply(m: Mat, v, w: float = 1.0):
    return (m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2] + m[0][3] * w,
            m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2] + m[1][3] * w,
            m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2] + m[2][3] * w)


def matrix4(m) -> Mat:
    return [[getattr(m, f"e{i}{j}") for j in range(4)] for i in range(4)]


def norm(v):
    n = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]) or 1.0
    return (v[0] / n, v[1] / n, v[2] / n)


# ---------- scene ----------
class Node:
    def __init__(self, go, tr, path: str, parent: "Node | None") -> None:
        self.go, self.tr, self.path, self.parent = go, tr, path, parent
        p, q, s = tr.m_LocalPosition, tr.m_LocalRotation, tr.m_LocalScale
        self.pos, self.rot, self.scale = (p.x, p.y, p.z), (q.x, q.y, q.z, q.w), (s.x, s.y, s.z)
        self.active = bool(go.m_IsActive) and (parent.active if parent else True)
        self.world: Mat = []


def components(go):
    for c in go.m_Component:
        p = c.component if hasattr(c, "component") else c
        try:
            yield p.deref()
        except Exception:
            continue


def build(root_go) -> tuple[list[Node], dict[int, Node]]:
    nodes: list[Node] = []
    by_tr: dict[int, Node] = {}

    def walk(go, path: str, parent: Node | None) -> None:
        tr_obj = next(c for c in components(go) if c.type.name in ("Transform", "RectTransform"))
        tr = tr_obj.read()
        node = Node(go, tr, path, parent)
        nodes.append(node)
        by_tr[tr_obj.path_id] = node
        for ch in tr.m_Children:
            try:
                cgo = ch.read().m_GameObject.read()
            except Exception:
                continue
            walk(cgo, f"{path}/{cgo.m_Name}" if path else cgo.m_Name, node)

    walk(root_go, "", None)
    return nodes, by_tr


def pose_world(nodes: list[Node], pose: dict) -> None:
    for n in nodes:
        p = pose.get(mecanim.path_hash(n.path), {}) if n.path else {}
        local = trs(p.get("pos", n.pos), p.get("rot", n.rot), p.get("scale", n.scale))
        n.world = mul(n.parent.world, local) if n.parent else local


def texture_of(material) -> Image.Image | None:
    try:
        for key, env in material.m_SavedProperties.m_TexEnvs:
            if key == "_MainTex" and env.m_Texture.m_PathID:
                return env.m_Texture.read().image.convert("RGBA")
    except Exception:
        return None
    return None


# Barycentric points inside a triangle: centroid, three inner points and three edge midpoints.
_BARY = [(1 / 3, 1 / 3, 1 / 3), (0.6, 0.2, 0.2), (0.2, 0.6, 0.2), (0.2, 0.2, 0.6),
         (0.5, 0.5, 0.0), (0.0, 0.5, 0.5), (0.5, 0.0, 0.5)]


def sample_triangle(tex: Image.Image, ua, ub, uc) -> tuple[int, int, int, int]:
    """Average of several texels inside the triangle, skipping transparent atlas gaps (one centroid sample
    landed on gaps and painted whole triangles black in the first render)."""
    acc, n = [0, 0, 0], 0
    for wa, wb, wc in _BARY:
        u = wa * ua[0] + wb * ub[0] + wc * uc[0]
        v = wa * ua[1] + wb * ub[1] + wc * uc[1]
        px = tex.getpixel((int((u % 1) * (tex.width - 1)), int((1 - v % 1) * (tex.height - 1))))
        if px[3] < 128:
            continue
        acc = [acc[k] + px[k] for k in range(3)]
        n += 1
    if n == 0:
        return (0, 0, 0, 0)
    return (acc[0] // n, acc[1] // n, acc[2] // n, 255)


# UV shift that swaps a face part to another cell of its sprite sheet. Measured on T_Chr_Marq_Face1 (512 px):
# default round eyes at px (14, 26), dead X-eyes at px (185, 116) -> du = 171/512, dv = -90/512.
DEAD_FACE = {"msh_eyes": (171 / 512, -90 / 512)}


HIDDEN_UNLESS_ANIMATED = ("StarsGroup",)   # stun stars: on in the prefab, switched on only by injured clips


def gather(nodes: list[Node], by_tr: dict[int, Node], dead: bool = False, enabled: dict[int, bool] | None = None):
    """Triangles in world space: (v0, v1, v2, normal, colour, detail). detail = (texture, uv triple) for small rigid
    parts (eyes, mouth) that need real texture mapping, else None."""
    tris = []
    enabled = enabled or {}
    for n in nodes:
        if not n.active:
            continue
        h_path = mecanim.path_hash(n.path)
        if h_path in enabled and not enabled[h_path]:
            continue
        if h_path not in enabled and any(k in n.path for k in HIDDEN_UNLESS_ANIMATED):
            continue
        for c in components(n.go):
            t = c.type.name
            if t not in ("SkinnedMeshRenderer", "MeshFilter"):
                continue
            try:
                if t == "SkinnedMeshRenderer":
                    r = c.read()
                    mesh, mats = r.m_Mesh.read(), r.m_Materials
                    if not r.m_Enabled:
                        continue
                else:
                    mesh = c.read().m_Mesh.read()
                    rend = next((x for x in components(n.go) if x.type.name == "MeshRenderer"), None)
                    if rend is None or not rend.read().m_Enabled:
                        continue
                    mats = rend.read().m_Materials
            except Exception:
                continue
            h = MeshHandler(mesh)
            h.process()
            verts, normals, uvs = h.m_Vertices, h.m_Normals or [], h.m_UV0 or []
            if t == "SkinnedMeshRenderer":
                bones = [by_tr.get(b.m_PathID) for b in r.m_Bones]
                skin = [mul(b.world, matrix4(bp)) if b else None for b, bp in zip(bones, mesh.m_BindPose)]
                weights, indices = h.m_BoneWeights, h.m_BoneIndices
                world_v, world_n = [], []
                for i, v in enumerate(verts):
                    acc, accn = [0.0, 0.0, 0.0], [0.0, 0.0, 0.0]
                    for w, bi in zip(weights[i], indices[i]):
                        if w <= 0 or bi >= len(skin) or skin[bi] is None:
                            continue
                        p = apply(skin[bi], v)
                        acc = [acc[k] + w * p[k] for k in range(3)]
                        if normals:
                            q = apply(skin[bi], normals[i], 0.0)
                            accn = [accn[k] + w * q[k] for k in range(3)]
                    world_v.append(tuple(acc))
                    world_n.append(norm(accn) if normals else (0.0, 1.0, 0.0))
            else:
                world_v = [apply(n.world, v) for v in verts]
                world_n = [norm(apply(n.world, nn, 0.0)) for nn in normals] if normals else [(0.0, 1.0, 0.0)] * len(verts)
            print(f"  part {n.path.split('/')[-1] or n.go.m_Name}: {t}, {len(verts)} verts")
            part = n.path.split("/")[-1] or n.go.m_Name
            du, dv = DEAD_FACE.get(part, (0.0, 0.0)) if dead else (0.0, 0.0)
            detailed = t == "MeshFilter"
            for sub, tri_list in enumerate(h.get_triangles()):
                mat = mats[min(sub, len(mats) - 1)].read() if mats else None
                tex = texture_of(mat) if mat else None
                for a, b, cc in tri_list:
                    detail = None
                    if tex is not None and uvs and detailed:
                        col = (255, 255, 255, 255)
                        detail = (tex, tuple((uvs[i][0] + du, uvs[i][1] + dv) for i in (a, b, cc)))
                    elif tex is not None and uvs:
                        col = sample_triangle(tex, uvs[a], uvs[b], uvs[cc])
                    else:
                        col = (220, 210, 200, 255)
                    nrm = norm(tuple(world_n[a][k] + world_n[b][k] + world_n[cc][k] for k in range(3)))
                    tris.append((world_v[a], world_v[b], world_v[cc], nrm, col, detail))
    return tris


def view_bounds(proj) -> tuple[float, float, float]:
    xs = [p[k][0] for p in proj for k in range(3)]
    ys = [p[k][1] for p in proj for k in range(3)]
    return ((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, max(max(xs) - min(xs), max(ys) - min(ys)) * 1.15)


def textured_triangle(img: Image.Image, pts, tex: Image.Image, uv, band: float) -> None:
    """Affine-map the texture onto one screen triangle (Pillow AFFINE maps output pixel -> input pixel)."""
    (x0, y0), (x1, y1), (x2, y2) = pts
    tx = [(u % 1) * tex.width for u, _ in uv]
    ty = [(1 - (v % 1)) * tex.height for _, v in uv]
    det = x0 * (y1 - y2) + x1 * (y2 - y0) + x2 * (y0 - y1)
    if abs(det) < 1e-6:
        return

    def solve(t0, t1, t2):
        a = (t0 * (y1 - y2) + t1 * (y2 - y0) + t2 * (y0 - y1)) / det
        b = (t0 * (x2 - x1) + t1 * (x0 - x2) + t2 * (x1 - x0)) / det
        c = (t0 * (x1 * y2 - x2 * y1) + t1 * (x2 * y0 - x0 * y2) + t2 * (x0 * y1 - x1 * y0)) / det
        return a, b, c

    a, b, c = solve(*tx)
    d_, e, f = solve(*ty)
    bx, by = int(min(x0, x1, x2)), int(min(y0, y1, y2))
    w, h = int(max(x0, x1, x2)) - bx + 2, int(max(y0, y1, y2)) - by + 2
    if w < 1 or h < 1:
        return
    patch = tex.transform((w, h), Image.Transform.AFFINE, (a, b, a * bx + b * by + c, d_, e, d_ * bx + e * by + f),
                          resample=Image.Resampling.BILINEAR)
    if band != 1.0:
        r, g, bl, al = patch.split()
        r, g, bl = (ch.point(lambda v, k=band: int(v * k)) for ch in (r, g, bl))
        patch = Image.merge("RGBA", (r, g, bl, al))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).polygon([(x - bx, y - by) for x, y in pts], fill=255)
    patch.putalpha(Image.composite(patch.getchannel("A"), Image.new("L", (w, h), 0), mask))
    img.alpha_composite(patch, (bx, by))


def render(tris, yaw: float, pitch: float, framing: tuple[float, float, float] | None = None,
           offset: tuple[float, float] = (0.0, 0.0)) -> Image.Image:
    """framing = (centre x, centre y, span) in view space; fixed across a sequence so frames line up.
    offset shifts the model in view space (used to move a character along a jump arc)."""
    cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))

    def view(p):
        x, y, z = p
        x, z = x * cy - z * sy, x * sy + z * cy      # yaw about Y
        y, z = y * cp - z * sp, y * sp + z * cp      # pitch about X
        return (x, y, z)

    proj = []
    for v0, v1, v2, n, col, detail in tris:
        a, b, c, nv = view(v0), view(v1), view(v2), view(n)
        # No back-face culling: averaged vertex normals misjudge faces at creases (holes in the first render);
        # back-to-front painting below hides back faces instead.
        proj.append((a, b, c, nv, col, detail))
    if offset != (0.0, 0.0):
        proj = [tuple((v[0] + offset[0], v[1] + offset[1], v[2]) for v in p[:3]) + p[3:] for p in proj]
    if framing is None:
        framing = view_bounds(proj)
    cx, cyy, span = framing
    scale = SIZE * SS / span
    img = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    light = norm((-0.4, 0.8, -0.45))
    bias = span * 0.03      # face parts sit on the head surface; pull them a little toward the camera

    def depth(p):
        return (p[0][2] + p[1][2] + p[2][2]) / 3 - (bias if p[5] else 0.0)

    for a, b, c, nv, col, detail in sorted(proj, key=lambda p: -depth(p)):
        ndl = nv[0] * light[0] + nv[1] * light[1] - nv[2] * abs(light[2])
        band = 1.0 if ndl > 0.25 else 0.8         # two-tone toon band
        if col[3] == 0:
            continue
        rgb = tuple(int(col[k] * band) for k in range(3))
        pts = [((p[0] - cx) * scale + SIZE * SS / 2, (cyy - p[1]) * scale + SIZE * SS / 2) for p in (a, b, c)]
        if detail:
            textured_triangle(img, pts, detail[0], detail[1], band)
        else:
            d.polygon(pts, fill=rgb + (255,))
    mask = img.getchannel("A").point(lambda v: 255 if v > 90 else 0).filter(ImageFilter.MaxFilter(7))
    out = Image.new("RGBA", img.size, OUTLINE + (0,))
    out.putalpha(mask)
    out.alpha_composite(img)
    return out.resize((SIZE, SIZE), Image.Resampling.LANCZOS)


def main() -> int:
    prefab, clip_name, t, out = sys.argv[1], sys.argv[2], float(sys.argv[3]), Path(sys.argv[4]).expanduser()
    yaw, pitch = float(sys.argv[5]), float(sys.argv[6])
    bundles = [*sys.argv[7:], *SHARED]
    env = UnityPy.load(*[str(p) for b in bundles for p in AA.glob(f"{b}_assets_all*.bundle")])
    root = next(o.read() for o in env.objects if o.type.name == "GameObject" and o.peek_name() == prefab)
    clip_tt = next(o.read_typetree() for o in env.objects if o.type.name == "AnimationClip" and o.peek_name() == clip_name)
    nodes, by_tr = build(root)
    pose = mecanim.Clip(clip_tt).pose(t)
    hit = sum(1 for n in nodes if n.path and mecanim.path_hash(n.path) in pose)
    print(f"{len(nodes)} transforms, clip binds {len(pose)}, matched {hit}")
    pose_world(nodes, pose)
    clip = mecanim.Clip(clip_tt)
    tris = gather(nodes, by_tr, dead="dead" in clip_name.lower(), enabled=clip.renderers(t))
    print(f"{len(tris)} triangles")
    out.parent.mkdir(parents=True, exist_ok=True)
    render(tris, yaw, pitch).save(out)
    print(f"-> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
