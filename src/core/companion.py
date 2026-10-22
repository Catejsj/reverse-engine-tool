"""RET companion: plays sounds for a running game mod (decision R7).

Sherlock's engine can load a PNG at runtime but not a sound file, so the UE4SS mod writes one tiny JSON
file per sound into ~/ret-work/bridge/events/ ({"sound": "<name>"}, written to .tmp then renamed), and
this loop plays the matching WAV with PipeWire's `pw-play`. Everything stays on this machine and offline.

    .venv/bin/python src/main.py companion        # Ctrl+C to stop
"""

from __future__ import annotations

import json
import shutil
import subprocess
import time
from pathlib import Path
from typing import Callable

WORK = Path.home() / "ret-work"
EVENTS = WORK / "bridge" / "events"
AUDIO = WORK / "root" / "extract" / "audio"

# Event name -> WAV (extracted from Root's own sound bank, never shipped anywhere).
SOUNDS = {
    "ballofviolence": AUDIO / "ui-battle-ballofviolence.wav",
    "death": AUDIO / "unit-genericdeath.wav",
}


def play_with_pipewire(path: Path) -> None:
    subprocess.Popen(["pw-play", str(path)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def handle(event_file: Path, play: Callable[[Path], None], sounds: dict[str, Path] = SOUNDS) -> str:
    """Play one event and delete it. Returns what happened, for the log line and the tests."""
    try:
        name = json.loads(event_file.read_text(encoding="utf-8")).get("sound", "")
    except (OSError, ValueError):
        event_file.unlink(missing_ok=True)
        return f"unreadable event {event_file.name}"
    event_file.unlink(missing_ok=True)
    wav = sounds.get(name)
    if wav is None:
        return f"unknown sound '{name}'"
    if not wav.exists():
        return f"missing {wav}"
    play(wav)
    return f"played {name}"


def run(play: Callable[[Path], None] = play_with_pipewire, poll: float = 0.02) -> None:
    if shutil.which("pw-play") is None and play is play_with_pipewire:
        raise SystemExit("pw-play not found (PipeWire)")
    EVENTS.mkdir(parents=True, exist_ok=True)
    for stale in EVENTS.glob("*"):          # events from a previous session are not replayed
        stale.unlink()
    print(f"ret companion: watching {EVENTS}  (Ctrl+C to stop)")
    while True:
        for f in sorted(EVENTS.glob("*.json")):
            print(handle(f, play))
        time.sleep(poll)
