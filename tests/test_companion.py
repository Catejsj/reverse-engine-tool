"""The companion's event handling, with a fake player so no sound plays during tests."""

from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from core import companion  # noqa: E402


class TestCompanion(unittest.TestCase):

    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        self.wav = self.dir / "boom.wav"
        self.wav.write_bytes(b"RIFF")
        self.played: list[Path] = []

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def event(self, body: str) -> Path:
        f = self.dir / "1-1.json"
        f.write_text(body)
        return f

    def test_plays_known_sound_and_deletes_event(self) -> None:
        f = self.event('{"sound": "boom"}')
        result = companion.handle(f, self.played.append, {"boom": self.wav})
        self.assertEqual((result, self.played, f.exists()), ("played boom", [self.wav], False))

    def test_unknown_and_broken_events_are_dropped(self) -> None:
        self.assertIn("unknown", companion.handle(self.event('{"sound": "nope"}'), self.played.append, {}))
        self.assertIn("unreadable", companion.handle(self.event("{half"), self.played.append, {}))
        self.assertEqual(self.played, [])


if __name__ == "__main__":
    unittest.main()
