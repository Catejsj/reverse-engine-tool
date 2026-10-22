"""Decode Unity Mecanim "generic" AnimationClips and sample a pose. Stdlib only, works on UnityPy typetrees.

Why: Root's characters are skinned meshes posed by AnimationClips stored in Mecanim's compact form
(`m_MuscleClip`), which UnityPy reads but does not decode. Root's death pose `A_Chr_Marq_dead01` is one such clip;
the Vagabond's own animations and the Ball-of-Violence arms are more. This turns a clip into per-transform
position / rotation / scale at any time.

Format (the same layout AssetStudio's converter reads):
  * Curves are numbered: streamed curves first, then dense, then constant.
  * `m_ClipBindingConstant.genericBindings` maps curve numbers to targets, in order; each binding consumes
    3 curves (attribute 1 position, 3 scale, 4 euler), 4 (attribute 2 rotation quaternion) or 1 (anything else).
    Bindings with typeID 4 are Transforms; `path` is the CRC32 of the transform's path below the animator root.
  * Streamed clip: uint32 words read as a byte stream of frames. Frame = float time, int32 key count,
    then keys of (int32 curve index, 4 float coefficients). A curve's value at time t uses the last key at or
    before t: ((c0*dt + c1)*dt + c2)*dt + c3, with dt = t - key time.
  * Dense clip: frame-major float array, `m_CurveCount` values per frame at `m_SampleRate`, linearly interpolated.
  * Constant clip: one value per curve.
"""

from __future__ import annotations

import struct
import zlib
from bisect import bisect_right

DIMENSION = {1: 3, 2: 4, 3: 3, 4: 3}
TRANSFORM = 4
RENDERER = 25                  # Unity class id of Renderer
ENABLED = 3305885265           # attribute hash of Renderer.m_Enabled (seen toggling Root's faces, gear and FX)


def path_hash(path: str) -> int:
    """Unity's binding / bone-name hash: CRC32 of the transform path, e.g. 'grp_Cat_Export/grp_Cat_Joints/jnt_root'."""
    return zlib.crc32(path.encode("utf-8"))


def decode_streamed(words: list[int]) -> dict[int, list[tuple[float, tuple[float, float, float, float]]]]:
    """Curve index -> [(key time, (c0, c1, c2, c3)), ...] sorted by time."""
    raw = struct.pack(f"<{len(words)}I", *words)
    curves: dict[int, list[tuple[float, tuple[float, float, float, float]]]] = {}
    pos = 0
    while pos + 8 <= len(raw):
        time, count = struct.unpack_from("<fi", raw, pos)
        pos += 8
        for _ in range(count):
            index = struct.unpack_from("<i", raw, pos)[0]
            coeff = struct.unpack_from("<4f", raw, pos + 4)
            pos += 20
            curves.setdefault(index, []).append((time, coeff))
    for keys in curves.values():
        keys.sort(key=lambda k: k[0])
    return curves


class Clip:
    def __init__(self, typetree: dict) -> None:
        self.name = typetree["m_Name"]
        muscle = typetree["m_MuscleClip"]
        data = muscle["m_Clip"]["data"]
        self.start, self.stop = muscle["m_StartTime"], muscle["m_StopTime"]
        self.streamed = decode_streamed(data["m_StreamedClip"]["data"])
        self.n_streamed = data["m_StreamedClip"]["curveCount"]
        dense = data["m_DenseClip"]
        self.dense = dense["m_SampleArray"]
        self.n_dense = dense["m_CurveCount"]
        self.dense_rate, self.dense_begin, self.dense_frames = dense["m_SampleRate"], dense["m_BeginTime"], dense["m_FrameCount"]
        self.constant = data["m_ConstantClip"]["data"]
        self.bindings = typetree["m_ClipBindingConstant"]["genericBindings"]
        self._times = {i: [k[0] for k in keys] for i, keys in self.streamed.items()}

    def curve(self, index: int, t: float) -> float | None:
        if index < self.n_streamed:
            keys = self.streamed.get(index)
            if not keys:
                return None
            i = max(0, bisect_right(self._times[index], t) - 1)
            time, (c0, c1, c2, c3) = keys[i]
            dt = t - time if time > float("-inf") else 0.0
            return ((c0 * dt + c1) * dt + c2) * dt + c3
        index -= self.n_streamed
        if index < self.n_dense:
            if not self.dense_frames:
                return None
            f = max(0.0, min(self.dense_frames - 1, (t - self.dense_begin) * self.dense_rate))
            a = int(f)
            b = min(a + 1, self.dense_frames - 1)
            va, vb = self.dense[a * self.n_dense + index], self.dense[b * self.n_dense + index]
            return va + (vb - va) * (f - a)
        index -= self.n_dense
        return self.constant[index] if index < len(self.constant) else None

    def renderers(self, t: float) -> dict[int, bool]:
        """path hash -> renderer enabled, for every Renderer.m_Enabled binding (faces, items, FX in Root)."""
        out: dict[int, bool] = {}
        index = 0
        for b in self.bindings:
            attr, type_id = b["attribute"], b.get("typeID")
            n = DIMENSION.get(attr, 1) if type_id == TRANSFORM else 1
            if type_id == RENDERER and attr == ENABLED:
                v = self.curve(index, t)
                if v is not None:
                    out[b["path"]] = v > 0.5
            index += n
        return out

    def pose(self, t: float) -> dict[int, dict[str, tuple[float, ...]]]:
        """path hash -> {'pos': (x,y,z), 'rot': (x,y,z,w), 'scale': (x,y,z), 'euler': (x,y,z)} for Transform bindings."""
        out: dict[int, dict[str, tuple[float, ...]]] = {}
        index = 0
        for b in self.bindings:
            attr, type_id = b["attribute"], b.get("typeID")
            n = DIMENSION.get(attr, 1) if type_id == TRANSFORM else 1
            if type_id == TRANSFORM and attr in DIMENSION:
                values = [self.curve(index + k, t) for k in range(n)]
                if all(v is not None for v in values):
                    key = {1: "pos", 2: "rot", 3: "scale", 4: "euler"}[attr]
                    out.setdefault(b["path"], {})[key] = tuple(values)  # type: ignore[arg-type]
            index += n
        return out
