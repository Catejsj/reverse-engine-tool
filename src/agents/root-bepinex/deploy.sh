#!/usr/bin/env bash
# Install BepInEx 6.0.0-be.788 (bleeding edge, Unity IL2CPP, win-x64) into Root (Steam 965580) for the Spinners of Mercy mod (R12).
# Run it yourself; it copies the loader next to Root.exe and records every file it adds, so undeploy.sh removes exactly
# those. Root's own files are never changed. Play OFFLINE only while it is installed.
# Steam launch option for Root afterwards:  WINEDLLOVERRIDES="winhttp=n,b" %command%
set -euo pipefail
ROOT="$HOME/.local/share/Steam/steamapps/common/Root"
SRC="$HOME/ret-work/tools/bepinex/6.0.0-be.788"   # pre.2 failed: its Cpp2IL reads metadata 23-29, Root is 31
ZIP="$SRC/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip"   # builds.bepinex.dev/projects/bepinex_be/788
SHA=f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a
LIST="$HOME/ret-work/root/bepinex-installed.txt"
[ -f "$ROOT/Root.exe" ] || { echo "Root not found at $ROOT"; exit 1; }
[ -e "$ROOT/winhttp.dll" ] && { echo "winhttp.dll already in Root: something else is installed, stopping"; exit 1; }
echo "$SHA  $ZIP" | sha256sum -c - >/dev/null || { echo "BepInEx zip checksum mismatch, stopping"; exit 1; }
cd "$SRC/unpacked"
find . -type f | sed 's#^\./##' | sort > "$LIST"
cp -r --no-clobber . "$ROOT/"
echo "BepInEx installed ($(wc -l < "$LIST") files, list in $LIST)."
echo "Set Root's Steam launch option to:  WINEDLLOVERRIDES=\"winhttp=n,b\" %command%"
echo "First launch takes several minutes (BepInEx generates its interop files). Play offline."
