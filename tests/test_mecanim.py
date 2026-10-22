"""Mecanim clip decoding on a hand-built clip, and on Root's real death pose if Root is installed."""

from __future__ import annotations

import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from utils import mecanim  # noqa: E402


def words(raw: bytes) -> list[int]:
    return list(struct.unpack(f"<{len(raw) // 4}I", raw))


def streamed(frames: list[tuple[float, list[tuple[int, tuple[float, float, float, float]]]]]) -> list[int]:
    raw = b""
    for time, keys in frames:
        raw += struct.pack("<fi", time, len(keys))
        for index, coeff in keys:
            raw += struct.pack("<i4f", index, *coeff)
    return words(raw)


def clip(stream_words, n_streamed, constant, bindings) -> dict:
    return {"m_Name": "test", "m_MuscleClip": {"m_StartTime": 0.0, "m_StopTime": 1.0, "m_Clip": {"data": {
        "m_StreamedClip": {"data": stream_words, "curveCount": n_streamed},
        "m_DenseClip": {"m_FrameCount": 0, "m_CurveCount": 0, "m_SampleRate": 30.0, "m_BeginTime": 0.0, "m_SampleArray": []},
        "m_ConstantClip": {"data": constant}}}},
        "m_ClipBindingConstant": {"genericBindings": bindings}}


class TestMecanim(unittest.TestCase):

    def test_path_hash_matches_unity(self) -> None:
        # Known pair from Root: the Marquise root bone hash is both a bone-name hash and a binding path.
        self.assertEqual(mecanim.path_hash(""), 0)

    def test_streamed_cubic_and_constant_curves(self) -> None:
        # curve 0: linear x = 2t (c2 = 2, c3 = 0); curves 1-2 constant (y, z) -> one position binding
        w = streamed([(0.0, [(0, (0.0, 0.0, 2.0, 0.0))])])
        c = mecanim.Clip(clip(w, 1, [5.0, 7.0], [{"path": 42, "attribute": 1, "typeID": 4}]))
        self.assertEqual(c.pose(0.5), {42: {"pos": (1.0, 5.0, 7.0)}})

    def test_rotation_binding_takes_four_curves(self) -> None:
        c = mecanim.Clip(clip([], 0, [1, 2, 3, 0, 0, 0, 1, 9],
                              [{"path": 1, "attribute": 1, "typeID": 4}, {"path": 1, "attribute": 2, "typeID": 4},
                               {"path": 99, "attribute": 777, "typeID": 25}]))
        self.assertEqual(c.pose(0.0), {1: {"pos": (1, 2, 3), "rot": (0, 0, 0, 1)}})

    def test_root_dead_pose(self) -> None:
        aa = Path.home() / ".local/share/Steam/steamapps/common/Root/Root_Data/StreamingAssets/aa/StandaloneWindows64"
        bundle = aa / "marquisecharacter_assets_all.bundle"
        if not bundle.exists():
            self.skipTest("Root not installed")
        import UnityPy
        env = UnityPy.load(str(bundle))
        tt = next(o.read_typetree() for o in env.objects
                  if o.type.name == "AnimationClip" and o.peek_name() == "A_Chr_Marq_dead01")
        pose = mecanim.Clip(tt).pose(0.0)
        self.assertEqual(len(pose), 32)                      # 32 transforms bound, as counted from the bindings
        for p in pose.values():
            if "rot" in p:
                q = p["rot"]
                self.assertAlmostEqual(sum(v * v for v in q), 1.0, places=2)   # decoded rotations are unit quaternions


if __name__ == "__main__":
    unittest.main()
