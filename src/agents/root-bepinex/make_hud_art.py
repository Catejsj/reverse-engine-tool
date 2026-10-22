"""Cut the Spinners of Mercy HUD art out of the fan faction boards (slice H2).

Reads  ~/ret-work/root/spinners/raw/board.png, board_back.png  (fan art; never copied into the repository)
Writes ~/ret-work/root/spinners/hud/<name>.png                 (loaded by the plugin, matched to Root sprite names)

Crop boxes are in the boards' own pixels (3378x2622), checked on a contact sheet. Icons that sit on the board's
background (warrior, web) get a transparent background: flood-filled from the corners for the warrior, a circle mask for
the round web token.
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

RAW = Path.home() / "ret-work/root/spinners/raw"
OUT = Path.home() / "ret-work/root/spinners/hud"

# name: (board file, left, top, right, bottom)
CROPS = {
    "portrait": ("board.png", 2434, 1606, 3363, 2535),        # nurse spider with candle
    "warrior": ("board_back.png", 346, 380, 600, 668),       # manifest warrior
    "web": ("board_back.png", 1867, 470, 2107, 706),         # manifest web token
    "infirmary_mouse": ("board.png", 1952, 1808, 2168, 2021),  # track, +2 column (no icon over it)
    "infirmary_rabbit": ("board.png", 1952, 2050, 2168, 2263),
    "infirmary_fox": ("board.png", 1952, 2290, 2168, 2505),
    "pattern": ("board.png", 0, 1100, 170, 1270),             # teal bandage border
    "btn_gather": ("board.png", 1782, 1885, 1932, 2035),      # card-draw icon on the mouse track (Gather, on the Recruit button since 0.29.0)
    "btn_establish": ("board_back.png", 1005, 380, 1300, 830),  # the three infirmaries (Establish button)
    "btn_shelter": ("vp.png", 0, 0, 212, 212),                # VP marker: nurse spider in laurels (Shelter button)
    "btn_recruit": ("board_back.png", 1867, 470, 2107, 706),  # web token (Recruit, on the Overwork button since 0.29.0)
}


def clear_corners(img: Image.Image, tolerance: int = 60) -> Image.Image:
    """Make the background transparent by flood-filling from each corner (the board's pale pattern)."""
    img = img.convert("RGBA")
    w, h = img.size
    for xy in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        ImageDraw.floodfill(img, xy, (0, 0, 0, 0), thresh=tolerance)
    return img


def circle_mask(img: Image.Image, inset: int = 4) -> Image.Image:
    """Keep only the round token."""
    img = img.convert("RGBA")
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).ellipse((inset, inset, img.size[0] - inset, img.size[1] - inset), fill=255)
    img.putalpha(mask)
    return img


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    boards = {name: Image.open(RAW / name).convert("RGBA") for name in {c[0] for c in CROPS.values()}}
    for name, (board, left, top, right, bottom) in CROPS.items():
        img = boards[board].crop((left, top, right, bottom))
        if name in ("warrior", "btn_establish"):
            img = clear_corners(img)
        elif name in ("web", "btn_recruit"):
            img = circle_mask(img)
        img.save(OUT / f"{name}.png")
        print(f"{name}: {img.size[0]}x{img.size[1]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
