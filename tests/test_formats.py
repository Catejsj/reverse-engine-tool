"""Each header reader against a tiny synthetic file, so the tests run without any game installed.

    .venv/bin/python -m unittest discover -s tests -v
"""

from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from core import recon, snapshot  # noqa: E402
from utils import pe, steam, unity, unreal  # noqa: E402


def fake_pe(path: Path, section_names: list[str]) -> None:
    """MZ header -> PE header -> empty optional header -> section table."""
    pe_at = 0x40
    dos = bytearray(pe_at)
    dos[:2] = b"MZ"
    struct.pack_into("<I", dos, 0x3C, pe_at)
    coff = struct.pack("<HHIIIHH", 0x8664, len(section_names), 0, 0, 0, 0, 0)
    table = b"".join(n.encode().ljust(8, b"\0") + bytes(32) for n in section_names)
    path.write_bytes(bytes(dos) + b"PE\0\0" + coff + table)


class TestReaders(unittest.TestCase):

    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def test_pe_sections_and_steam_wrapper(self) -> None:
        exe = self.dir / "Game.exe"
        fake_pe(exe, [".text", ".rdata", ".bind"])
        names = pe.sections(exe)
        self.assertEqual(names, [".text", ".rdata", ".bind"])
        self.assertEqual(pe.wrappers(names or []), ["Steam DRM wrapper (SteamStub)"])

    def test_not_a_pe(self) -> None:
        f = self.dir / "notes.exe"
        f.write_bytes(b"hello")
        self.assertIsNone(pe.sections(f))

    def test_unity_il2cpp(self) -> None:
        data = self.dir / "Game_Data"
        (data / "il2cpp_data" / "Metadata").mkdir(parents=True)
        (data / "globalgamemanagers").write_bytes(bytes(48) + b"2022.3.62f2\0" + bytes(64))
        (data / "il2cpp_data" / "Metadata" / "global-metadata.dat").write_bytes(
            struct.pack("<II", unity.IL2CPP_MAGIC, 31))
        (self.dir / "GameAssembly.dll").write_bytes(b"")
        fp = unity.fingerprint(self.dir)
        self.assertEqual(fp["version"], "2022.3.62f2")
        self.assertEqual(fp["backend"]["kind"], "il2cpp")
        self.assertEqual(fp["backend"]["metadata_version"], 31)

    def test_unity_obfuscated_metadata_is_flagged(self) -> None:
        data = self.dir / "Game_Data"
        (data / "il2cpp_data" / "Metadata").mkdir(parents=True)
        (data / "globalgamemanagers").write_bytes(b"")
        (data / "il2cpp_data" / "Metadata" / "global-metadata.dat").write_bytes(bytes(8))
        (self.dir / "GameAssembly.dll").write_bytes(b"")
        self.assertFalse(unity.fingerprint(self.dir)["backend"]["metadata_readable"])

    def test_pak_footer(self) -> None:
        pak = self.dir / "p.pak"
        for flag, expect in ((0, False), (1, True)):
            pak.write_bytes(bytes(100) + bytes(16) + bytes([flag])
                            + struct.pack("<II", unreal.PAK_MAGIC, 11) + bytes(60))
            info = unreal.pak_info(pak)
            self.assertEqual((info["version"], info["index_encrypted"]), (11, expect))

    def test_utoc_flags(self) -> None:
        toc = self.dir / "c.utoc"
        head = bytearray(96)
        head[:16], head[16], head[80] = unreal.UTOC_MAGIC, 3, 9
        toc.write_bytes(bytes(head))
        info = unreal.utoc_info(toc)
        self.assertEqual(info["flags"], ["compressed", "indexed"])
        self.assertFalse(info["encrypted"])

    def test_engine_hint_utf16(self) -> None:
        """SHTA.exe's version string is UTF-16; an ASCII-only search missed it."""
        exe = self.dir / "Game.exe"
        exe.write_bytes(bytes(32) + "++UE4+Release-4.27".encode("utf-16-le") + bytes(32))
        self.assertEqual(unreal.engine_hint(exe)[0], "4.27")

    def test_steam_manifest(self) -> None:
        steamapps = self.dir / "steamapps"
        game = steamapps / "common" / "Root"
        game.mkdir(parents=True)
        (steamapps / "appmanifest_965580.acf").write_text(
            '"AppState"\n{\n\t"appid"\t\t"965580"\n\t"name"\t\t"Root"\n\t"installdir"\t\t"Root"\n'
            '\t"buildid"\t\t"22238765"\n\t"AutoUpdateBehavior"\t\t"0"\n}\n')
        st = steam.fingerprint(game) or {}
        self.assertEqual((st["appid"], st["buildid"]), ("965580", "22238765"))
        self.assertEqual(st["auto_update"], "always keep updated")

    def test_anti_cheat_means_stop(self) -> None:
        (self.dir / "AntiCheatExpert").mkdir()
        (self.dir / "driver.sys").write_bytes(b"")
        self.assertEqual(recon.recon(self.dir)["verdict"]["status"], "stop")

    def test_snapshot_diff(self) -> None:
        (self.dir / "a.txt").write_text("one")
        before = snapshot.manifest(self.dir, "1")
        (self.dir / "a.txt").write_text("two")
        (self.dir / "mod.dll").write_bytes(b"x")
        d = snapshot.diff(before, snapshot.manifest(self.dir, "1"))
        self.assertEqual((d["added"], d["changed"], d["buildid_changed"]), (["mod.dll"], ["a.txt"], False))
        self.assertTrue(snapshot.diff(before, snapshot.manifest(self.dir, "2"))["buildid_changed"])


if __name__ == "__main__":
    unittest.main()
