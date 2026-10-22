#!/usr/bin/env bash
# Build the Spinners of Mercy plugin and copy it into Root's BepInEx/plugins (BepInEx must be installed: deploy.sh).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
PLUGINS="$HOME/.local/share/Steam/steamapps/common/Root/BepInEx/plugins"
[ -d "$PLUGINS" ] || { echo "BepInEx not installed in Root: run deploy.sh and launch Root once"; exit 1; }
dotnet build -c Release "$HERE/SpinnersOfMercy" | tail -1
cp "$HOME/ret-work/root/plugin-build/bin/Release/net6.0/RetSpinnersOfMercy.dll" "$PLUGINS/"
# Where the plugin finds the fan faction's art (Wine path); the art itself stays in ~/ret-work.
[ -f "$HOME/ret-work/root/spinners/raw/relationship.png" ] || echo "warning: Spinners art missing in ~/ret-work/root/spinners/raw"
printf 'Z:%s/ret-work/root/spinners\n' "$HOME" > "$PLUGINS/RetSpinners.path"
echo "Installed. Launch Root (offline) to the main menu, then quit. Log: Root/BepInEx/LogOutput.log"
