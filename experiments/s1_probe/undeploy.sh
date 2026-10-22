#!/usr/bin/env bash
# Remove everything deploy.sh added, and move any UE4SS logs/dumps to ~/ret-work (never the repository).
set -euo pipefail
W="$HOME/.local/share/Steam/steamapps/common/Sherlock Holmes The Awakened/SH3RE/Binaries/Win64"
OUT="$HOME/ret-work/sherlock-holmes-the-awakened/s1-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT"
for f in UE4SS.log UE4SS_ObjectDump.txt; do [ -e "$W/$f" ] && mv "$W/$f" "$OUT/"; done
rm -f "$W/dwmapi.dll" "$W/UE4SS.dll" "$W/UE4SS-settings.ini"
rm -rf "$W/Mods"
echo "removed. logs (if any) in $OUT. Clear the Steam launch option too."
