#!/usr/bin/env bash
# Remove BepInEx from Root: deletes exactly the files deploy.sh recorded, plus what BepInEx generated in BepInEx/.
# Then remove the launch option in Steam. Root's own files are untouched.
set -euo pipefail
ROOT="$HOME/.local/share/Steam/steamapps/common/Root"
LIST="$HOME/ret-work/root/bepinex-installed.txt"
[ -f "$LIST" ] || { echo "no install record at $LIST"; exit 1; }
while read -r f; do rm -f "$ROOT/$f"; done < "$LIST"
rm -rf "$ROOT/BepInEx" "$ROOT/dotnet"
rm -f "$LIST"
echo "BepInEx removed. Clear Root's Steam launch option."
