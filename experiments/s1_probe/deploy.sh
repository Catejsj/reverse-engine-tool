#!/usr/bin/env bash
# RET spike S1: install UE4SS v3.0.1 + the RetS1Probe mod into Sherlock Holmes: The Awakened.
# Adds files only; refuses to overwrite anything. The Steam DRM wrapper on SHTA.exe is not touched.
# UE4SS zip: ~/ret-work/tools/ue4ss/v3.0.1/zip, sha256 4b47d4bceddd2f561a4e395bfa00924ccfc945af576a2d0c613e6537846c57ec
set -euo pipefail
W="$HOME/.local/share/Steam/steamapps/common/Sherlock Holmes The Awakened/SH3RE/Binaries/Win64"
U="$HOME/ret-work/tools/ue4ss/v3.0.1/unpacked"
P="$(cd "$(dirname "$0")" && pwd)"
echo "4b47d4bceddd2f561a4e395bfa00924ccfc945af576a2d0c613e6537846c57ec  $HOME/ret-work/tools/ue4ss/v3.0.1/zip/UE4SS_v3.0.1.zip" | sha256sum -c -
for f in dwmapi.dll UE4SS.dll UE4SS-settings.ini Mods; do
  [ -e "$W/$f" ] && { echo "already there: $f (run undeploy.sh first)"; exit 1; }
done
cp "$U/dwmapi.dll" "$U/UE4SS.dll" "$W/"
cp "$P/UE4SS-settings.ini" "$W/"
mkdir -p "$W/Mods"
cp -r "$U/Mods/shared" "$U/Mods/Keybinds" "$P/RetS1Probe" "$W/Mods/"
cp "$P/mods.txt" "$W/Mods/mods.txt"
echo "installed. Steam launch options for the game:  WINEDLLOVERRIDES=\"dwmapi=n,b\" %command%"
