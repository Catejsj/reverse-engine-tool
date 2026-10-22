"""The IoStore directory-index reader, on a hand-built index and on the installed game."""

from __future__ import annotations

import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from utils import iostore  # noqa: E402

N = iostore.NONE


def fstr(s: str) -> bytes:
    raw = s.encode("latin-1") + b"\0"
    return struct.pack("<i", len(raw)) + raw


def build_index() -> bytes:
    """root -> Game -> {Fx/NS_Dust.uasset, Hero.uasset}"""
    strings = ["Game", "Fx", "NS_Dust.uasset", "Hero.uasset"]
    dirs = [(N, 1, N, N), (0, 2, N, 1), (1, N, N, 0)]       # (name, child, sibling, first file)
    files = [(2, N, 0), (3, N, 1)]                           # (name, next, user data)
    out = fstr("../../../")
    out += struct.pack("<I", len(dirs)) + b"".join(struct.pack("<4I", *d) for d in dirs)
    out += struct.pack("<I", len(files)) + b"".join(struct.pack("<3I", *f) for f in files)
    out += struct.pack("<I", len(strings)) + b"".join(fstr(s) for s in strings)
    return out


class TestIndex(unittest.TestCase):

    def test_parse_synthetic(self) -> None:
        self.assertEqual(iostore.parse_index(build_index()),
                         ["../../../Game/Fx/NS_Dust.uasset", "../../../Game/Hero.uasset"])

    def test_game_path(self) -> None:
        self.assertEqual(iostore.game_path("../../../SH3RE/Content/Effects/NS_Rain.uasset"),
                         "/Game/Effects/NS_Rain")
        self.assertEqual(iostore.game_path("../../../Engine/Content/BasicShapes/Plane.uasset"),
                         "/Engine/BasicShapes/Plane")
        self.assertEqual(iostore.game_path("../../../SH3RE/Plugins/GameActionZone/Content/NS_AZ_Dot.uasset"),
                         "/GameActionZone/NS_AZ_Dot")

    def test_sherlock_golden(self) -> None:
        paks = Path.home() / ".local/share/Steam/steamapps/common/Sherlock Holmes The Awakened/SH3RE/Content/Paks"
        if not paks.is_dir():
            self.skipTest("Sherlock not installed")
        listed = iostore.list_assets(paks)
        main = listed["pakchunk0-WindowsNoEditor.utoc"]
        if len(main) != 52477:
            self.skipTest(f"{len(main)} files, golden is 52477 for build 11165908. Re-measure (D9)")
        self.assertIn("../../../SH3RE/Content/Effects/Particles/Weather/NS_Rain.uasset", main)


if __name__ == "__main__":
    unittest.main()
