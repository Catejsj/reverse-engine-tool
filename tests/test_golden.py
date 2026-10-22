"""M0's golden test: recon must reproduce, by itself, what was measured by hand on 2026-10-14.

Keyed to Steam buildid (D9). If Steam has updated a game, the values below may no longer hold, so
the test SKIPS with "re-measure" rather than passing or failing on stale facts. A game that is not
installed is also skipped. Reads only; launches nothing.
"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from core import recon  # noqa: E402

COMMON = Path.home() / ".local/share/Steam/steamapps/common"


def _recon_or_skip(case: unittest.TestCase, folder: str, buildid: str) -> dict:
    game = COMMON / folder
    if not game.is_dir():
        case.skipTest(f"{folder} not installed")
    fp = recon.recon(game)
    now = (fp.get("steam") or {}).get("buildid")
    if now != buildid:
        case.skipTest(f"{folder}: buildid {now}, golden values are for {buildid}. Re-measure (D9)")
    return fp


class TestGolden(unittest.TestCase):

    def test_root(self) -> None:
        fp = _recon_or_skip(self, "Root", "22238765")
        e = fp["engine"]
        self.assertEqual((e["family"], e["version"]), ("unity", "2022.3.62f2"))
        self.assertEqual((e["backend"]["kind"], e["backend"]["metadata_version"]), ("il2cpp", 31))
        self.assertEqual([x for x in fp["executables"] if x["wrappers"]], [])
        self.assertIn("Steamworks API", fp["online"])
        self.assertEqual(fp["verdict"]["status"], "caution")

    def test_sherlock(self) -> None:
        fp = _recon_or_skip(self, "Sherlock Holmes The Awakened", "11165908")
        e = fp["engine"]
        self.assertEqual((e["family"], e["version"], e["iostore"]), ("unreal", "4.27", True))
        self.assertEqual({p["version"] for p in e["paks"]}, {11})
        self.assertFalse(any(p["index_encrypted"] for p in e["paks"]))
        self.assertFalse(any(u.get("encrypted") for u in e["utocs"]))
        shta = [x for x in fp["executables"] if x["path"].endswith("SHTA.exe")][0]
        self.assertIn("Steam DRM wrapper (SteamStub)", shta["wrappers"])
        self.assertIn("Epic Online Services", fp["online"])
        self.assertTrue(any("EULA" in p for p in fp["licence_files"]))
        self.assertEqual(fp["verdict"]["status"], "caution")

    def test_neverness_to_everness_is_stop(self) -> None:
        """The negative case: an anti-cheat game must never get a 'go'."""
        fp = _recon_or_skip(self, "Neverness to Everness", "25643250")
        self.assertEqual(fp["verdict"]["status"], "stop")
        self.assertIn("AntiCheatExpert (ACE)", fp["anti_cheat"])


if __name__ == "__main__":
    unittest.main()
