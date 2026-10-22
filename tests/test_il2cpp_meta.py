"""il2cpp_meta reads a hand-built v31 metadata blob: one image, one type with one field and two methods."""

import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from utils import il2cpp_meta as M  # noqa: E402


def build() -> bytes:
    strings = b"\0Game.dll\0Spinners\0Mercy\0web\0Weave\0Spin\0"
    idx = {s: strings.index(s.encode() + b"\0") for s in ("Game.dll", "Spinners", "Mercy", "web", "Weave", "Spin")}
    header_size = 8 + 8 * 24
    tables = [(0, 0)] * 24
    body = b""

    def add(table: int, blob: bytes) -> None:
        nonlocal body
        tables[table] = (header_size + len(body), len(blob))
        body += blob

    add(M.T_STRINGS, strings)
    methods = b"".join(struct.pack("<7i4H", idx[n], 0, 0, 0, 0, -1, 0, 0, 0, 0, 0) for n in ("Weave", "Spin"))
    add(M.T_METHODS, methods)
    add(M.T_FIELDS, struct.pack("<3I", idx["web"], 0, 0))
    t = struct.pack("<IIiiiiiIii", idx["Mercy"], idx["Spinners"], 0, -1, -1, -1, -1, 0, 0, 0)
    t += struct.pack("<6i", -1, -1, -1, -1, -1, -1) + struct.pack("<8H", 2, 0, 1, 0, 0, 0, 0, 0) + struct.pack("<II", 0, 0)
    assert len(t) == M.TYPE_SIZE
    add(M.T_TYPES, t)
    add(M.T_IMAGES, struct.pack("<IiiI6i", idx["Game.dll"], 0, 0, 1, 0, 0, -1, 0, 0, 0))
    head = struct.pack("<Ii", M.MAGIC, 31) + b"".join(struct.pack("<II", *x) for x in tables)
    return head + body


class TestMetadata(unittest.TestCase):
    def test_reads_types_fields_methods(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "global-metadata.dat"
            p.write_bytes(build())
            m = M.Metadata(p)
            self.assertEqual(m.images(), [("Game.dll", 0, 1)])
            (t,) = m.types()
            self.assertEqual((t.full_name, t.image, t.fields, t.methods),
                             ("Spinners.Mercy", "Game.dll", ["web"], ["Weave", "Spin"]))

    def test_rejects_other_versions(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "m.dat"
            p.write_bytes(struct.pack("<Ii", M.MAGIC, 29) + bytes(8 * 24))
            with self.assertRaises(ValueError):
                M.Metadata(p)


if __name__ == "__main__":
    unittest.main()
