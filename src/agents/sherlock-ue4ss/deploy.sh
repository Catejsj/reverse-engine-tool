#!/usr/bin/env bash
# Install the RetVagabond Lua mod into Sherlock Holmes: The Awakened (UE4SS must already be installed by
# experiments/s1_probe/deploy.sh). Text files only. Fills in the Wine path of ~/ret-work, so the mod reads its
# cloud frames from there and nothing extracted from Root is copied into the game folder.
set -euo pipefail
W="$HOME/.local/share/Steam/steamapps/common/Sherlock Holmes The Awakened/SH3RE/Binaries/Win64"
SRC="$(cd "$(dirname "$0")" && pwd)/RetVagabond"
[ -f "$W/UE4SS.dll" ] || { echo "UE4SS not installed: run experiments/s1_probe/deploy.sh first"; exit 1; }
[ -f "$HOME/ret-work/root/extract/fx/VagabondStrike/frames/0129.png" ] || { echo "strike frames missing: run experiments/root_fx_render.py then root_vaga_strike.py"; exit 1; }
mkdir -p "$W/Mods/RetVagabond/Scripts" "$HOME/ret-work/bridge/events"
sed "s#@RET_WORK@#Z:$HOME/ret-work#" "$SRC/Scripts/main.lua" > "$W/Mods/RetVagabond/Scripts/main.lua"
printf 'Keybinds : 1\nRetS1Probe : 0\nRetVagabond : 1\n' > "$W/Mods/mods.txt"
echo "RetVagabond installed. Start the sound helper first:  .venv/bin/python src/main.py companion"
