"""Compose the strike animation: Root's Vagabond leaps into Root's Ball of Violence, brawls inside it, and is
revealed in his victory pose as it clears. Output is one RGBA frame sequence for the in-world panel in Sherlock.

    .venv/bin/python experiments/root_vaga_strike.py

Inputs (all from Root, read-only, already extracted to ~/ret-work):
  * BallOfViolence frames (root_fx_render.py), 512 px, 30 fps
  * P_Chr_Vaga_Default posed by A_Chr_Vaga_Spawn (the leap) and A_Chr_Vaga_VictoryIdle_01, rendered here with one
    fixed framing so he keeps the same size in every frame
Output: ~/ret-work/root/extract/fx/VagabondStrike/frames/####.png (W x H) + manifest.json + preview.gif
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import root_char_render as R  # noqa: E402
from root_char_seq import R_proj, Character  # noqa: E402

FX = Path.home() / "ret-work/root/extract/fx"
OUT = FX / "VagabondStrike"
W, H = 768, 384          # frame size; the cloud centre sits at CLOUD_X
FPS = 30
CLOUD_X, CLOUD_Y = 528, 250
CLOUD_PX = 384           # cloud frames (512) are scaled to this square
VAGA_PX = 150            # the Vagabond's standing height in the frame
# Measured from his feet height per frame: takeoff 0.62 s, apex 1.2 s (feet +0.31 units, ~0.8x his height), lands 1.5 s.
JUMP_CLIP, JUMP_END = "A_Chr_Vaga_Spawn", 1.5
TAKEOFF = 0.6
GROUND_Y = 330                                     # feet line in the frame; leaves room for the apex above
START_X = 110                                      # where he crouches before the leap
HIDE_UNDER_CLOUD = 3                               # cloud frames after which he is inside it
VICTORY_CLIP, VICTORY_FROM, VICTORY_LEN = "A_Chr_Vaga_VictoryIdle_01", 58, 0.9
YAW, PITCH = 270, -15


def ease(x: float) -> float:
    return x * x * (3 - 2 * x)


def main() -> int:
    ch = Character("P_Chr_Vaga_Default", ["vagabondthief", "vagabondcharacter", "vagabond"])
    jump_times = [i / FPS for i in range(int(JUMP_END * FPS))]
    vic_times = [i / FPS for i in range(int(VICTORY_LEN * FPS))]
    jump = [ch.triangles(JUMP_CLIP, t) for t in jump_times]
    vic = [ch.triangles(VICTORY_CLIP, t) for t in vic_times]

    # One framing for every Vagabond frame (union of bounds), so scale and ground line never move.
    bounds = [R.view_bounds(R_proj(tr, YAW, PITCH)) for tr in jump + vic]
    xs = [b[0] - b[2] / 2 for b in bounds] + [b[0] + b[2] / 2 for b in bounds]
    ys = [b[1] - b[2] / 2 for b in bounds] + [b[1] + b[2] / 2 for b in bounds]
    framing = ((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, max(max(xs) - min(xs), max(ys) - min(ys)))
    sprites_jump = [R.render(tr, YAW, PITCH, framing) for tr in jump]
    sprites_vic = [R.render(tr, YAW, PITCH, framing) for tr in vic]

    # Scale from the first (standing) frame: its alpha box gives the standing height and the ground line.
    box = sprites_jump[0].getchannel("A").getbbox()
    k = VAGA_PX / (box[3] - box[1])
    ground_src = box[3]
    size = int(R.SIZE * k)

    def place(frame: Image.Image, sprite: Image.Image, cx: float, ground_y: float, alpha: float = 1.0) -> None:
        s = sprite.resize((size, size), Image.Resampling.LANCZOS)
        if alpha < 1.0:
            s.putalpha(s.getchannel("A").point(lambda v: int(v * alpha)))
        frame.alpha_composite(s, (int(cx - size / 2), int(ground_y - ground_src * k)))

    ground = GROUND_Y                     # the cloud (centre CLOUD_Y) swallows him where he lands
    cloud = sorted((FX / "BallOfViolence/frames").glob("[0-9]*.png"))
    frames: list[Image.Image] = []

    # 1. the leap: horizontal ease from START_X to the cloud while airborne; the clip itself does the height
    for t, sp in zip(jump_times, sprites_jump):
        f = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        u = 0.0 if t < TAKEOFF else ease(min(1.0, (t - TAKEOFF) / (JUMP_END - TAKEOFF)))
        place(f, sp, START_X + (CLOUD_X - START_X) * u, ground)
        frames.append(f)

    # 2. the brawl: cloud over him; he is inside after HIDE_UNDER_CLOUD frames; 3. victory revealed as it clears
    for i, cf in enumerate(cloud):
        f = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        if i < HIDE_UNDER_CLOUD:
            place(f, sprites_jump[-1], CLOUD_X, ground)
        elif i >= VICTORY_FROM:
            j = i - VICTORY_FROM
            fade = 1.0 if j < len(sprites_vic) - 8 else max(0.0, (len(sprites_vic) - j) / 8)
            place(f, sprites_vic[min(j, len(sprites_vic) - 1)], CLOUD_X, ground, fade)
        c = Image.open(cf).resize((CLOUD_PX, CLOUD_PX), Image.Resampling.LANCZOS)
        f.alpha_composite(c, (CLOUD_X - CLOUD_PX // 2, CLOUD_Y - CLOUD_PX // 2))
        frames.append(f)
    for j in range(len(cloud) - VICTORY_FROM, len(sprites_vic)):       # victory tail after the cloud ends
        f = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        fade = max(0.0, (len(sprites_vic) - j) / 8) if j >= len(sprites_vic) - 8 else 1.0
        place(f, sprites_vic[j], CLOUD_X, ground, fade)
        frames.append(f)

    out = OUT / "frames"
    out.mkdir(parents=True, exist_ok=True)
    for old in out.glob("*.png"):
        old.unlink()
    for i, f in enumerate(frames):
        f.save(out / f"{i:04d}.png")
    cloud_start = len(jump_times)
    (out / "manifest.json").write_text(json.dumps({
        "fps": FPS, "count": len(frames), "w": W, "h": H, "cloud_center": [CLOUD_X, CLOUD_Y],
        "cloud_start": cloud_start, "cover_frame": cloud_start + 8,
        "note": "cover_frame: cloud fully covers the target; ragdoll it then"}, indent=1))
    frames[0].save(OUT / "preview.gif", save_all=True, append_images=frames[1:], duration=int(1000 / FPS),
                   loop=0, disposal=2)
    print(f"{len(frames)} frames ({len(frames) / FPS:.1f} s), cloud starts at {cloud_start} -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
